using Microsoft.AspNetCore.Http.HttpResults;
using Thaliak.Service.Api.Models;
using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

public static class ArtifactEndpoints
{
    private const string ArtifactContentType = "application/octet-stream";

    public static RouteGroupBuilder MapArtifactEndpoints(this RouteGroupBuilder group)
    {
        var artifacts = group.MapGroup("/artifacts");
        artifacts.MapGet("/regions", GetRegionsAsync);
        artifacts.MapGet("/regions/{region}", GetRegionAsync);

        return group;
    }

    public static IEndpointRouteBuilder MapArtifactFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cluts/{slugOrAlias}/{gameVersion}.clut", GetClutAsync);
        app.MapGet("/luts/{slugOrAlias}/{patchVersion}.lut", GetLutAsync);

        return app;
    }

    private static async Task<Ok<ArtifactRegionsResponseDto>> GetRegionsAsync(
        ArtifactReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetRegionsAsync(cancellationToken));

    private static async Task<Results<Ok<ArtifactRegionDto>, NotFound>> GetRegionAsync(
        string region,
        ArtifactReadService readService,
        CancellationToken cancellationToken)
    {
        var result = await readService.GetRegionAsync(region, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static Task<IResult> GetClutAsync(
        string slugOrAlias,
        string gameVersion,
        HttpContext httpContext,
        ArtifactReadService readService,
        CancellationToken cancellationToken) =>
        GetArtifactFileAsync("clut", slugOrAlias, gameVersion, httpContext, readService, cancellationToken);

    private static Task<IResult> GetLutAsync(
        string slugOrAlias,
        string patchVersion,
        HttpContext httpContext,
        ArtifactReadService readService,
        CancellationToken cancellationToken) =>
        GetArtifactFileAsync("lut", slugOrAlias, patchVersion, httpContext, readService, cancellationToken);

    private static async Task<IResult> GetArtifactFileAsync(
        string kind,
        string slugOrAlias,
        string versionString,
        HttpContext httpContext,
        ArtifactReadService readService,
        CancellationToken cancellationToken)
    {
        var lookup = await readService.GetFileAsync(kind, slugOrAlias, versionString, cancellationToken);
        if (lookup is null) {
            return TypedResults.NotFound();
        }

        var fileInfo = new FileInfo(lookup.AbsolutePath);
        httpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        if (!string.IsNullOrWhiteSpace(lookup.Artifact.Sha256)) {
            httpContext.Response.Headers.ETag = $"\"{lookup.Artifact.Sha256}\"";
        }

        return Results.File(
            lookup.AbsolutePath,
            ArtifactContentType,
            enableRangeProcessing: true,
            lastModified: fileInfo.LastWriteTimeUtc);
    }
}
