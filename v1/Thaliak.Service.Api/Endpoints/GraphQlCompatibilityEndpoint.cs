using System.Text.Json;
using Thaliak.Service.Api.Models;
using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

public static class GraphQlCompatibilityEndpoint
{
    public static IEndpointRouteBuilder MapGraphQlCompatibilityEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/graphql/2022-08-14", HandleGraphQlAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGraphQlAsync(
        GraphQlRequestDto request,
        ThaliakReadService readService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) {
            return Error("GraphQL query is required.", StatusCodes.Status400BadRequest);
        }

        var slug = TryGetRepositorySlug(request);
        if (string.IsNullOrWhiteSpace(slug)) {
            return Error("GraphQL query must provide a repository slug variable named repoId.", StatusCodes.Status400BadRequest);
        }

        if (request.Query.Contains("versions", StringComparison.Ordinal)) {
            var repository = await readService.GetGraphQlVersionsAsync(slug, cancellationToken);
            return repository is null
                ? Error($"Repository '{slug}' was not found.", StatusCodes.Status404NotFound)
                : Json(new GraphQlResponseDto<GraphQlRepositoryEnvelopeDto>(new GraphQlRepositoryEnvelopeDto(repository)));
        }

        if (request.Query.Contains("latestVersion", StringComparison.Ordinal)) {
            var repository = await readService.GetGraphQlMetadataAsync(slug, cancellationToken);
            return repository is null
                ? Error($"Repository '{slug}' was not found.", StatusCodes.Status404NotFound)
                : Json(new GraphQlResponseDto<GraphQlRepositoryEnvelopeDto>(new GraphQlRepositoryEnvelopeDto(repository)));
        }

        return Error("Unsupported GraphQL query shape.", StatusCodes.Status400BadRequest);
    }

    private static string? TryGetRepositorySlug(GraphQlRequestDto request)
    {
        if (request.Variables is null) {
            return null;
        }

        foreach (var variableName in new[] { "repoId", "slug", "repositorySlug" }) {
            if (!request.Variables.TryGetValue(variableName, out var value)) {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String) {
                return value.GetString();
            }
        }

        return null;
    }

    private static IResult Error(string message, int statusCode) =>
        Json(new GraphQlResponseDto<object>(null, [new GraphQlErrorDto(message)]), statusCode);

    private static IResult Json<T>(GraphQlResponseDto<T> response, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(response, statusCode: statusCode);
}
