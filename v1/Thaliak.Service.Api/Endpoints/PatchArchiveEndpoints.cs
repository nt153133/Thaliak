using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

public static class PatchArchiveEndpoints
{
    public static IEndpointRouteBuilder MapPatchArchiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
            "/patches/{slug}/{patchVersion}.patch",
            [HttpMethods.Get, HttpMethods.Head],
            GetPatchAsync);
        return app;
    }

    private static async Task<IResult> GetPatchAsync(
        string slug,
        string patchVersion,
        PatchArchiveService archiveService,
        CancellationToken cancellationToken)
    {
        var lookup = await archiveService.GetFileAsync(slug, patchVersion, cancellationToken);
        return lookup is null
            ? TypedResults.NotFound()
            : new PatchArchiveHttpResult(lookup);
    }
}
