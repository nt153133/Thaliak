using System.Text.Json.Serialization;

namespace Thaliak.Service.Api.Models;

public sealed record StatusDto(
    string Status,
    [property: JsonPropertyName("checked_at_utc")]
    DateTime CheckedAtUtc,
    DatabaseStatusDto Database,
    ArtifactStatusDto Artifacts);

public sealed record DatabaseStatusDto(string Status);

public sealed record ArtifactStatusDto(
    string Root,
    [property: JsonPropertyName("generator_enabled")]
    bool GeneratorEnabled,
    IReadOnlyList<ArtifactRegionSummaryDto> Regions);

public sealed record ArtifactRegionSummaryDto(
    string Region,
    [property: JsonPropertyName("repository_count")]
    int RepositoryCount,
    [property: JsonPropertyName("ready_clut_count")]
    int ReadyClutCount,
    [property: JsonPropertyName("latest_ready_at_utc")]
    DateTime? LatestReadyAtUtc);
