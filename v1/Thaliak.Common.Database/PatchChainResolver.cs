using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database.Models;

namespace Thaliak.Common.Database;

public sealed class PatchChainResolver(ThaliakContext db)
{
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

        if (targetVersion is null)
        {
            return toVersion is null ? [] : null;
        }

        var upgradePaths = await db.UpgradePaths
            .AsNoTracking()
            .Where(upgradePath => upgradePath.RepositoryId == repositoryId)
            .OrderByDescending(upgradePath => upgradePath.LastOffered)
            .ThenByDescending(upgradePath => upgradePath.Id)
            .ToListAsync(cancellationToken);

        var previousByVersion = upgradePaths
            .GroupBy(upgradePath => upgradePath.RepoVersionId)
            .ToDictionary(group => group.Key, group => group.First());

        var chain = new List<XivRepoVersion>();
        var currentVersion = targetVersion;
        var visitedVersionIds = new HashSet<int>();

        while (visitedVersionIds.Add(currentVersion.Id))
        {
            chain.Add(currentVersion);

            if (string.Equals(currentVersion.VersionString, fromVersion, StringComparison.Ordinal))
            {
                break;
            }

            if (!previousByVersion.TryGetValue(currentVersion.Id, out var upgradePath)
                || upgradePath.PreviousRepoVersionId is null)
            {
                if (fromVersion is not null)
                {
                    return null;
                }

                break;
            }

            if (!versionsById.TryGetValue(upgradePath.PreviousRepoVersionId.Value, out currentVersion!))
            {
                return fromVersion is null ? Flatten(chain).Reverse().ToArray() : null;
            }
        }

        if (fromVersion is not null
            && chain.All(version => !string.Equals(version.VersionString, fromVersion, StringComparison.Ordinal)))
        {
            return null;
        }

        chain.Reverse();
        return Flatten(chain).ToArray();
    }

    private static IEnumerable<XivPatch> Flatten(IEnumerable<XivRepoVersion> versions) =>
        versions.SelectMany(version => version.Patches.OrderBy(patch => patch.Id));

    private static string VersionSortKey(string version)
    {
        var match = XivRepoVersion.VersionRegex.Match(version);
        return match.Success ? match.Groups[1].Value : version;
    }
}
