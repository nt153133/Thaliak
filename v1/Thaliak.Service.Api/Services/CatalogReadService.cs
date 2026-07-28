using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Models;

namespace Thaliak.Service.Api.Services;

public sealed class CatalogReadService(
    ThaliakContext db,
    ArtifactPathService pathService,
    IOptions<ArtifactOptions> options)
{
    private readonly ArtifactOptions _options = options.Value;

    public ServicesResponseDto GetServices()
    {
        var services = ArtifactTargetCatalog.Regions
            .Select(region => new ServiceDto(
                region,
                ToServiceName(region),
                region,
                ArtifactTargetCatalog.ForRegion(region)
                    .Select(target => new ServiceRepositoryDto(
                        target.RepositorySlug,
                        target.Expansion,
                        target.Description,
                        target.Aliases))
                    .ToArray()))
            .ToArray();

        return new ServicesResponseDto(services, services.Length);
    }

    public async Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var databaseStatus = await db.Database.CanConnectAsync(cancellationToken) ? "ok" : "unavailable";
        var artifacts = await db.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Kind == "clut")
            .Where(artifact => artifact.Status == "ready")
            .ToListAsync(cancellationToken);

        var regions = ArtifactTargetCatalog.Regions
            .Select(region =>
            {
                var targets = ArtifactTargetCatalog.ForRegion(region);
                var readyArtifacts = artifacts
                    .Where(artifact => targets.Any(target => target.RepositorySlug == artifact.RepositorySlug))
                    .GroupBy(artifact => artifact.RepositorySlug)
                    .Select(group => group
                        .OrderByDescending(artifact => VersionSortKey(artifact.VersionString), StringComparer.Ordinal)
                        .ThenByDescending(artifact => artifact.ReadyAtUtc)
                        .First())
                    .ToArray();

                return new ArtifactRegionSummaryDto(
                    region,
                    targets.Count,
                    readyArtifacts.Length,
                    readyArtifacts
                        .Select(artifact => artifact.ReadyAtUtc)
                        .Where(readyAt => readyAt.HasValue)
                        .DefaultIfEmpty()
                        .Max());
            })
            .ToArray();

        var status = databaseStatus == "ok" ? "ok" : "degraded";
        return new StatusDto(
            status,
            DateTime.UtcNow,
            new DatabaseStatusDto(databaseStatus),
            new ArtifactStatusDto(pathService.Root, _options.Enabled, regions));
    }

    private static string ToServiceName(string region) =>
        region.ToLowerInvariant() switch
        {
            "global" => "FFXIV Global",
            "china" => "FFXIV China",
            "tc" => "FFXIV Traditional Chinese",
            _ => region
        };

    private static string VersionSortKey(string versionString) =>
        versionString.TrimStart('H', 'D');
}
