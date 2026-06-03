using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace PullToOBS;

/// <summary>
/// Handles instapost quick-save logic: saves the OBS replay buffer on-demand
/// and writes a timestamped JSON descriptor for consumption by limitcut.
/// </summary>
public sealed class InstapostHandler : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IOBSController _obs;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly ICondition _condition;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;
    private readonly Func<uint, string?> _encounterNameResolver;
    private readonly Func<uint, string?> _territoryNameResolver;
    private readonly Func<int> _cooldownSeconds;
    private readonly Func<bool> _enabled;

    private DateTime _nextAllowed = DateTime.MinValue;
    private bool _inProgress;

    /// <summary>The number of successful quick-saves this session.</summary>
    public int QuickSaveCount { get; private set; }

    /// <summary>
    /// Fired immediately when a quick-save is triggered (before the async save completes).
    /// Used by the status indicator to show the gold flash.
    /// </summary>
    public event Action? QuickSaveTriggered;

    public InstapostHandler(
        IOBSController obs,
        IClientState clientState,
        IPlayerState playerState,
        ICondition condition,
        IChatGui chatGui,
        IPluginLog log,
        Func<uint, string?> encounterNameResolver,
        Func<uint, string?> territoryNameResolver,
        Func<int> cooldownSeconds,
        Func<bool> enabled)
    {
        _obs = obs;
        _clientState = clientState;
        _playerState = playerState;
        _condition = condition;
        _chatGui = chatGui;
        _log = log;
        _encounterNameResolver = encounterNameResolver;
        _territoryNameResolver = territoryNameResolver;
        _cooldownSeconds = cooldownSeconds;
        _enabled = enabled;
    }

    /// <summary>
    /// Whether a quick-save can be triggered right now (enabled, not on cooldown, not in progress).
    /// </summary>
    public bool CanQuickSave => _enabled() && DateTime.Now >= _nextAllowed && !_inProgress;

    /// <summary>
    /// Attempts to save the replay buffer and write an instapost JSON file.
    /// Returns true if the save was initiated, false if blocked by cooldown or disabled state.
    /// </summary>
    public async Task<bool> TryQuickSaveAsync()
    {
        if (!_enabled())
        {
            _chatGui.Print("[PullToOBS] Quick save is disabled. Enable it in /pto settings.");
            return false;
        }

        if (!CanQuickSave)
        {
            if (_inProgress)
                _chatGui.Print("[PullToOBS] Quick save already in progress, please wait...");
            else
            {
                var remaining = (int)(_nextAllowed - DateTime.Now).TotalSeconds;
                _chatGui.Print($"[PullToOBS] Quick save on cooldown ({remaining}s remaining)");
            }

            return false;
        }

        if (!_obs.IsConnected)
        {
            _chatGui.Print("[PullToOBS] Quick save failed: not connected to OBS");
            return false;
        }

        _inProgress = true;
        _nextAllowed = DateTime.Now.AddSeconds(_cooldownSeconds());
        QuickSaveTriggered?.Invoke();

        try
        {
            _chatGui.Print("[PullToOBS] Saving quick clip...");

            var path = await _obs.SaveReplayBuffer();

            if (path is null)
            {
                _chatGui.Print("[PullToOBS] Quick save failed: no replay buffer path returned from OBS");
                _nextAllowed = DateTime.Now;
                return false;
            }

            WriteInstapostJson(path);
            QuickSaveCount++;

            _chatGui.Print($"[PullToOBS] Quick save #{QuickSaveCount}: {Path.GetFileName(path)}");
            _log.Debug($"[Instapost] Quick save #{QuickSaveCount} completed: {path}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"[Instapost] Quick save failed: {ex}");
            _chatGui.Print($"[PullToOBS] Quick save failed: {ex.Message}");
            _nextAllowed = DateTime.Now;
            return false;
        }
        finally
        {
            _inProgress = false;
        }
    }

    private void WriteInstapostJson(string replayBufferPath)
    {
        var directory = Path.GetDirectoryName(replayBufferPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            _log.Warning("[Instapost] Could not determine directory for replay buffer path; skipping JSON write");
            return;
        }

        var now = DateTimeOffset.Now;
        var filename = $"instapost_{now:yyyyMMdd_HHmmss}.json";
        var jsonPath = Path.Combine(directory, filename);

        var territoryType = _clientState.TerritoryType;
        var encounterName = _encounterNameResolver(territoryType);
        var territoryName = _territoryNameResolver(territoryType);

        var data = new InstapostData(
            StartedAt: now,
            ReplayBuffer: Path.GetFileName(replayBufferPath),
            Job: _clientState.IsLoggedIn ? _playerState.ClassJob.ValueNullable?.Abbreviation.ToString() : null,
            Encounter: encounterName,
            TerritoryName: territoryName,
            TerritoryType: territoryType,
            IsInCombat: _condition[ConditionFlag.InCombat],
            PlayerName: _clientState.IsLoggedIn ? _playerState.CharacterName : null);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var tempPath = jsonPath + ".tmp";
        File.WriteAllText(tempPath, json + Environment.NewLine);
        File.Move(tempPath, jsonPath, overwrite: true);
        _log.Debug($"[Instapost] Wrote {jsonPath}");
    }

    public void Dispose()
    {
        // Nothing to dispose — no unmanaged resources.
    }
}
