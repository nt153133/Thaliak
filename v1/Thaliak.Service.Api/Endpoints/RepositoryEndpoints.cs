using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Thaliak.Service.Api.Models;
using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

public static class RepositoryEndpoints
{
    public static RouteGroupBuilder MapRepositoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/repositories", GetRepositoriesAsync);
        group.MapGet("/repositories/{slug}", GetRepositoryAsync);
        group.MapGet("/repositories/{slug}/patches", GetRepositoryPatchesAsync);
        group.MapGet("/repositories/{slug}/patches/{version}", GetRepositoryPatchAsync);

        return group;
    }

    private static async Task<Ok<RepositoriesResponseDto>> GetRepositoriesAsync(
        ThaliakReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetRepositoriesAsync(cancellationToken));

    private static async Task<Results<Ok<RepositoryDto>, NotFound>> GetRepositoryAsync(
        string slug,
        ThaliakReadService readService,
        CancellationToken cancellationToken)
    {
        var repository = await readService.GetRepositoryAsync(slug, cancellationToken);
        return repository is null ? TypedResults.NotFound() : TypedResults.Ok(repository);
    }

    private static async Task<Results<Ok<PatchesResponseDto>, NotFound>> GetRepositoryPatchesAsync(
        string slug,
        [FromQuery(Name = "from")] string? fromVersion,
        [FromQuery(Name = "to")] string? toVersion,
        [FromQuery] bool? all,
        [FromQuery] bool? active,
        ThaliakReadService readService,
        CancellationToken cancellationToken)
    {
        var patches = await readService.GetRepositoryPatchesAsync(
            slug,
            fromVersion,
            toVersion,
            all ?? false,
            active,
            cancellationToken);

        return patches is null ? TypedResults.NotFound() : TypedResults.Ok(patches);
    }

    private static async Task<Results<Ok<PatchDto>, NotFound>> GetRepositoryPatchAsync(
        string slug,
        string version,
        ThaliakReadService readService,
        CancellationToken cancellationToken)
    {
        var patch = await readService.GetRepositoryPatchAsync(slug, version, cancellationToken);
        return patch is null ? TypedResults.NotFound() : TypedResults.Ok(patch);
    }
}
