using System.Text.Json.Serialization;

namespace Thaliak.Service.Api.Models;

public sealed record RepositoriesResponseDto(
    [property: JsonPropertyName("repositories")] IReadOnlyList<RepositoryDto> Repositories,
    [property: JsonPropertyName("total")] int Total);

public sealed record RepositoryDto(
    [property: JsonPropertyName("service_id")] string ServiceId,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("latest_patch")] LatestPatchInfoDto? LatestPatch);

public sealed record LatestPatchInfoDto(
    [property: JsonPropertyName("version_string")] string VersionString,
    [property: JsonPropertyName("first_offered")] DateTime FirstOffered,
    [property: JsonPropertyName("last_offered")] DateTime LastOffered);

public sealed record PatchesResponseDto(
    [property: JsonPropertyName("patches")] IReadOnlyList<PatchDto> Patches,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("total_size")] long TotalSize);

public sealed record PatchDto(
    [property: JsonPropertyName("repository_slug")] string RepositorySlug,
    [property: JsonPropertyName("version_string")] string VersionString,
    [property: JsonPropertyName("remote_url")] string RemoteUrl,
    [property: JsonPropertyName("local_path")] string LocalPath,
    [property: JsonPropertyName("first_seen")] DateTime? FirstSeen,
    [property: JsonPropertyName("last_seen")] DateTime? LastSeen,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("hash")] PatchHashDto Hash,
    [property: JsonPropertyName("first_offered")] DateTime? FirstOffered,
    [property: JsonPropertyName("last_offered")] DateTime? LastOffered,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record PatchHashDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("block_size")] long? BlockSize = null,
    [property: JsonPropertyName("hashes")] IReadOnlyList<string>? Hashes = null);
