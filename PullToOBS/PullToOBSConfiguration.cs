using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;

namespace PullToOBS;

[Serializable]
public class PullToOBSConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public string ObsWebSocketUrl { get; set; } = "ws://localhost:4455";

    public string ObsPassword { get; set; } = "";

    public bool AutoConnectOnStart { get; set; } = false;

    public Vector2 IndicatorPosition { get; set; } = new Vector2(300, 300);

    public float IndicatorScale { get; set; } = 1.0f;

    public bool HideIndicator { get; set; } = false;

    public bool SaveEncounterMetadata { get; set; } = false;

    // ── Instapost / quick-save ──────────────────────────────────────────

    public bool InstapostEnabled { get; set; } = false;

    /// <summary>
    /// Stored virtual key code for the instapost hotkey.
    /// Uses <see cref="VirtualKey"/> values, serialized as an <see cref="int"/>.
    /// </summary>
    public int InstapostKeyCode { get; set; } = (int)VirtualKey.NO_KEY;

    /// <summary>
    /// Legacy string-based hotkey setting retained for migration from v0.4.0.0 preview builds.
    /// Ignored once <see cref="InstapostKeyCode"/> is populated.
    /// </summary>
    public string InstapostKey { get; set; } = "";

    public bool InstapostModCtrl { get; set; } = false;

    public bool InstapostModShift { get; set; } = false;

    public bool InstapostModAlt { get; set; } = false;

    public int InstapostCooldownSeconds { get; set; } = 15;

    /// <summary>
    /// Delegate used to persist this configuration. Injected by the plugin at startup
    /// to avoid static coupling to the plugin interface.
    /// </summary>
    [NonSerialized]
    private Action<IPluginConfiguration>? _saveAction;

    public void SetSaveAction(Action<IPluginConfiguration> saveAction)
    {
        _saveAction = saveAction;
    }

    public void Save()
    {
        _saveAction?.Invoke(this);
    }
}
