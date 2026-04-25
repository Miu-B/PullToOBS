using System;

namespace PullToOBS;

public sealed record EncounterRecord(
    DateTimeOffset StartedAt,
    uint TerritoryType,
    string? JobAbbreviation,
    string? RecordingPath,
    string? ReplayBufferPath);
