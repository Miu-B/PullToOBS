using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using PullToOBS.Models;

namespace PullToOBS;

/// <summary>
/// Connects to IINACT via Dalamud IPC (in-process, no WebSocket).
/// IINACT exposes a subscriber API: we register a call gate that IINACT invokes
/// to push events to us, and we send subscription requests back through IINACT's gate.
///
/// All IPC calls MUST happen on the game/framework thread (via Framework.Update).
/// Call TryConnect() and TryHeartbeat() each frame from the framework update handler.
/// </summary>
public class IINACTIpcClient : IIINACTClient
{
    // The name of the call gate we register for IINACT to push events into.
    private const string SinkGateName = "PullToOBS.IINACTSink";

    // IINACT IPC gate names
    private const string IpcCreateSubscriber = "IINACT.CreateSubscriber";
    private const string IpcUnsubscribe = "IINACT.Unsubscribe";
    private const string IpcVersion = "IINACT.Version";

    // Events we want from IINACT
    private static readonly string[] SubscribeEvents = ["InCombat"];

    // Subscribe message sent to IINACT's IPC handler via the per-client provider gate.
    private const string SubscribeMessageJson =
        "{\"call\":\"subscribe\",\"events\":[\"InCombat\"]}";
    private static readonly JObject SubscribeMessage = JObject.Parse(SubscribeMessageJson);

    // IINACT readiness check gate
    private const string IpcServerListening = "IINACT.Server.Listening";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    private readonly ICallGateProvider<JObject, bool> _sinkGate;

    private bool _isDisposed;
    private bool _isConnected;

    // Retry throttle: don't hammer IINACT every frame
    private DateTime _lastConnectAttempt = DateTime.MinValue;
    private static readonly TimeSpan ConnectRetryInterval = TimeSpan.FromSeconds(2);

    // Heartbeat throttle
    private DateTime _lastHeartbeat = DateTime.MinValue;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public bool IsConnected => _isConnected;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<InCombatPayload>? CombatStateChanged;
    public event Action<string>? ErrorOccurred;

    public IINACTIpcClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _log = log;

        // Register our event sink gate once. IINACT's IpcHandler.Send calls InvokeFunc on
        // this gate to push events to us. Must be registered before CreateSubscriber.
        _sinkGate = _pluginInterface.GetIpcProvider<JObject, bool>(SinkGateName);
        _sinkGate.RegisterFunc(OnIINACTEvent);
    }

    /// <summary>
    /// Call this every frame from the game thread (Framework.Update).
    /// If not connected, attempts to connect with a throttled retry interval.
    /// </summary>
    public void TryConnect()
    {
        if (_isDisposed || _isConnected) return;
        if (DateTime.UtcNow - _lastConnectAttempt < ConnectRetryInterval) return;

        _lastConnectAttempt = DateTime.UtcNow;

        try
        {
            // Step 1: Check that IINACT's server is ready before attempting anything.
            // LMeter does this check too; without it, CreateSubscriber may succeed but
            // the subscription message may arrive before IINACT is fully initialised.
            var isListening = _pluginInterface
                .GetIpcSubscriber<bool>(IpcServerListening)
                .InvokeFunc();

            if (!isListening)
            {
                _log.Debug("[IINACT] Server not listening yet, will retry");
                return;
            }

            // Step 2: Clear any stale registration from a previous plugin load.
            TryUnsubscribe();

            // Step 3: Ask IINACT to create a subscriber handler for our sink gate.
            var ok = _pluginInterface
                .GetIpcSubscriber<string, bool>(IpcCreateSubscriber)
                .InvokeFunc(SinkGateName);
            _log.Debug($"[IINACT] CreateSubscriber returned: {ok}");

            if (!ok)
            {
                _log.Debug("[IINACT] IINACT not ready yet (CreateSubscriber=false), will retry");
                return;
            }

            // Step 4: Send the subscribe message through IINACT's per-client provider gate.
            _log.Debug($"[IINACT] Sending subscribe: {SubscribeMessage}");
            _pluginInterface
                .GetIpcSubscriber<JObject, bool>($"IINACT.IpcProvider.{SinkGateName}")
                .InvokeAction(SubscribeMessage);

            _isConnected = true;
            Connected?.Invoke();
            _log.Info("[IINACT] Connected to IINACT via IPC, subscribed to: " +
                      string.Join(", ", SubscribeEvents));
        }
        catch (IpcNotReadyError)
        {
            _log.Debug("[IINACT] IPC not ready (IpcNotReadyError), will retry");
        }
        catch (Exception ex)
        {
            _log.Warning($"[IINACT] IPC error during connect: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Call this every frame from the game thread (Framework.Update).
    /// Periodically checks if IINACT is still alive; reconnects if it unloads.
    /// </summary>
    public void TryHeartbeat()
    {
        if (_isDisposed || !_isConnected) return;
        if (DateTime.UtcNow - _lastHeartbeat < HeartbeatInterval) return;

        _lastHeartbeat = DateTime.UtcNow;

        try
        {
            // IINACT.Version is always registered and zero-arg -- reliable liveness check.
            _pluginInterface
                .GetIpcSubscriber<Version>(IpcVersion)
                .InvokeFunc();
            _log.Verbose("[IINACT] Heartbeat OK");
        }
        catch (IpcNotReadyError)
        {
            _log.Info("[IINACT] IPC lost (IpcNotReadyError on heartbeat), will reconnect");
            _isConnected = false;
            Disconnected?.Invoke();
            // TryConnect will pick this up on the next frame
        }
        catch (Exception ex)
        {
            _log.Debug($"[IINACT] Heartbeat non-fatal error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TryUnsubscribe()
    {
        try
        {
            _pluginInterface
                .GetIpcSubscriber<string, bool>(IpcUnsubscribe)
                .InvokeFunc(SinkGateName);
        }
        catch (IpcNotReadyError)
        {
            // IINACT not loaded -- expected during first connect attempt
        }
        catch (Exception ex)
        {
            _log.Debug($"[IINACT] TryUnsubscribe non-fatal error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Called by IINACT when it pushes an event to us (game thread).</summary>
    private bool OnIINACTEvent(JObject message)
    {
        try
        {
            var type = message["type"]?.Value<string>();
            _log.Debug($"[IINACT] Event received: type={type ?? "(null)"}");

            switch (type)
            {
                case "InCombat":
                    ProcessInCombat(message);
                    break;
                default:
                    _log.Debug($"[IINACT] Unhandled event type: {type ?? "(null)"}");
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to process IINACT event: {ex.Message}");
        }

        return true;
    }

    private void ProcessInCombat(JObject message)
    {
        // {"type":"InCombat","inACTCombat":bool,"inGameCombat":bool}
        var inAct = message["inACTCombat"]?.Value<bool>() ?? false;
        var inGame = message["inGameCombat"]?.Value<bool>() ?? false;
        _log.Debug($"[IINACT] ProcessInCombat: inACTCombat={inAct}, inGameCombat={inGame}");

        CombatStateChanged?.Invoke(new InCombatPayload(inGame, inAct));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isConnected)
        {
            try
            {
                _pluginInterface
                    .GetIpcSubscriber<string, bool>(IpcUnsubscribe)
                    .InvokeFunc(SinkGateName);
            }
            catch (IpcNotReadyError)
            {
                // IINACT may already be unloaded
            }
            catch (Exception ex)
            {
                _log.Debug($"[IINACT] Dispose unsubscribe error: {ex.GetType().Name}: {ex.Message}");
            }

            _isConnected = false;
            Disconnected?.Invoke();
        }

        _sinkGate.UnregisterFunc();
    }
}
