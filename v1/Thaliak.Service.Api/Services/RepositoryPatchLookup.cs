using FFXIVDownloader.Thaliak;
using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database.Models;

namespace Thaliak.Service.Api.Services;

internal static class RepositoryPatchLookup
{
    public static async Task<XivPatch?> FindAsync(
        IQueryable<XivPatch> patches,
        string requestedVersion,
        CancellationToken cancellationToken)
    {
        if (!TryParseVersion(requestedVersion, out var parsedVersion)) {
            return null;
        }

        var repositoryVersion = ToRepositoryVersion(parsedVersion);
        var hyphenatedVersion = repositoryVersion.Replace('.', '-');
        var filenameCandidates = await patches
            .Where(patch =>
                patch.RemoteOriginPath.Contains(repositoryVersion)
                || patch.RemoteOriginPath.Contains(hyphenatedVersion))
            .OrderBy(patch => patch.Id)
            .ToListAsync(cancellationToken);

        var exactPatch = filenameCandidates.FirstOrDefault(candidate =>
            TryParseVersion(GetPatchVersion(candidate.RemoteOriginPath), out var candidateVersion)
            && candidateVersion == parsedVersion);
        if (exactPatch is not null) {
            return exactPatch;
        }

        if (!string.Equals(requestedVersion, repositoryVersion, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return await patches
            .Where(patch => patch.RepoVersion.VersionString == repositoryVersion)
            .OrderBy(patch => patch.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static string GetPatchVersion(string patchUrl)
    {
        var path = Uri.TryCreate(patchUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : patchUrl;
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string ToRepositoryVersion(PatchVersion version) =>
        $"{version.Year:D4}.{version.Month:D2}.{version.Day:D2}.{version.Part:D4}.{version.Revision:D4}";

    private static bool TryParseVersion(string version, out PatchVersion result)
    {
        if (string.IsNullOrWhiteSpace(version)) {
            result = default;
            return false;
        }

        try {
            result = new PatchVersion(version);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or FormatException
                                          or OverflowException
                                          or IndexOutOfRangeException) {
            result = default;
            return false;
        }
    }
}
