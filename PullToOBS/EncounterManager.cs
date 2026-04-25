using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace PullToOBS;

public class EncounterManager : IDisposable
{
    /// <summary>Delay before saving the replay buffer after recording starts.</summary>
    private static readonly TimeSpan ReplayBufferSaveDelay = TimeSpan.FromSeconds(5);

    /// <summary>Grace period after combat ends before stopping the recording.</summary>
    private static readonly TimeSpan CombatEndGracePeriod = TimeSpan.FromSeconds(5);

    private readonly IOBSController _obs;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly ICondition _condition;
    private readonly IPluginLog _log;

    private bool _isInCombat;
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// When true, combat-triggered recording is suppressed.
    /// Runtime-only (not persisted) - resets to false on plugin startup.
    /// Toggled via the /pto rec command.
    /// </summary>
    public bool IsStandby { get; set; }

    /// <summary>
    /// Tracks whether we initiated recording. Protected by <see cref="_lock"/>.
    /// Set true when we call StartRecording, false when we call StopRecording
    /// or determine we should not be recording.
    /// </summary>
    private bool _weStartedRecording;
    private DateTimeOffset? _encounterStartedAt;
    private uint _encounterTerritoryType;
    private string? _encounterJobAbbreviation;
    private string? _replayBufferPath;
    private long _encounterSequence;
    private Task<string?>? _pendingReplayBufferSaveTask;

    /// <summary>
    /// Kernel-backed timer for the grace period before stopping recording.
    /// Fires reliably regardless of thread pool pressure.
    /// </summary>
    private System.Timers.Timer? _gracePeriodTimer;

    /// <summary>
    /// Kernel-backed timer for the replay buffer save delay after starting recording.
    /// </summary>
    private System.Timers.Timer? _replayBufferTimer;

    public bool IsInCombat => _isInCombat;

    public event Action? EncounterStarted;
    public event Action<EncounterRecord>? EncounterEnded;
    public event Action<string>? ErrorOccurred;
    public event Action? StateChanged;

    public EncounterManager(IOBSController obs, IClientState clientState, IPlayerState playerState, ICondition condition, IPluginLog log)
    {
        _obs = obs;
        _clientState = clientState;
        _playerState = playerState;
        _condition = condition;
        _log = log;
    }

    /// <summary>
    /// Call this every frame from the game thread (Framework.Update).
    /// Polls the Dalamud condition flag for combat state changes.
    /// </summary>
    public void Update()
    {
        var inCombat = _condition[ConditionFlag.InCombat];

        bool shouldStart = false;
        bool shouldEnd = false;
        bool fireStateChanged = false;

        lock (_lock)
        {
            if (_isDisposed) return;

            if (inCombat == _isInCombat) return;

            _log.Debug($"[Encounter] Combat state changed: inCombat={inCombat}, wasInCombat={_isInCombat}");

            _isInCombat = inCombat;
            fireStateChanged = true;

            // In standby mode, track combat state but skip recording actions.
            if (IsStandby)
            {
                _log.Debug("[Encounter] Standby mode active - skipping recording actions");
            }
            else if (inCombat)
            {
                // Entering combat - cancel any pending stop
                CancelGracePeriodTimer();
                shouldStart = true;
                _log.Debug("[Encounter] Entering combat, will start encounter");
            }
            else
            {
                shouldEnd = true;
                _log.Debug("[Encounter] Leaving combat, will end encounter");
            }
        }

        // Fire events outside the lock to avoid potential deadlocks
        if (fireStateChanged)
            StateChanged?.Invoke();

        if (shouldStart)
            HandleEncounterStart();
        else if (shouldEnd)
            HandleEncounterEnd();
    }

    /// <summary>
    /// Starts recording immediately (off the game thread via ThreadPool.QueueUserWorkItem)
    /// and schedules a replay buffer save after the configured delay.
    /// </summary>
    private void HandleEncounterStart()
    {
        lock (_lock)
        {
            CancelReplayBufferTimer();

            if (_weStartedRecording)
            {
                _log.Debug("[Encounter] HandleEncounterStart: already in a recording session we started, skipping");
                return;
            }

            _encounterStartedAt = DateTimeOffset.Now;
            _encounterTerritoryType = _clientState.TerritoryType;
            _encounterJobAbbreviation = _clientState.IsLoggedIn ? _playerState.ClassJob.ValueNullable?.Abbreviation.ToString() : null;
            _replayBufferPath = null;
            _pendingReplayBufferSaveTask = null;
            _encounterSequence++;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (_isDisposed) return;

                // Re-check under lock - the grace period callback may have run between
                // our initial check and this thread pool work item executing.
                if (_weStartedRecording)
                {
                    _log.Debug("[Encounter] HandleEncounterStart: already in a recording session (re-check), skipping");
                    return;
                }

                if (!_isInCombat || IsStandby)
                {
                    _log.Debug("[Encounter] HandleEncounterStart: combat ended or standby was enabled before recording could start, skipping");
                    return;
                }
            }

            try
            {
                _log.Debug($"[Encounter] HandleEncounterStart: obs.IsConnected={_obs.IsConnected}");

                if (!_obs.IsConnected)
                {
                    _log.Warning("[Encounter] HandleEncounterStart: OBS not connected, aborting");
                    lock (_lock)
                    {
                        ResetEncounterContext();
                    }
                    return;
                }

                _log.Debug("[Encounter] HandleEncounterStart: calling StartRecording");
                _obs.StartRecording();
                _log.Debug("[Encounter] HandleEncounterStart: StartRecording called successfully");

                lock (_lock)
                {
                    _weStartedRecording = true;
                }

                // Schedule replay buffer save via a kernel-backed timer.
                ScheduleReplayBufferSave();
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    ResetEncounterContext();
                }

                _log.Error($"[Encounter] HandleEncounterStart exception: {ex}");
                ErrorOccurred?.Invoke($"Error starting encounter: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Schedules a replay buffer save after <see cref="ReplayBufferSaveDelay"/>.
    /// Uses a kernel-backed timer to avoid thread pool scheduling delays.
    /// </summary>
    private void ScheduleReplayBufferSave()
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            CancelReplayBufferTimer();

            var timer = new System.Timers.Timer(ReplayBufferSaveDelay.TotalMilliseconds);
            timer.AutoReset = false;
            timer.Elapsed += OnReplayBufferTimerElapsed;
            _replayBufferTimer = timer;
            timer.Start();

            _log.Debug("[Encounter] HandleEncounterStart: scheduled replay buffer save");
        }
    }

    /// <summary>
    /// Fires when the replay buffer save delay has elapsed.
    /// Runs on a thread pool thread (fired by the kernel timer), then saves the replay buffer.
    /// </summary>
    private async void OnReplayBufferTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        long encounterSequence;

        lock (_lock)
        {
            if (_isDisposed) return;
            encounterSequence = _encounterSequence;
        }

        try
        {
            if (_obs.IsConnected && _obs.IsRecording && _obs.IsReplayBufferConfigured)
            {
                _log.Debug("[Encounter] HandleEncounterStart: calling SaveReplayBuffer");
                var replayBufferSaveTask = _obs.SaveReplayBuffer();

                lock (_lock)
                {
                    if (_isDisposed || _encounterStartedAt is null || _encounterSequence != encounterSequence)
                        return;

                    _pendingReplayBufferSaveTask = replayBufferSaveTask;
                }

                var replayBufferPath = await TrackReplayBufferSaveAsync(replayBufferSaveTask, encounterSequence);
                _log.Debug($"[Encounter] HandleEncounterStart: SaveReplayBuffer completed (path={replayBufferPath ?? "<null>"})");
            }

            EncounterStarted?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] SaveReplayBuffer exception: {ex}");
            ErrorOccurred?.Invoke($"Error saving replay buffer: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the grace period timer. When it fires, the recording will be stopped
    /// (unless combat resumes first, which cancels the timer).
    /// </summary>
    private void HandleEncounterEnd()
    {
        var shouldFinalizeWithoutStop = false;
        long encounterSequence = 0;

        lock (_lock)
        {
            if (_isDisposed) return;

            _log.Debug($"[Encounter] HandleEncounterEnd: obs.IsConnected={_obs.IsConnected}, obs.IsRecording={_obs.IsRecording}");

            if (!_obs.IsConnected || !_obs.IsRecording)
            {
                if (_encounterStartedAt is null)
                {
                    _log.Debug("[Encounter] HandleEncounterEnd: OBS not connected or not recording, and no encounter context exists");
                    ResetEncounterContext();
                    return;
                }

                _log.Debug("[Encounter] HandleEncounterEnd: OBS not connected or not recording, finalizing encounter without stopping");
                encounterSequence = _encounterSequence;
                _weStartedRecording = false;
                shouldFinalizeWithoutStop = true;
            }
            else
            {
                _log.Debug("[Encounter] HandleEncounterEnd: starting grace period timer");

                CancelGracePeriodTimer();

                var timer = new System.Timers.Timer(CombatEndGracePeriod.TotalMilliseconds);
                timer.AutoReset = false;
                timer.Elapsed += OnGracePeriodElapsed;
                _gracePeriodTimer = timer;
                timer.Start();

                return;
            }
        }

        if (shouldFinalizeWithoutStop)
            _ = FinalizeEncounterWithoutStopAsync(encounterSequence);
    }

    /// <summary>
    /// Fires when the grace period has elapsed without combat resuming.
    /// Runs on a thread pool thread (fired by the kernel timer), then stops the recording.
    /// </summary>
    private async void OnGracePeriodElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        long encounterSequence;

        lock (_lock)
        {
            if (_isDisposed) return;

            // If we didn't start this recording, or combat resumed and cancelled us, bail out.
            if (!_weStartedRecording)
            {
                _log.Debug("[Encounter] HandleEncounterEnd: _weStartedRecording is false, skipping stop");
                return;
            }

            encounterSequence = _encounterSequence;
            _weStartedRecording = false;
        }

        try
        {
            await AwaitPendingReplayBufferSaveAsync();

            // Re-check after grace period - recording may have stopped externally
            if (!_obs.IsConnected || !_obs.IsRecording)
            {
                _log.Debug("[Encounter] HandleEncounterEnd: recording already stopped during grace period, finalizing encounter without stopping");
                var encounterRecordWithoutStop = CompleteEncounter(null, encounterSequence);
                if (encounterRecordWithoutStop is not null)
                    EncounterEnded?.Invoke(encounterRecordWithoutStop);

                return;
            }

            _log.Debug("[Encounter] HandleEncounterEnd: calling StopRecording");
            var recordingPath = _obs.StopRecording();
            _log.Debug($"[Encounter] HandleEncounterEnd: StopRecording called successfully (path={recordingPath ?? "<null>"})");

            var encounterRecord = CompleteEncounter(recordingPath, encounterSequence);
            if (encounterRecord is not null)
                EncounterEnded?.Invoke(encounterRecord);
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] HandleEncounterEnd exception: {ex}");
            ErrorOccurred?.Invoke($"Error ending encounter: {ex.Message}");
        }
    }

    private async Task FinalizeEncounterWithoutStopAsync(long encounterSequence)
    {
        try
        {
            await AwaitPendingReplayBufferSaveAsync();

            var encounterRecord = CompleteEncounter(null, encounterSequence);
            if (encounterRecord is not null)
                EncounterEnded?.Invoke(encounterRecord);
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] FinalizeEncounterWithoutStopAsync exception: {ex}");
            ErrorOccurred?.Invoke($"Error finalizing encounter: {ex.Message}");
        }
    }

    private async Task<string?> TrackReplayBufferSaveAsync(Task<string?> replayBufferSaveTask, long encounterSequence)
    {
        try
        {
            var replayBufferPath = await replayBufferSaveTask;

            lock (_lock)
            {
                if (_isDisposed || _encounterStartedAt is null || _encounterSequence != encounterSequence)
                    return replayBufferPath;

                if (ReferenceEquals(_pendingReplayBufferSaveTask, replayBufferSaveTask))
                    _pendingReplayBufferSaveTask = null;

                _replayBufferPath = replayBufferPath;
            }

            return replayBufferPath;
        }
        catch
        {
            lock (_lock)
            {
                if (_encounterSequence == encounterSequence && ReferenceEquals(_pendingReplayBufferSaveTask, replayBufferSaveTask))
                    _pendingReplayBufferSaveTask = null;
            }

            throw;
        }
    }

    private async Task AwaitPendingReplayBufferSaveAsync()
    {
        Task<string?>? replayBufferSaveTask;

        lock (_lock)
        {
            replayBufferSaveTask = _pendingReplayBufferSaveTask;
        }

        if (replayBufferSaveTask is null)
            return;

        try
        {
            await replayBufferSaveTask;
        }
        catch (Exception ex)
        {
            _log.Debug($"[Encounter] AwaitPendingReplayBufferSaveAsync ignored save error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private EncounterRecord? CompleteEncounter(string? recordingPath, long encounterSequence)
    {
        lock (_lock)
        {
            if (_encounterStartedAt is null || _encounterSequence != encounterSequence)
                return null;

            var encounterRecord = new EncounterRecord(
                _encounterStartedAt.Value,
                _encounterTerritoryType,
                _encounterJobAbbreviation,
                recordingPath,
                _replayBufferPath);

            ResetEncounterContext();
            return encounterRecord;
        }
    }

    private void ResetEncounterContext()
    {
        CancelReplayBufferTimer();
        _encounterStartedAt = null;
        _encounterTerritoryType = 0;
        _encounterJobAbbreviation = null;
        _replayBufferPath = null;
        _pendingReplayBufferSaveTask = null;
    }

    /// <summary>Cancels and disposes the grace period timer if active. Must be called under lock.</summary>
    private void CancelGracePeriodTimer()
    {
        if (_gracePeriodTimer != null)
        {
            _log.Debug("[Encounter] Grace period timer cancelled");
            _gracePeriodTimer.Stop();
            _gracePeriodTimer.Elapsed -= OnGracePeriodElapsed;
            _gracePeriodTimer.Dispose();
            _gracePeriodTimer = null;
        }
    }

    /// <summary>Cancels and disposes the replay buffer timer if active. Must be called under lock.</summary>
    private void CancelReplayBufferTimer()
    {
        if (_replayBufferTimer != null)
        {
            _replayBufferTimer.Stop();
            _replayBufferTimer.Elapsed -= OnReplayBufferTimerElapsed;
            _replayBufferTimer.Dispose();
            _replayBufferTimer = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelGracePeriodTimer();
            CancelReplayBufferTimer();
        }
    }
}
