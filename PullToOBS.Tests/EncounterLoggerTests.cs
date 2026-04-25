using System;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public sealed class EncounterLoggerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IPluginLog _log;

    public EncounterLoggerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"PullToOBS.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _log = Substitute.For<IPluginLog>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    [Fact]
    public void HandleEncounterEnded_WritesJsonForInstancedContent()
    {
        var logger = new EncounterLogger(_log, territoryType => territoryType == 987u ? "The Omega Protocol" : null, () => true);
        var encounterRecord = new EncounterRecord(
            new DateTimeOffset(2026, 4, 24, 20, 14, 32, TimeSpan.Zero),
            987u,
            "BLM",
            Path.Combine(_tempDirectory, "recording.mkv"),
            Path.Combine(_tempDirectory, "replay.mkv"));

        logger.HandleEncounterEnded(encounterRecord);

        var metadataPath = Path.Combine(_tempDirectory, "2026-04-24_20-14-32.json");
        Assert.True(File.Exists(metadataPath));

        using var jsonDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = jsonDocument.RootElement;
        Assert.Equal("The Omega Protocol", root.GetProperty("encounter").GetString());
        Assert.Equal("BLM", root.GetProperty("job").GetString());
        Assert.Equal((uint)987, root.GetProperty("territory_type").GetUInt32());
        Assert.Equal("recording.mkv", root.GetProperty("recording").GetString());
        Assert.Equal("replay.mkv", root.GetProperty("replay_buffer").GetString());
    }

    [Fact]
    public void HandleEncounterEnded_WritesJsonWithNullReplayBuffer()
    {
        var logger = new EncounterLogger(_log, _ => "Futures Rewritten", () => true);
        var encounterRecord = new EncounterRecord(
            new DateTimeOffset(2026, 4, 24, 21, 15, 33, TimeSpan.Zero),
            123u,
            null,
            Path.Combine(_tempDirectory, "recording.mkv"),
            null);

        logger.HandleEncounterEnded(encounterRecord);

        var metadataPath = Path.Combine(_tempDirectory, "2026-04-24_21-15-33.json");
        using var jsonDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var jobProperty = jsonDocument.RootElement.GetProperty("job");
        var replayBufferProperty = jsonDocument.RootElement.GetProperty("replay_buffer");
        Assert.Equal(JsonValueKind.Null, jobProperty.ValueKind);
        Assert.Equal(JsonValueKind.Null, replayBufferProperty.ValueKind);
    }

    [Fact]
    public void HandleEncounterEnded_SkipsNonInstancedContent()
    {
        var logger = new EncounterLogger(_log, _ => null, () => true);
        var encounterRecord = new EncounterRecord(
            DateTimeOffset.UtcNow,
            444u,
            "WAR",
            Path.Combine(_tempDirectory, "recording.mkv"),
            null);

        logger.HandleEncounterEnded(encounterRecord);

        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.json"));
    }

    [Fact]
    public void HandleEncounterEnded_SkipsWhenNoPathsAreAvailable()
    {
        var logger = new EncounterLogger(_log, _ => "Abyssos: The Eighth Circle", () => true);
        var encounterRecord = new EncounterRecord(DateTimeOffset.UtcNow, 123u, null, null, null);

        logger.HandleEncounterEnded(encounterRecord);

        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.json"));
    }

    [Fact]
    public void HandleEncounterEnded_SkipsWhenDisabled()
    {
        var logger = new EncounterLogger(_log, _ => "The Omega Protocol", () => false);
        var encounterRecord = new EncounterRecord(
            new DateTimeOffset(2026, 4, 24, 20, 14, 32, TimeSpan.Zero),
            987u,
            "BLM",
            Path.Combine(_tempDirectory, "recording.mkv"),
            Path.Combine(_tempDirectory, "replay.mkv"));

        logger.HandleEncounterEnded(encounterRecord);

        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.json"));
    }
}
