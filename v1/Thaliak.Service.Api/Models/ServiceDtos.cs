using System.Text.Json.Serialization;

namespace Thaliak.Service.Api.Models;

public sealed record ServicesResponseDto(IReadOnlyList<ServiceDto> Services, int Count);

public sealed record ServiceDto(
    string Id,
    string Name,
    string Region,
    IReadOnlyList<ServiceRepositoryDto> Repositories);

public sealed record ServiceRepositoryDto(
    string Slug,
    string Expansion,
    string Description,
    IReadOnlyList<string> Aliases);
