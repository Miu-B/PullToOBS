using System;
using System.Threading;
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
    private readonly ICondition _condition;
    private readonly IPluginLog _log;

    private bool _isInCombat;
    private readonly object _lock = new();
    private bool _isDisposed;

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
    public event Action? EncounterEnded;
    public event Action<string>? ErrorOccurred;
    public event Action? StateChanged;

    public EncounterManager(IOBSController obs, ICondition condition, IPluginLog log)
    {
        _obs = obs;
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

            _log.Information($"[Encounter] Combat state changed: inCombat={inCombat}, wasInCombat={_isInCombat}");

            if (inCombat && !_isInCombat)
            {
                // Entering combat -- cancel any pending stop
                CancelGracePeriodTimer();
                shouldStart = true;
                _log.Information("[Encounter] Entering combat, will start encounter");
            }
            else if (!inCombat && _isInCombat)
            {
                shouldEnd = true;
                _log.Information("[Encounter] Leaving combat, will end encounter");
            }

            _isInCombat = inCombat;
            fireStateChanged = true;
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
        // Cancel any pending replay buffer save from a previous encounter
        lock (_lock)
        {
            CancelReplayBufferTimer();
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (_isDisposed) return;
            }

            try
            {
                _log.Information($"[Encounter] HandleEncounterStart: obs.IsConnected={_obs.IsConnected}, obs.IsRecording={_obs.IsRecording}");

                if (!_obs.IsConnected)
                {
                    _log.Warning("[Encounter] HandleEncounterStart: OBS not connected, aborting");
                    return;
                }

                _log.Information("[Encounter] HandleEncounterStart: calling StartRecording");
                _obs.StartRecording();
                _log.Information("[Encounter] HandleEncounterStart: StartRecording called successfully");

                // Schedule replay buffer save via a kernel-backed timer
                ScheduleReplayBufferSave();
            }
            catch (Exception ex)
            {
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

            _log.Information("[Encounter] HandleEncounterStart: scheduled replay buffer save");
        }
    }

    /// <summary>
    /// Fires when the replay buffer save delay has elapsed.
    /// Runs on a thread pool thread (fired by the kernel timer), then saves the replay buffer.
    /// </summary>
    private void OnReplayBufferTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
        }

        try
        {
            if (_obs.IsConnected && _obs.IsRecording && _obs.IsReplayBufferConfigured)
            {
                _log.Information("[Encounter] HandleEncounterStart: calling SaveReplayBuffer");
                _obs.SaveReplayBuffer();
                _log.Information("[Encounter] HandleEncounterStart: SaveReplayBuffer called successfully");
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
        lock (_lock)
        {
            if (_isDisposed) return;

            _log.Information($"[Encounter] HandleEncounterEnd: obs.IsConnected={_obs.IsConnected}, obs.IsRecording={_obs.IsRecording}");

            if (!_obs.IsConnected || !_obs.IsRecording)
            {
                _log.Warning("[Encounter] HandleEncounterEnd: OBS not connected or not recording, firing EncounterEnded without stopping");
                // Fire outside the lock below
            }
            else
            {
                _log.Information("[Encounter] HandleEncounterEnd: starting grace period timer");

                CancelGracePeriodTimer();

                var timer = new System.Timers.Timer(CombatEndGracePeriod.TotalMilliseconds);
                timer.AutoReset = false;
                timer.Elapsed += OnGracePeriodElapsed;
                _gracePeriodTimer = timer;
                timer.Start();

                return;
            }
        }

        EncounterEnded?.Invoke();
    }

    /// <summary>
    /// Fires when the grace period has elapsed without combat resuming.
    /// Runs on a thread pool thread (fired by the kernel timer), then stops the recording.
    /// </summary>
    private void OnGracePeriodElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
        }

        try
        {
            // Re-check after grace period -- recording may have stopped externally
            if (!_obs.IsConnected || !_obs.IsRecording)
            {
                _log.Information("[Encounter] HandleEncounterEnd: recording already stopped during grace period, firing EncounterEnded without stopping");
                EncounterEnded?.Invoke();
                return;
            }

            _log.Information("[Encounter] HandleEncounterEnd: calling StopRecording");
            _obs.StopRecording();
            _log.Information("[Encounter] HandleEncounterEnd: StopRecording called successfully");
            EncounterEnded?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] HandleEncounterEnd exception: {ex}");
            ErrorOccurred?.Invoke($"Error ending encounter: {ex.Message}");
        }
    }

    /// <summary>Cancels and disposes the grace period timer if active. Must be called under lock.</summary>
    private void CancelGracePeriodTimer()
    {
        if (_gracePeriodTimer != null)
        {
            _log.Information("[Encounter] Grace period timer cancelled");
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
