using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Models;

namespace Thaliak.Service.Api.Services;

public sealed class PatchArchiveService(
    ThaliakContext db,
    IOptions<ArtifactOptions> options)
{
    public const int MaxRangesPerRequest = 64;
    public const long MaxRequestBytes = 512L << 20;

    private readonly ArtifactOptions _options = options.Value;

    public async Task<PatchArchiveLookup?> GetFileAsync(
        string repositorySlug,
        string patchVersion,
        CancellationToken cancellationToken)
    {
        var candidates = db.Patches
            .AsNoTracking()
            .Include(patch => patch.RepoVersion)
            .ThenInclude(version => version.Repository)
            .Where(patch => patch.RepoVersion.Repository.Slug == repositorySlug);
        var patch = await RepositoryPatchLookup.FindAsync(candidates, patchVersion, cancellationToken);
        if (patch is null || !TryResolveLocalFile(patch, out var fileInfo)) {
            return null;
        }

        return new PatchArchiveLookup(patch, fileInfo);
    }

    public IReadOnlyList<PatchSourceDto> GetSources(string repositorySlug, XivPatch patch)
    {
        var sources = new List<PatchSourceDto>(2);
        if (TryResolveLocalFile(patch, out _)) {
            sources.Add(new PatchSourceDto(
                "archive",
                BuildArchiveUrl(repositorySlug, patch),
                SupportsRangeRequests: true,
                SupportsMultipartRanges: true,
                MaxRangesPerRequest,
                MaxRequestBytes));
        }

        sources.Add(new PatchSourceDto(
            "origin",
            patch.RemoteOriginPath,
            SupportsRangeRequests: true,
            SupportsMultipartRanges: false,
            MaxRangesPerRequest: 1));
        return sources;
    }

    private string BuildArchiveUrl(string repositorySlug, XivPatch patch)
    {
        var path = $"/patches/{Uri.EscapeDataString(repositorySlug)}/"
                   + $"{Uri.EscapeDataString(RepositoryPatchLookup.GetPatchVersion(patch.RemoteOriginPath))}.patch";
        return string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? path
            : $"{_options.PublicBaseUrl.TrimEnd('/')}{path}";
    }

    private bool TryResolveLocalFile(XivPatch patch, out FileInfo fileInfo)
    {
        fileInfo = null!;
        if (string.IsNullOrWhiteSpace(_options.PatchRoot)) {
            return false;
        }

        string root;
        string absolutePath;
        try {
            root = Path.GetFullPath(_options.PatchRoot);
            absolutePath = Path.GetFullPath(Path.Combine(
                root,
                patch.LocalStoragePath.TrimStart('/', '\\')
                    .Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) {
            return false;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        fileInfo = new FileInfo(absolutePath);
        return fileInfo.Exists && (patch.Size <= 0 || fileInfo.Length == patch.Size);
    }

}

public sealed record PatchArchiveLookup(XivPatch Patch, FileInfo File);
