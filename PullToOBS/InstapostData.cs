using System;
using System.Text.Json.Serialization;

namespace PullToOBS;

/// <summary>
/// Data written to an instapost_*.json file after each quick-save of the OBS replay buffer.
/// Consumed by limitcut --watch for automated video processing and upload.
/// </summary>
public sealed record InstapostData(
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("replay_buffer")] string ReplayBuffer,
    [property: JsonPropertyName("job")] string? Job,
    [property: JsonPropertyName("encounter")] string? Encounter,
    [property: JsonPropertyName("territory_name")] string? TerritoryName,
    [property: JsonPropertyName("territory_type")] uint TerritoryType,
    [property: JsonPropertyName("is_in_combat")] bool IsInCombat,
    [property: JsonPropertyName("player_name")] string? PlayerName);
