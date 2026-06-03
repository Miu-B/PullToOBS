using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public class InstapostHandlerTests : IDisposable
{
    private readonly IOBSController _obs;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly ICondition _condition;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;
    private readonly int _cooldownSeconds = 15;
    private bool _enabled = true;
    private readonly InstapostHandler _sut;
    private readonly string _tempDir;

    // Simulated in-combat state for the condition flag.
    private bool _inCombat;

    public InstapostHandlerTests()
    {
        _obs = Substitute.For<IOBSController>();
        _clientState = Substitute.For<IClientState>();
        _playerState = Substitute.For<IPlayerState>();
        _condition = Substitute.For<ICondition>();
        _chatGui = Substitute.For<IChatGui>();
        _log = Substitute.For<IPluginLog>();

        // Default: OBS connected, not recording
        _obs.IsConnected.Returns(true);
        _obs.IsRecording.Returns(false);

        // Default: logged in, dummy territory and job
        _clientState.IsLoggedIn.Returns(true);
        _clientState.TerritoryType.Returns((ushort)987u);

        _playerState.CharacterName.Returns("M'iu Bittermoon");

        // Combat flag
        _inCombat = false;
        _condition[ConditionFlag.InCombat].Returns(_ => _inCombat);

        _tempDir = Path.Combine(Path.GetTempPath(), $"PullToOBS_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _sut = new InstapostHandler(
            _obs,
            _clientState,
            _playerState,
            _condition,
            _chatGui,
            _log,
            territoryType => territoryType == 987u ? "Deltascape V1.0" : null,
            territoryType => territoryType == 987u ? "Solution Nine" : null,
            () => _cooldownSeconds,
            () => _enabled);
    }

    public void Dispose()
    {
        _sut.Dispose();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task TryQuickSave_WhenEnabledAndConnected_SavesReplayBuffer()
    {
        var replayPath = Path.Combine(_tempDir, "Replay 2026-06-02 15-30-45.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        var result = await _sut.TryQuickSaveAsync();

        Assert.True(result);
        await _obs.Received(1).SaveReplayBuffer();
        Assert.Equal(1, _sut.QuickSaveCount);
    }

    [Fact]
    public async Task TryQuickSave_WritesInstapostJson()
    {
        var replayPath = Path.Combine(_tempDir, "Replay 2026-06-02 15-30-45.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        await _sut.TryQuickSaveAsync();

        var jsonFiles = Directory.GetFiles(_tempDir, "instapost_*.json");
        Assert.Single(jsonFiles);

        var jsonContent = await File.ReadAllTextAsync(jsonFiles[0]);
        var data = JsonSerializer.Deserialize<InstapostData>(jsonContent)!;

        Assert.Equal("Replay 2026-06-02 15-30-45.mkv", data.ReplayBuffer);
        Assert.Equal("Deltascape V1.0", data.Encounter);
        Assert.Equal("Solution Nine", data.TerritoryName);
        Assert.Equal(987u, data.TerritoryType);
        Assert.False(data.IsInCombat);
        Assert.Equal("M'iu Bittermoon", data.PlayerName);
    }

    [Fact]
    public async Task TryQuickSave_WhenInCombat_JsonReflectsCombatState()
    {
        _inCombat = true;

        var replayPath = Path.Combine(_tempDir, "Replay 2026-06-02 15-30-45.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        await _sut.TryQuickSaveAsync();

        var jsonFiles = Directory.GetFiles(_tempDir, "instapost_*.json");
        var jsonContent = await File.ReadAllTextAsync(jsonFiles[0]);
        var data = JsonSerializer.Deserialize<InstapostData>(jsonContent)!;

        Assert.True(data.IsInCombat);
    }

    [Fact]
    public async Task TryQuickSave_FiresQuickSaveTriggered()
    {
        var triggered = false;
        _sut.QuickSaveTriggered += () => triggered = true;

        var replayPath = Path.Combine(_tempDir, "Replay 2026-06-02 15-30-45.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        await _sut.TryQuickSaveAsync();

        Assert.True(triggered);
    }

    [Fact]
    public async Task TryQuickSave_WhenDisabled_PrintsMessageAndReturnsFalse()
    {
        _enabled = false;

        var result = await _sut.TryQuickSaveAsync();

        Assert.False(result);
        _chatGui.Received(1).Print(Arg.Is<string>(s => s.Contains("disabled")));
        await _obs.DidNotReceive().SaveReplayBuffer();
    }

    [Fact]
    public async Task TryQuickSave_WhenNotConnected_PrintsMessageAndDoesNotStartCooldown()
    {
        _obs.IsConnected.Returns(false);

        var replayPath = Path.Combine(_tempDir, "Replay.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        var result = await _sut.TryQuickSaveAsync();

        Assert.False(result);
        _chatGui.Received(1).Print(Arg.Is<string>(s => s.Contains("not connected")));

        // Cooldown should not start — user can retry immediately
        Assert.True(_sut.CanQuickSave);
    }

    [Fact]
    public async Task TryQuickSave_RespectsCooldown()
    {
        var replayPath = Path.Combine(_tempDir, "Replay.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        // First save succeeds
        var result1 = await _sut.TryQuickSaveAsync();
        Assert.True(result1);

        // Immediate second save is blocked by cooldown
        var result2 = await _sut.TryQuickSaveAsync();
        Assert.False(result2);
        _chatGui.Received(1).Print(Arg.Is<string>(s => s.Contains("cooldown")));
    }

    [Fact]
    public async Task TryQuickSave_OnFailure_ResetsCooldown()
    {
        _obs.IsConnected.Returns(false);

        // First attempt fails (not connected)
        await _sut.TryQuickSaveAsync();

        // Cooldown should be reset — next attempt should be allowed (even if it fails again)
        Assert.True(_sut.CanQuickSave);
    }

    [Fact]
    public async Task TryQuickSave_WritesTimestampedJsonFilename_AndLeavesNoTempFile()
    {
        var replayPath = Path.Combine(_tempDir, "Replay1.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        await _sut.TryQuickSaveAsync();

        var jsonFiles = Directory.GetFiles(_tempDir, "instapost_*.json");
        Assert.Single(jsonFiles);
        Assert.StartsWith("instapost_", Path.GetFileName(jsonFiles[0]));
        Assert.EndsWith(".json", jsonFiles[0]);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public async Task TryQuickSave_WritesPlayerNameWithLiteralApostrophe()
    {
        var replayPath = Path.Combine(_tempDir, "Replay1.mkv");
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>(replayPath));

        await _sut.TryQuickSaveAsync();

        var jsonFiles = Directory.GetFiles(_tempDir, "instapost_*.json");
        var jsonContent = await File.ReadAllTextAsync(jsonFiles[0]);

        Assert.Contains("\"player_name\": \"M'iu Bittermoon\"", jsonContent);
        Assert.DoesNotContain("\\u0027", jsonContent);
    }
}
