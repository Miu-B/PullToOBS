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
    private readonly ICondition _condition;
    private readonly IPluginLog _log;

    private bool _isInCombat;
    private CancellationTokenSource? _pendingStopCts;
    private CancellationTokenSource? _disposalCts;
    private readonly object _lock = new();
    private bool _isDisposed;

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
        _disposalCts = new CancellationTokenSource();
    }

    /// <summary>
    /// Call this every frame from the game thread (Framework.Update).
    /// Polls the Dalamud condition flag for combat state changes.
    /// </summary>
    public void Update()
    {
        var inCombat = _condition[ConditionFlag.InCombat];

        CancellationTokenSource? stopCtsToCancel = null;
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
                stopCtsToCancel = _pendingStopCts;
                _pendingStopCts = null;
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

        // Cancel outside the lock to avoid deadlock
        stopCtsToCancel?.Cancel();
        stopCtsToCancel?.Dispose();

        if (shouldStart)
            _ = Task.Run(HandleEncounterStart);
        else if (shouldEnd)
            _ = Task.Run(HandleEncounterEnd);
    }

    private async Task HandleEncounterStart()
    {
        CancellationToken disposalToken;

        lock (_lock)
        {
            if (_isDisposed) return;
            disposalToken = _disposalCts!.Token;
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

            // Save replay buffer after overlap delay to capture the prepull.
            // Cancellable via disposal token so we don't access disposed objects.
            await Task.Delay(ReplayBufferSaveDelay, disposalToken);

            if (_obs.IsConnected && _obs.IsRecording && _obs.IsReplayBufferConfigured)
            {
                _log.Information("[Encounter] HandleEncounterStart: calling SaveReplayBuffer");
                _obs.SaveReplayBuffer();
                _log.Information("[Encounter] HandleEncounterStart: SaveReplayBuffer called successfully");
            }

            EncounterStarted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            _log.Debug("[Encounter] HandleEncounterStart: cancelled (plugin disposing)");
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] HandleEncounterStart exception: {ex}");
            ErrorOccurred?.Invoke($"Error starting encounter: {ex.Message}");
        }
    }

    private async Task HandleEncounterEnd()
    {
        CancellationTokenSource cts;
        CancellationToken disposalToken;

        lock (_lock)
        {
            if (_isDisposed) return;
            cts = new CancellationTokenSource();
            _pendingStopCts = cts;
            disposalToken = _disposalCts!.Token;
        }

        try
        {
            _log.Information($"[Encounter] HandleEncounterEnd: obs.IsConnected={_obs.IsConnected}, obs.IsRecording={_obs.IsRecording}");

            if (!_obs.IsConnected || !_obs.IsRecording)
            {
                _log.Warning("[Encounter] HandleEncounterEnd: OBS not connected or not recording, firing EncounterEnded without stopping");
                EncounterEnded?.Invoke();
                return;
            }

            // Wait grace period -- cancelled if combat restarts or plugin disposes
            _log.Information("[Encounter] HandleEncounterEnd: waiting before stopping recording");
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, disposalToken);
            await Task.Delay(CombatEndGracePeriod, linkedCts.Token);

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
        catch (OperationCanceledException)
        {
            // Combat restarted or plugin disposing -- continue recording, do not stop
            _log.Information("[Encounter] HandleEncounterEnd: cancelled (combat restarted or disposing), continuing recording");
        }
        catch (Exception ex)
        {
            _log.Error($"[Encounter] HandleEncounterEnd exception: {ex}");
            ErrorOccurred?.Invoke($"Error ending encounter: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_pendingStopCts, cts))
                    _pendingStopCts = null;
            }
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? pendingCts;
        CancellationTokenSource? disposalCts;

        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            pendingCts = _pendingStopCts;
            _pendingStopCts = null;

            disposalCts = _disposalCts;
            _disposalCts = null;
        }

        // Cancel all pending operations so background tasks exit promptly
        disposalCts?.Cancel();
        disposalCts?.Dispose();

        pendingCts?.Cancel();
        pendingCts?.Dispose();
    }
}
