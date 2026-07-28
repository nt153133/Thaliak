using System.Text.Json.Serialization;

namespace Thaliak.Service.Api.Models;

public sealed record ArtifactRegionsResponseDto(IReadOnlyList<ArtifactRegionDto> Regions);

public sealed record ArtifactRegionDto(
    string Region,
    [property: JsonPropertyName("is_ready")]
    bool IsReady,
    [property: JsonPropertyName("ready_at_utc")]
    DateTime? ReadyAtUtc,
    IReadOnlyList<ArtifactRepositoryDto> Repositories);

public sealed record ArtifactRepositoryDto(
    string Slug,
    string Expansion,
    string Description,
    IReadOnlyList<string> Aliases,
    [property: JsonPropertyName("latest_clut")]
    ArtifactFileDto? LatestClut,
    [property: JsonPropertyName("latest_lut")]
    ArtifactFileDto? LatestLut);

public sealed record ArtifactFileDto(
    string Kind,
    [property: JsonPropertyName("version_string")]
    string VersionString,
    string Url,
    long Size,
    string? Sha256,
    [property: JsonPropertyName("ready_at_utc")]
    DateTime? ReadyAtUtc);
