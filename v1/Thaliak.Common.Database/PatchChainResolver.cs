using FFXIVDownloader.Thaliak;
using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database.Models;

namespace Thaliak.Common.Database;

public sealed class PatchChainResolver(ThaliakContext db)
{
    private const long MinimumPatchSize = 12;

    public async Task<IReadOnlyList<XivPatch>?> ResolveAsync(
        int repositoryId,
        string? fromVersion = null,
        string? toVersion = null,
        CancellationToken cancellationToken = default)
    {
        var versions = await db.RepoVersions
            .AsNoTracking()
            .Include(version => version.Patches)
            .Where(version => version.RepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        var repositorySlug = await db.Repositories
            .AsNoTracking()
            .Where(repository => repository.Id == repositoryId)
            .Select(repository => repository.Slug)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositorySlug is null)
        {
            return null;
        }

        var versionsByString = versions.ToDictionary(version => version.VersionString, StringComparer.Ordinal);

        var targetVersion = toVersion is null
            ? versions
                .SelectMany(version => version.Patches, (version, patch) => new { Version = version, Patch = patch })
                .Where(item => item.Patch.IsActive)
                .OrderByDescending(item => new GameVersion(item.Version.VersionString))
                .ThenByDescending(item => item.Patch.Id)
                .Select(item => item.Version)
                .FirstOrDefault()
            : versionsByString.GetValueOrDefault(toVersion);

        if (targetVersion is null)
        {
            return toVersion is null ? [] : null;
        }

        var upgradePaths = await db.UpgradePaths
            .AsNoTracking()
            .Where(upgradePath => upgradePath.RepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        var pathsByVersionId = upgradePaths
            .GroupBy(path => path.RepoVersionId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var versionStringsById = versions.ToDictionary(
            version => version.Id,
            version => new GameVersion(version.VersionString));
        var nodes = versions.Select(version =>
        {
            var paths = pathsByVersionId.GetValueOrDefault(version.Id, []);
            var edges = new List<PatchGraphEdge>();
            foreach (var path in paths)
            {
                if (path.PreviousRepoVersionId is null)
                {
                    edges.Add(new PatchGraphEdge(null, path.IsActive));
                }
                else if (versionStringsById.TryGetValue(
                             path.PreviousRepoVersionId.Value,
                             out var previousVersion))
                {
                    edges.Add(new PatchGraphEdge(previousVersion, path.IsActive));
                }
            }

            return new PatchGraphNode(
                new GameVersion(version.VersionString),
                version.Patches.Any(patch => patch.IsActive),
                version.Patches.Where(IsMeaningfulPatch).Sum(patch => patch.Size),
                edges);
        });

        var plan = PatchGraphPlanner.Resolve(
            repositorySlug,
            nodes,
            new GameVersion(targetVersion.VersionString),
            fromVersion is null ? null : new GameVersion(fromVersion));
        if (plan is null)
        {
            return null;
        }

        return plan.Versions
            .Select(version => versionsByString[version.ToString()])
            .SelectMany(version => version.Patches
                .Where(IsMeaningfulPatch)
                .OrderBy(patch => patch.Id))
            .ToArray();
    }

    private static bool IsMeaningfulPatch(XivPatch patch) =>
        patch.Size > MinimumPatchSize;
}
