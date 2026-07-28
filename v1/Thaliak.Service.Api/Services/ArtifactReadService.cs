using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Models;

namespace Thaliak.Service.Api.Services;

public sealed class ArtifactReadService(
    ThaliakContext db,
    ArtifactPathService pathService,
    IOptions<ArtifactOptions> options)
{
    private readonly ArtifactOptions _options = options.Value;

    public async Task<ArtifactRegionsResponseDto> GetRegionsAsync(CancellationToken cancellationToken)
    {
        var artifacts = await GetReadyArtifactsAsync(cancellationToken);
        var regions = ArtifactTargetCatalog.Regions
            .Select(region => BuildRegionDto(region, artifacts))
            .ToArray();

        return new ArtifactRegionsResponseDto(regions);
    }

    public async Task<ArtifactRegionDto?> GetRegionAsync(string region, CancellationToken cancellationToken)
    {
        if (ArtifactTargetCatalog.ForRegion(region).Count == 0) {
            return null;
        }

        var artifacts = await GetReadyArtifactsAsync(cancellationToken);
        return BuildRegionDto(region, artifacts);
    }

    public async Task<ArtifactFileLookup?> GetFileAsync(
        string kind,
        string slugOrAlias,
        string versionString,
        CancellationToken cancellationToken)
    {
        var repositorySlug = ArtifactTargetCatalog.ResolveSlugOrAlias(slugOrAlias);
        var artifact = await db.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Kind == kind)
            .Where(artifact => artifact.RepositorySlug == repositorySlug)
            .Where(artifact => artifact.VersionString == versionString)
            .Where(artifact => artifact.Status == "ready")
            .FirstOrDefaultAsync(cancellationToken);

        if (artifact is null) {
            return null;
        }

        var absolutePath = pathService.GetAbsolutePath(artifact.RelativePath);
        if (!pathService.IsUnderRoot(absolutePath) || !File.Exists(absolutePath)) {
            return null;
        }

        return new ArtifactFileLookup(artifact, absolutePath);
    }

    public string BuildArtifactUrl(string kind, string repositorySlug, string versionString)
    {
        var path = $"/{kind}s/{repositorySlug}/{versionString}.{kind}";
        return string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? path
            : $"{_options.PublicBaseUrl.TrimEnd('/')}{path}";
    }

    private async Task<List<XivArtifact>> GetReadyArtifactsAsync(CancellationToken cancellationToken) =>
        await db.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Status == "ready")
            .ToListAsync(cancellationToken);

    private ArtifactRegionDto BuildRegionDto(string region, IReadOnlyList<XivArtifact> artifacts)
    {
        var repositories = ArtifactTargetCatalog.ForRegion(region)
            .Select(target => BuildRepositoryDto(target, artifacts))
            .ToArray();

        var isReady = repositories.Length > 0 && repositories.All(repository => repository.LatestClut is not null);
        var readyAtUtc = isReady
            ? repositories
                .Select(repository => repository.LatestClut?.ReadyAtUtc)
                .Where(readyAt => readyAt.HasValue)
                .Max()
            : null;

        return new ArtifactRegionDto(region.ToLowerInvariant(), isReady, readyAtUtc, repositories);
    }

    private ArtifactRepositoryDto BuildRepositoryDto(ArtifactTarget target, IReadOnlyList<XivArtifact> artifacts)
    {
        var latestClut = FindLatestArtifact(artifacts, "clut", target.RepositorySlug);
        var latestLut = FindLatestArtifact(artifacts, "lut", target.RepositorySlug);

        return new ArtifactRepositoryDto(
            target.RepositorySlug,
            target.Expansion,
            target.Description,
            target.Aliases,
            latestClut is null ? null : ToFileDto(latestClut),
            latestLut is null ? null : ToFileDto(latestLut));
    }

    private ArtifactFileDto ToFileDto(XivArtifact artifact) =>
        new(
            artifact.Kind,
            artifact.VersionString,
            BuildArtifactUrl(artifact.Kind, artifact.RepositorySlug, artifact.VersionString),
            artifact.Size,
            artifact.Sha256,
            artifact.ReadyAtUtc);

    private static XivArtifact? FindLatestArtifact(
        IEnumerable<XivArtifact> artifacts,
        string kind,
        string repositorySlug) =>
        artifacts
            .Where(artifact => artifact.Kind == kind)
            .Where(artifact => artifact.RepositorySlug == repositorySlug)
            .OrderByDescending(artifact => VersionSortKey(artifact.VersionString), StringComparer.Ordinal)
            .ThenByDescending(artifact => artifact.ReadyAtUtc)
            .FirstOrDefault();

    private static string VersionSortKey(string versionString) =>
        versionString.TrimStart('H', 'D');
}

public sealed record ArtifactFileLookup(XivArtifact Artifact, string AbsolutePath);
