using System;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace PullToOBS;

public class OBSController : IOBSController
{
    private const int StatePollingIntervalMs = 500;

    private readonly OBSWebsocket _obs;
    private readonly IPluginLog _log;
    private bool _isDisposed;
    private System.Timers.Timer? _statePollingTimer;

    // Volatile ensures cross-thread visibility (timer thread writes, UI thread reads).
    private volatile bool _isRecording;
    private volatile bool _isReplayBufferActive;
    private volatile bool _isReplayBufferConfigured;

    public bool IsConnected => _obs.IsConnected;
    public bool IsRecording => _isRecording;
    public bool IsReplayBufferActive => _isReplayBufferActive;
    public bool IsReplayBufferConfigured => _isReplayBufferConfigured;

    public event Action? ConnectionStateChanged;
    public event Action? RecordingStateChanged;
    public event Action? ReplayBufferStateChanged;
    public event Action<string>? ErrorOccurred;

    public OBSController(IPluginLog log)
    {
        _log = log;
        _obs = new OBSWebsocket();
        _obs.Connected += OnConnected;
        _obs.Disconnected += OnDisconnected;
    }

    public async Task ConnectAsync(string url, string password)
    {
        if (_obs.IsConnected)
            Disconnect();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnConnected(object? s, EventArgs e) => tcs.TrySetResult(true);
        void OnDisconnected(object? s, ObsDisconnectionInfo e) =>
            tcs.TrySetException(new Exception(e.DisconnectReason ?? "Connection failed"));

        _obs.Connected += OnConnected;
        _obs.Disconnected += OnDisconnected;

        try
        {
            _obs.ConnectAsync(url, password);
            await tcs.Task;

            CheckReplayBufferConfiguration();
            if (_isReplayBufferConfigured)
                TryStartReplayBuffer();
            StartStatePolling();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to connect to OBS: {ex.Message}");
            throw;
        }
        finally
        {
            _obs.Connected -= OnConnected;
            _obs.Disconnected -= OnDisconnected;
        }
    }

    private void StartStatePolling()
    {
        _statePollingTimer = new System.Timers.Timer(StatePollingIntervalMs);
        _statePollingTimer.Elapsed += (_, _) => PollState();
        _statePollingTimer.Start();
    }

    private void PollState()
    {
        if (!_obs.IsConnected) return;

        try
        {
            var recordStatus = _obs.GetRecordStatus();
            var wasRecording = _isRecording;
            _isRecording = recordStatus.IsRecording;
            if (wasRecording != _isRecording)
                RecordingStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Debug($"[OBS] PollState recording error: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var wasActive = _isReplayBufferActive;
            _isReplayBufferActive = _obs.GetReplayBufferStatus();
            if (wasActive != _isReplayBufferActive)
                ReplayBufferStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Debug($"[OBS] PollState replay buffer error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        StopStatePolling();

        if (_obs.IsConnected)
            _obs.Disconnect();
    }

    private void StopStatePolling()
    {
        _statePollingTimer?.Stop();
        _statePollingTimer?.Dispose();
        _statePollingTimer = null;
    }

    private void CheckReplayBufferConfiguration()
    {
        try
        {
            var isActive = _obs.GetReplayBufferStatus();
            _isReplayBufferConfigured = true;
            _isReplayBufferActive = isActive;
        }
        catch (Exception ex)
        {
            _log.Debug($"[OBS] CheckReplayBufferConfiguration: not configured ({ex.GetType().Name}: {ex.Message})");
            _isReplayBufferConfigured = false;
            _isReplayBufferActive = false;
        }
    }

    private void TryStartReplayBuffer()
    {
        if (_isReplayBufferActive) return;

        try
        {
            _obs.StartReplayBuffer();
            _isReplayBufferActive = true;
            ReplayBufferStateChanged?.Invoke();
            _log.Debug("[OBS] TryStartReplayBuffer: started successfully");
        }
        catch (Exception ex) when (IsAlreadyRunningError(ex))
        {
            _log.Debug("[OBS] TryStartReplayBuffer: replay buffer was already running");
            _isReplayBufferActive = true;
            ReplayBufferStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Warning($"[OBS] TryStartReplayBuffer: could not auto-start: {ex.Message}");
            ErrorOccurred?.Invoke($"Could not auto-start replay buffer: {ex.Message}");
        }
    }

    public void StartReplayBuffer()
    {
        ExecuteObsAction(
            "StartReplayBuffer",
            () =>
            {
                _obs.StartReplayBuffer();
                _isReplayBufferActive = true;
                ReplayBufferStateChanged?.Invoke();
            });
    }

    public void StopReplayBuffer()
    {
        ExecuteObsAction(
            "StopReplayBuffer",
            () =>
            {
                _obs.StopReplayBuffer();
                _isReplayBufferActive = false;
                ReplayBufferStateChanged?.Invoke();
            });
    }

    public void SaveReplayBuffer()
    {
        _log.Debug($"[OBS] SaveReplayBuffer called: IsConnected={_obs.IsConnected}, IsReplayBufferActive={_isReplayBufferActive}");
        ExecuteObsAction(
            "SaveReplayBuffer",
            () => _obs.SaveReplayBuffer());
    }

    public void StartRecording()
    {
        _log.Debug($"[OBS] StartRecording called: IsConnected={_obs.IsConnected}, IsRecording={_isRecording}");
        ExecuteObsAction(
            "StartRecording",
            () =>
            {
                _obs.StartRecord();
                _isRecording = true;
                RecordingStateChanged?.Invoke();
            });
    }

    public void StopRecording()
    {
        _log.Debug($"[OBS] StopRecording called: IsConnected={_obs.IsConnected}, IsRecording={_isRecording}");
        ExecuteObsAction(
            "StopRecording",
            () =>
            {
                _obs.StopRecord();
                _isRecording = false;
                RecordingStateChanged?.Invoke();
            });
    }

    /// <summary>
    /// Executes an OBS action with standard connection check, logging, and error handling.
    /// </summary>
    private void ExecuteObsAction(string operationName, Action action)
    {
        if (!_obs.IsConnected)
        {
            _log.Debug($"[OBS] {operationName}: not connected, skipping");
            return;
        }

        try
        {
            action();
            _log.Debug($"[OBS] {operationName}: succeeded");
        }
        catch (Exception ex)
        {
            _log.Error($"[OBS] {operationName} failed: {ex.GetType().Name}: {ex.Message}");
            ErrorOccurred?.Invoke($"Failed to {operationName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Checks if the exception indicates the resource is already in the requested state.
    /// OBS WebSocket uses error code 500 for "already running" conditions.
    /// </summary>
    private static bool IsAlreadyRunningError(Exception ex)
    {
        // obs-websocket-dotnet wraps the error code in the message.
        // Check both the message and inner exception for robustness.
        return ex.Message.Contains("500") ||
               (ex.InnerException?.Message.Contains("500") ?? false);
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        ConnectionStateChanged?.Invoke();
    }

    private void OnDisconnected(object? sender, ObsDisconnectionInfo e)
    {
        _isRecording = false;
        _isReplayBufferActive = false;
        StopStatePolling();
        ConnectionStateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        StopStatePolling();

        _obs.Connected -= OnConnected;
        _obs.Disconnected -= OnDisconnected;

        if (_obs.IsConnected)
            _obs.Disconnect();

        _isDisposed = true;
    }
}
