using Microsoft.AspNetCore.Http.HttpResults;
using Thaliak.Service.Api.Models;
using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

public static class ServiceEndpoints
{
    public static RouteGroupBuilder MapServiceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/services", GetServices);
        group.MapGet("/status", GetStatusAsync);

        return group;
    }

    private static Ok<ServicesResponseDto> GetServices(CatalogReadService readService) =>
        TypedResults.Ok(readService.GetServices());

    private static async Task<Ok<StatusDto>> GetStatusAsync(
        CatalogReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetStatusAsync(cancellationToken));
}
