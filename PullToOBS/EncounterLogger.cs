using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;

namespace PullToOBS;

public sealed class EncounterLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly EncounterManager? _encounterManager;
    private readonly IPluginLog _log;
    private readonly Func<uint, string?> _encounterNameResolver;
    private readonly Func<bool> _enabled;

    public EncounterLogger(IPluginLog log, Func<uint, string?> encounterNameResolver, Func<bool> enabled)
    {
        _log = log;
        _encounterNameResolver = encounterNameResolver;
        _enabled = enabled;
    }

    public EncounterLogger(EncounterManager encounterManager, IPluginLog log, Func<uint, string?> encounterNameResolver, Func<bool> enabled)
        : this(log, encounterNameResolver, enabled)
    {
        _encounterManager = encounterManager;
        _encounterManager.EncounterEnded += HandleEncounterEnded;
    }

    public void HandleEncounterEnded(EncounterRecord encounterRecord)
    {
        if (!_enabled())
            return;

        try
        {
            if (encounterRecord.RecordingPath is null && encounterRecord.ReplayBufferPath is null)
            {
                _log.Debug("[EncounterLogger] Skipping metadata write: encounter produced no file paths");
                return;
            }

            var encounterName = _encounterNameResolver(encounterRecord.TerritoryType);
            if (string.IsNullOrWhiteSpace(encounterName))
            {
                _log.Debug($"[EncounterLogger] Skipping metadata write: territory {encounterRecord.TerritoryType} is not instanced content");
                return;
            }

            var outputDirectory = GetOutputDirectory(encounterRecord);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                _log.Warning("[EncounterLogger] Skipping metadata write: could not resolve output directory");
                return;
            }

            var encounterMetadata = new EncounterMetadata(
                encounterRecord.StartedAt,
                encounterName,
                encounterRecord.JobAbbreviation,
                encounterRecord.TerritoryType,
                GetFileName(encounterRecord.RecordingPath),
                GetFileName(encounterRecord.ReplayBufferPath));

            var metadataPath = Path.Combine(
                outputDirectory,
                $"{encounterRecord.StartedAt:yyyy-MM-dd_HH-mm-ss}.json");

            var encounterMetadataJson = JsonSerializer.Serialize(encounterMetadata, JsonOptions);
            File.WriteAllText(metadataPath, encounterMetadataJson + Environment.NewLine);
            _log.Debug($"[EncounterLogger] Wrote encounter metadata to {metadataPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"[EncounterLogger] Failed to write encounter metadata: {ex}");
        }
    }

    public void Dispose()
    {
        if (_encounterManager is not null)
            _encounterManager.EncounterEnded -= HandleEncounterEnded;
    }

    private static string? GetOutputDirectory(EncounterRecord encounterRecord)
    {
        return GetDirectoryName(encounterRecord.RecordingPath)
               ?? GetDirectoryName(encounterRecord.ReplayBufferPath);
    }

    private static string? GetDirectoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var directoryName = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(directoryName) ? null : directoryName;
    }

    private static string? GetFileName(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
    }

    private sealed record EncounterMetadata(
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("encounter")] string Encounter,
        [property: JsonPropertyName("job")] string? Job,
        [property: JsonPropertyName("territory_type")] uint TerritoryType,
        [property: JsonPropertyName("recording")] string? Recording,
        [property: JsonPropertyName("replay_buffer")] string? ReplayBuffer);
}
