using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Models;

namespace Thaliak.Service.Api.Services;

public sealed class ThaliakReadService(ThaliakContext db)
{
    public async Task<RepositoriesResponseDto> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        var repositories = await db.Repositories
            .AsNoTracking()
            .OrderBy(repository => repository.ServiceId)
            .ThenBy(repository => repository.Id)
            .ToListAsync(cancellationToken);

        var latestPatches = await GetLatestPatchMapAsync(repositories.Select(repository => repository.Id), cancellationToken);
        var repositoryDtos = repositories
            .Select(repository => ToRepositoryDto(repository, latestPatches.GetValueOrDefault(repository.Id)))
            .ToArray();

        return new RepositoriesResponseDto(repositoryDtos, repositoryDtos.Length);
    }

    public async Task<RepositoryDto?> GetRepositoryAsync(string slug, CancellationToken cancellationToken)
    {
        var repository = await FindRepositoryAsync(slug, cancellationToken);
        if (repository is null) {
            return null;
        }

        var latestPatch = await GetLatestPatchAsync(repository.Id, cancellationToken);
        return ToRepositoryDto(repository, latestPatch);
    }

    public async Task<PatchesResponseDto?> GetRepositoryPatchesAsync(
        string slug,
        string? fromVersion,
        string? toVersion,
        bool all,
        bool? active,
        CancellationToken cancellationToken)
    {
        var repository = await FindRepositoryAsync(slug, cancellationToken);
        if (repository is null) {
            return null;
        }

        var patches = all
            ? await GetAllPatchesAsync(repository, active ?? true, cancellationToken)
            : await GetPatchChainAsync(repository, fromVersion, toVersion, cancellationToken);

        if (patches is null) {
            return null;
        }

        return new PatchesResponseDto(patches, patches.Count, patches.Sum(patch => patch.Size));
    }

    public async Task<PatchDto?> GetRepositoryPatchAsync(string slug, string version, CancellationToken cancellationToken)
    {
        var repository = await FindRepositoryAsync(slug, cancellationToken);
        if (repository is null) {
            return null;
        }

        var patch = await PatchQuery(repository.Id)
            .Where(patch => patch.RepoVersion.VersionString == version)
            .OrderBy(patch => patch.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return patch is null ? null : ToPatchDto(repository.Slug, patch);
    }

    public async Task<GraphQlRepositoryDto?> GetGraphQlMetadataAsync(string slug, CancellationToken cancellationToken)
    {
        var repository = await FindRepositoryAsync(slug, cancellationToken);
        if (repository is null) {
            return null;
        }

        var latestPatch = await GetLatestPatchAsync(repository.Id, cancellationToken);
        var latestVersion = latestPatch is null
            ? null
            : new GraphQlVersionReferenceDto(latestPatch.RepoVersion.VersionString);

        return new GraphQlRepositoryDto(repository.Name, repository.Description, latestVersion, null);
    }

    public async Task<GraphQlRepositoryDto?> GetGraphQlVersionsAsync(string slug, CancellationToken cancellationToken)
    {
        var repository = await FindRepositoryAsync(slug, cancellationToken);
        if (repository is null) {
            return null;
        }

        var versions = await db.RepoVersions
            .AsNoTracking()
            .Include(version => version.Patches)
            .Where(version => version.RepositoryId == repository.Id)
            .ToListAsync(cancellationToken);

        var versionIds = versions.Select(version => version.Id).ToArray();
        var upgradePaths = versionIds.Length == 0
            ? []
            : await db.UpgradePaths
                .AsNoTracking()
                .Include(upgradePath => upgradePath.PreviousRepoVersion)
                .Where(upgradePath => upgradePath.RepositoryId == repository.Id)
                .Where(upgradePath => versionIds.Contains(upgradePath.RepoVersionId))
                .ToListAsync(cancellationToken);

        var prerequisitesByVersion = upgradePaths
            .Where(upgradePath => upgradePath.PreviousRepoVersion is not null)
            .GroupBy(upgradePath => upgradePath.RepoVersionId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(upgradePath => upgradePath.LastOffered)
                    .ThenByDescending(upgradePath => upgradePath.Id)
                    .Select(upgradePath => new GraphQlVersionReferenceDto(upgradePath.PreviousRepoVersion!.VersionString))
                    .DistinctBy(version => version.VersionString)
                    .ToArray());

        var versionDtos = versions
            .OrderByDescending(version => VersionSortKey(version.VersionString), StringComparer.Ordinal)
            .ThenByDescending(version => version.Id)
            .Select(version => new GraphQlAnnotatedVersionDto(
                version.VersionString,
                version.Patches.Any(patch => patch.IsActive),
                prerequisitesByVersion.GetValueOrDefault(version.Id) ?? [],
                version.Patches
                    .OrderBy(patch => patch.Id)
                    .Select(patch => new GraphQlPatchDto(patch.RemoteOriginPath, patch.Size))
                    .ToArray()))
            .ToArray();

        return new GraphQlRepositoryDto(repository.Name, repository.Description, null, versionDtos);
    }

    private async Task<XivRepository?> FindRepositoryAsync(string slug, CancellationToken cancellationToken) =>
        await db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(repository => repository.Slug == slug, cancellationToken);

    private async Task<XivPatch?> GetLatestPatchAsync(int repositoryId, CancellationToken cancellationToken)
    {
        var latestPatches = await GetLatestPatchMapAsync([repositoryId], cancellationToken);
        return latestPatches.GetValueOrDefault(repositoryId);
    }

    private async Task<Dictionary<int, XivPatch>> GetLatestPatchMapAsync(
        IEnumerable<int> repositoryIds,
        CancellationToken cancellationToken)
    {
        var repositoryIdList = repositoryIds.Distinct().ToArray();
        if (repositoryIdList.Length == 0) {
            return new Dictionary<int, XivPatch>();
        }

        var patches = await db.Patches
            .AsNoTracking()
            .Include(patch => patch.RepoVersion)
            .Where(patch => patch.IsActive)
            .Where(patch => repositoryIdList.Contains(patch.RepoVersion.RepositoryId))
            .ToListAsync(cancellationToken);

        return patches
            .OrderByDescending(patch => VersionSortKey(patch.RepoVersion.VersionString), StringComparer.Ordinal)
            .ThenByDescending(patch => patch.Id)
            .GroupBy(patch => patch.RepoVersion.RepositoryId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<IReadOnlyList<PatchDto>> GetAllPatchesAsync(
        XivRepository repository,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var query = PatchQuery(repository.Id);

        if (activeOnly) {
            query = query.Where(patch => patch.IsActive);
        }

        var patches = await query.ToListAsync(cancellationToken);

        return patches
            .OrderBy(patch => VersionSortKey(patch.RepoVersion.VersionString), StringComparer.Ordinal)
            .ThenBy(patch => patch.Id)
            .Select(patch => ToPatchDto(repository.Slug, patch))
            .ToArray();
    }

    private async Task<IReadOnlyList<PatchDto>?> GetPatchChainAsync(
        XivRepository repository,
        string? fromVersion,
        string? toVersion,
        CancellationToken cancellationToken)
    {
        var versions = await db.RepoVersions
            .AsNoTracking()
            .Include(version => version.Patches)
            .Where(version => version.RepositoryId == repository.Id)
            .ToListAsync(cancellationToken);

        var versionsById = versions.ToDictionary(version => version.Id);
        var versionsByString = versions.ToDictionary(version => version.VersionString, StringComparer.Ordinal);

        var targetVersion = toVersion is null
            ? versions
                .SelectMany(version => version.Patches, (version, patch) => new { Version = version, Patch = patch })
                .Where(item => item.Patch.IsActive)
                .OrderByDescending(item => VersionSortKey(item.Version.VersionString), StringComparer.Ordinal)
                .ThenByDescending(item => item.Patch.Id)
                .Select(item => item.Version)
                .FirstOrDefault()
            : versionsByString.GetValueOrDefault(toVersion);

        if (targetVersion is null) {
            return toVersion is null ? [] : null;
        }

        var upgradePaths = await db.UpgradePaths
            .AsNoTracking()
            .Where(upgradePath => upgradePath.RepositoryId == repository.Id)
            .OrderByDescending(upgradePath => upgradePath.LastOffered)
            .ThenByDescending(upgradePath => upgradePath.Id)
            .ToListAsync(cancellationToken);

        var previousByVersion = upgradePaths
            .GroupBy(upgradePath => upgradePath.RepoVersionId)
            .ToDictionary(group => group.Key, group => group.First());

        var chain = new List<XivRepoVersion>();
        var currentVersion = targetVersion;
        var visitedVersionIds = new HashSet<int>();

        while (visitedVersionIds.Add(currentVersion.Id)) {
            chain.Add(currentVersion);

            if (string.Equals(currentVersion.VersionString, fromVersion, StringComparison.Ordinal)) {
                break;
            }

            if (!previousByVersion.TryGetValue(currentVersion.Id, out var upgradePath)
                || upgradePath.PreviousRepoVersionId is null) {
                if (fromVersion is not null) {
                    return null;
                }

                break;
            }

            if (!versionsById.TryGetValue(upgradePath.PreviousRepoVersionId.Value, out currentVersion!)) {
                return fromVersion is null ? chain.SelectMany(ToPatchDtos).Reverse().ToArray() : null;
            }
        }

        if (fromVersion is not null && chain.All(version => version.VersionString != fromVersion)) {
            return null;
        }

        chain.Reverse();
        return chain.SelectMany(ToPatchDtos).ToArray();

        IEnumerable<PatchDto> ToPatchDtos(XivRepoVersion version) =>
            version.Patches
                .OrderBy(patch => patch.Id)
                .Select(patch => ToPatchDto(repository.Slug, patch));
    }

    private IQueryable<XivPatch> PatchQuery(int repositoryId) =>
        db.Patches
            .AsNoTracking()
            .Include(patch => patch.RepoVersion)
            .Where(patch => patch.RepoVersion.RepositoryId == repositoryId);

    private static RepositoryDto ToRepositoryDto(XivRepository repository, XivPatch? latestPatch) =>
        new(
            ToServiceId(repository.ServiceId),
            repository.Slug,
            repository.Name,
            repository.Description ?? string.Empty,
            latestPatch is null
                ? null
                : new LatestPatchInfoDto(
                    latestPatch.RepoVersion.VersionString,
                    latestPatch.FirstOffered ?? DateTime.UnixEpoch,
                    latestPatch.LastOffered ?? DateTime.UnixEpoch));

    private static PatchDto ToPatchDto(string repositorySlug, XivPatch patch) =>
        new(
            repositorySlug,
            patch.RepoVersion.VersionString,
            patch.RemoteOriginPath,
            patch.LocalStoragePath,
            patch.FirstSeen,
            patch.LastSeen,
            patch.Size,
            ToPatchHashDto(patch),
            patch.FirstOffered,
            patch.LastOffered,
            patch.IsActive);

    private static PatchHashDto ToPatchHashDto(XivPatch patch)
    {
        var hashType = patch.HashType?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hashType) || hashType == "none") {
            return new PatchHashDto("none");
        }

        return new PatchHashDto(hashType, patch.HashBlockSize, patch.Hashes ?? []);
    }

    private static string ToServiceId(int serviceId) =>
        serviceId switch
        {
            1 => "jp",
            2 => "kr",
            3 => "cn",
            4 => "tw",
            _ => $"service-{serviceId}"
        };

    private static string VersionSortKey(string versionString) =>
        versionString.TrimStart('H', 'D');
}
