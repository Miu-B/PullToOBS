using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace PullToOBS.Windows;

public class PullToOBSConfigWindow : Window, IDisposable
{
    private readonly PullToOBSPlugin _plugin;
    private readonly PullToOBSConfiguration _configuration;
    private readonly IChatGui _chatGui;
    private readonly IKeyState _keyState;

    private string _urlBuffer;
    private string _passwordBuffer;
    private bool _isConnecting;
    private bool _capturingInstapostHotkey;
    private bool _instapostCaptureArmed;
    private string _statusMessage = "";
    private Vector4 _statusColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

    public bool IsCapturingInstapostHotkey => _capturingInstapostHotkey;

    public PullToOBSConfigWindow(PullToOBSPlugin plugin, IChatGui chatGui, IKeyState keyState) : base("PullToOBS Configuration")
    {
        _plugin = plugin;
        _configuration = plugin.Configuration;
        _chatGui = chatGui;
        _keyState = keyState;

        Size = new Vector2(500, 400);
        SizeCondition = ImGuiCond.FirstUseEver;

        _urlBuffer = _configuration.ObsWebSocketUrl;
        _passwordBuffer = _configuration.ObsPassword;
    }

    public override void Draw()
    {
        UpdateStatus();

        ImGui.Text("OBS WebSocket Connection");
        ImGui.Separator();
        ImGui.Spacing();

        // URL Input -- save only when user finishes editing (focus loss / Enter)
        ImGui.Text("WebSocket URL:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##Url", ref _urlBuffer, 500);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _configuration.ObsWebSocketUrl = _urlBuffer;
            _configuration.Save();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Default: ws://localhost:4455 (OBS WebSocket v5)");

        ImGui.Spacing();

        // Password Input -- save only when user finishes editing
        ImGui.Text("Password:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##Password", ref _passwordBuffer, 500, ImGuiInputTextFlags.Password);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _configuration.ObsPassword = _passwordBuffer;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Auto-connect checkbox
        var autoConnect = _configuration.AutoConnectOnStart;
        if (ImGui.Checkbox("Auto-connect to OBS on plugin start", ref autoConnect))
        {
            _configuration.AutoConnectOnStart = autoConnect;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Connection status
        ImGui.Text("Status:");
        ImGui.SameLine();
        ImGui.TextColored(_statusColor, _statusMessage);

        ImGui.Spacing();

        // Connect/Disconnect button
        var obs = _plugin.ObsController;
        var buttonText = obs.IsConnected ? "Disconnect" : "Connect";

        if (_isConnecting) ImGui.BeginDisabled();

        if (ImGui.Button(_isConnecting ? "Connecting..." : buttonText, new Vector2(120, 0)) && !_isConnecting)
        {
            _ = HandleConnectionAsync();
        }

        if (_isConnecting) ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            IsOpen = false;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Replay buffer warning
        if (obs.IsConnected && !obs.IsReplayBufferConfigured)
        {
            ImGui.TextColored(
                new Vector4(1.0f, 0.6f, 0.0f, 1.0f),
                "Warning: Replay Buffer is not ready in OBS. Enable it in OBS (Settings > Output > Replay Buffer), then disconnect and reconnect PullToOBS.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Indicator settings
        ImGui.Text("Indicator Settings");
        ImGui.Spacing();

        var scale = _configuration.IndicatorScale;
        if (ImGui.SliderFloat("Indicator Scale", ref scale, 0.5f, 2.0f, "%.1fx"))
        {
            _configuration.IndicatorScale = scale;
            _configuration.Save();
        }

        ImGui.Spacing();

        var hideIndicator = _configuration.HideIndicator;
        if (ImGui.Checkbox("Hide Indicator", ref hideIndicator))
        {
            _configuration.HideIndicator = hideIndicator;
            _configuration.Save();
        }

        ImGui.Spacing();

        var saveEncounterMetadata = _configuration.SaveEncounterMetadata;
        if (ImGui.Checkbox("Save encounter metadata", ref saveEncounterMetadata))
        {
            _configuration.SaveEncounterMetadata = saveEncounterMetadata;
            _configuration.Save();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Writes a JSON file alongside each recording for use with the limitcut tool");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Instapost / Quick Save ───────────────────────────────────────

        ImGui.Text("Quick Save (Instapost)");
        ImGui.Spacing();

        var instapostEnabled = _configuration.InstapostEnabled;
        if (ImGui.Checkbox("Enable quick save hotkey", ref instapostEnabled))
        {
            _configuration.InstapostEnabled = instapostEnabled;
            _configuration.Save();
        }

        if (instapostEnabled)
        {
            ImGui.Spacing();

            DrawInstapostHotkeyCaptureUi();

            ImGui.Spacing();

            var cooldown = _configuration.InstapostCooldownSeconds;
            if (ImGui.SliderInt("Cooldown (seconds)", ref cooldown, 5, 60))
            {
                _configuration.InstapostCooldownSeconds = cooldown;
                _configuration.Save();
            }

            ImGui.Spacing();
            ImGui.TextColored(
                new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                "Click Change, then press your desired key combination. Escape cancels capture; Delete/Backspace clears while listening.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
        else if (_capturingInstapostHotkey)
        {
            CancelInstapostHotkeyCapture();
        }

        // Instructions
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "How it works:");
        ImGui.TextWrapped(
            "When configured and started, the plugin will:" +
            "\n1. Connect to OBS and start the Replay Buffer" +
            "\n2. Detect combat state changes via Dalamud" +
            "\n3. On encounter start: Start recording, wait 5s, save replay buffer" +
            "\n4. On encounter end: Wait 5s overlap, stop recording" +
            "\n\nResult: Two files per encounter — replay buffer clip (prepull) + full recording.");
    }

    private void DrawInstapostHotkeyCaptureUi()
    {
        ProcessInstapostHotkeyCapture();

        var binding = InstapostHotkey.FormatBinding(
            _configuration.InstapostKeyCode,
            _configuration.InstapostModCtrl,
            _configuration.InstapostModShift,
            _configuration.InstapostModAlt);

        var displayText = _capturingInstapostHotkey
            ? (_instapostCaptureArmed ? "Listening... press a key combination" : "Release all keys...")
            : binding;

        ImGui.Text("Hotkey:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), displayText);

        ImGui.Spacing();

        if (!_capturingInstapostHotkey)
        {
            if (ImGui.Button("Change", new Vector2(100, 0)))
                BeginInstapostHotkeyCapture();

            ImGui.SameLine();
            if (ImGui.Button("Clear", new Vector2(100, 0)))
                ClearInstapostHotkey();
        }
        else
        {
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
                CancelInstapostHotkeyCapture();

            ImGui.SameLine();
            if (ImGui.Button("Clear", new Vector2(100, 0)))
            {
                ClearInstapostHotkey();
                CancelInstapostHotkeyCapture();
            }
        }
    }

    private void BeginInstapostHotkeyCapture()
    {
        _capturingInstapostHotkey = true;
        _instapostCaptureArmed = false;
    }

    private void CancelInstapostHotkeyCapture()
    {
        _capturingInstapostHotkey = false;
        _instapostCaptureArmed = false;
    }

    private void ClearInstapostHotkey()
    {
        _configuration.InstapostKeyCode = (int)VirtualKey.NO_KEY;
        _configuration.InstapostModCtrl = false;
        _configuration.InstapostModShift = false;
        _configuration.InstapostModAlt = false;
        _configuration.Save();
    }

    private void ProcessInstapostHotkeyCapture()
    {
        if (!_capturingInstapostHotkey)
            return;

        if (!_instapostCaptureArmed)
        {
            if (!InstapostHotkey.AreAnyCaptureKeysDown(_keyState))
                _instapostCaptureArmed = true;

            return;
        }

        var action = InstapostHotkey.GetCaptureAction(_keyState);
        switch (action.Kind)
        {
            case InstapostHotkeyCaptureActionKind.None:
                return;
            case InstapostHotkeyCaptureActionKind.Cancel:
                CancelInstapostHotkeyCapture();
                return;
            case InstapostHotkeyCaptureActionKind.Clear:
                ClearInstapostHotkey();
                CancelInstapostHotkeyCapture();
                return;
            case InstapostHotkeyCaptureActionKind.Set:
                _configuration.InstapostKeyCode = (int)action.Key;
                _configuration.InstapostModCtrl = action.Ctrl;
                _configuration.InstapostModShift = action.Shift;
                _configuration.InstapostModAlt = action.Alt;
                _configuration.Save();
                CancelInstapostHotkeyCapture();
                return;
        }
    }

    private void UpdateStatus()
    {
        var obs = _plugin.ObsController;
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: _isConnecting,
            isConnected: obs.IsConnected,
            isRecording: obs.IsRecording,
            isStandby: _plugin.EncounterManager.IsStandby,
            isReplayBufferActive: obs.IsReplayBufferActive);

        switch (status)
        {
            case ObsStatusKind.Connecting:
                _statusMessage = "Connecting...";
                _statusColor = new Vector4(0.8f, 0.8f, 0.0f, 1.0f);
                break;
            case ObsStatusKind.Disconnected:
                _statusMessage = "Not Connected";
                _statusColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                break;
            case ObsStatusKind.Recording:
                _statusMessage = "Recording";
                _statusColor = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                break;
            case ObsStatusKind.Standby:
                _statusMessage = "Standby";
                _statusColor = new Vector4(0.0f, 1.0f, 1.0f, 1.0f);
                break;
            case ObsStatusKind.ReplayBufferInactive:
                _statusMessage = "Replay Buffer Inactive";
                _statusColor = new Vector4(1.0f, 0.6f, 0.0f, 1.0f);
                break;
            default:
                _statusMessage = "Connected & Ready";
                _statusColor = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                break;
        }
    }

    private async Task HandleConnectionAsync()
    {
        var obs = _plugin.ObsController;

        if (obs.IsConnected)
        {
            // Disconnect
            obs.Disconnect();
            _chatGui.Print("[PullToOBS] Disconnected from OBS");
        }
        else
        {
            // Connect
            _isConnecting = true;

            try
            {
                await obs.ConnectAsync(_configuration.ObsWebSocketUrl, _configuration.ObsPassword);
                _chatGui.Print("[PullToOBS] Connected to OBS");
            }
            catch (Exception ex)
            {
                _chatGui.Print($"[PullToOBS] Failed to connect to OBS: {ex.Message}");
            }
            finally
            {
                _isConnecting = false;
            }
        }
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
