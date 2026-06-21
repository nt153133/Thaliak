using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thaliak.Service.Api.Models;

public sealed record GraphQlRequestDto(
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, JsonElement>? Variables,
    [property: JsonPropertyName("operationName")] string? OperationName = null);

public sealed record GraphQlResponseDto<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("errors")] IReadOnlyList<GraphQlErrorDto>? Errors = null);

public sealed record GraphQlErrorDto(
    [property: JsonPropertyName("message")] string Message);

public sealed record GraphQlRepositoryEnvelopeDto(
    [property: JsonPropertyName("repository")] GraphQlRepositoryDto Repository);

public sealed record GraphQlRepositoryDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("latestVersion")] GraphQlVersionReferenceDto? LatestVersion,
    [property: JsonPropertyName("versions")] IReadOnlyList<GraphQlAnnotatedVersionDto>? Versions);

public sealed record GraphQlVersionReferenceDto(
    [property: JsonPropertyName("versionString")] string VersionString);

public sealed record GraphQlAnnotatedVersionDto(
    [property: JsonPropertyName("versionString")] string VersionString,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("prerequisiteVersions")] IReadOnlyList<GraphQlVersionReferenceDto> PrerequisiteVersions,
    [property: JsonPropertyName("patches")] IReadOnlyList<GraphQlPatchDto> Patches);

public sealed record GraphQlPatchDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("size")] long Size);
