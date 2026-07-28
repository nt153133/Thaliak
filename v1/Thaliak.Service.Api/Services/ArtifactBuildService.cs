using System.Security.Cryptography;
using FFXIVDownloader.Lut;
using FFXIVDownloader.Thaliak;
using FFXIVDownloader.ZiPatch;
using FFXIVDownloader.ZiPatch.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;
using static FFXIVDownloader.ZiPatch.Config.ZiPatchConfig;

namespace Thaliak.Service.Api.Services;

public sealed class ArtifactBuildService(
    ThaliakContext db,
    ArtifactPathService pathService,
    ArtifactWebhookService webhookService,
    IOptions<ArtifactOptions> options,
    ILogger<ArtifactBuildService> logger)
{
    private readonly ArtifactOptions _options = options.Value;
    private readonly PatchChainResolver _patchChainResolver = new(db);

    public async Task BuildAllAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        Directory.CreateDirectory(pathService.Root);

        foreach (var region in ArtifactTargetCatalog.Regions) {
            foreach (var target in ArtifactTargetCatalog.ForRegion(region)) {
                await BuildTargetAsync(target, cancellationToken);
            }

            await NotifyRegionIfReadyAsync(region, cancellationToken);
        }
    }

    private async Task BuildTargetAsync(ArtifactTarget target, CancellationToken cancellationToken)
    {
        var repository = await db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(repository => repository.Slug == target.RepositorySlug, cancellationToken);
        if (repository is null) {
            logger.LogWarning("Artifact target repository {RepositorySlug} was not found.", target.RepositorySlug);
            return;
        }

        var latestPatch = await db.Patches
            .AsNoTracking()
            .Include(patch => patch.RepoVersion)
            .Where(patch => patch.IsActive)
            .Where(patch => patch.RepoVersion.RepositoryId == repository.Id)
            .ToListAsync(cancellationToken);

        var latestVersion = latestPatch
            .OrderByDescending(patch => VersionSortKey(patch.RepoVersion.VersionString), StringComparer.Ordinal)
            .ThenByDescending(patch => patch.Id)
            .Select(patch => patch.RepoVersion.VersionString)
            .FirstOrDefault();
        if (latestVersion is null) {
            return;
        }

        var chain = await _patchChainResolver.ResolveAsync(repository.Id, null, latestVersion, cancellationToken);
        if (chain is null || chain.Count == 0) {
            return;
        }

        var entries = new List<ClutBuilder.ChainEntry>(chain.Count);
        foreach (var patch in chain) {
            var localPatchPath = ResolveLocalPatchPath(patch);
            if (localPatchPath is null) {
                logger.LogInformation(
                    "Waiting for local patch file for {RepositorySlug} {VersionString}.",
                    target.RepositorySlug,
                    patch.RepoVersion.VersionString);
                return;
            }

            var patchVersion = ToPatchVersion(patch);
            entries.Add(new ClutBuilder.ChainEntry(
                new GameVersion(patch.RepoVersion.VersionString),
                patchVersion,
                localPatchPath));
        }

        var latestEntry = entries[^1];
        var latestClutPath = pathService.GetAbsolutePath(
            "clut",
            target.RepositorySlug,
            latestEntry.GameVersion.ToString());
        if (await IsReadyArtifactAsync(
                "clut",
                target.RepositorySlug,
                latestEntry.GameVersion.ToString(),
                latestClutPath,
                cancellationToken)) {
            return;
        }

        var luts = new List<LutFile>(entries.Count);
        for (var i = 0; i < entries.Count; i++) {
            luts.Add(await GetOrBuildLutAsync(
                target,
                chain[i],
                entries[i].PatchVersion,
                entries[i].LocalPatchPath,
                cancellationToken));
        }

        await BuildClutsAsync(target, entries, luts, cancellationToken);
    }

    private async Task<LutFile> GetOrBuildLutAsync(
        ArtifactTarget target,
        XivPatch patch,
        PatchVersion patchVersion,
        string localPatchPath,
        CancellationToken cancellationToken)
    {
        var versionString = patchVersion.ToString();
        var absolutePath = pathService.GetAbsolutePath("lut", target.RepositorySlug, versionString);
        if (File.Exists(absolutePath)) {
            var existingLut = LoadLut(absolutePath);
            if (!await IsReadyArtifactAsync(
                    "lut",
                    target.RepositorySlug,
                    versionString,
                    absolutePath,
                    cancellationToken)) {
                await UpsertArtifactAsync(
                    "lut",
                    target.Region,
                    target.RepositorySlug,
                    versionString,
                    absolutePath,
                    cancellationToken);
            }

            return existingLut;
        }

        var compression = ParseCompression();
        var lutFile = new LutFile
        {
            Header = new LutHeader
            {
                Compression = compression,
                Version = patchVersion,
                Repository = target.RepositorySlug
            }
        };

        await using (var patchFileStream = File.OpenRead(localPatchPath))
        {
            using var bufferedStream = new BufferedStream(patchFileStream, 1 << 20);
            using var patchStream = new PositionedStream(bufferedStream);
            using var ziPatchFile = new ZiPatchFile(patchStream);
            await foreach (var chunk in ziPatchFile.GetChunksAsync(cancellationToken).WithCancellation(cancellationToken)) {
                lutFile.Chunks.Add(new LutChunk(chunk));
            }
        }

        await WriteArtifactFileAsync(absolutePath, writer => lutFile.Write(writer), cancellationToken);
        await UpsertArtifactAsync("lut", target.Region, target.RepositorySlug, versionString, absolutePath, cancellationToken);
        return lutFile;
    }

    private async Task BuildClutsAsync(
        ArtifactTarget target,
        IReadOnlyList<ClutBuilder.ChainEntry> entries,
        IReadOnlyList<LutFile> luts,
        CancellationToken cancellationToken)
    {
        var compression = ParseCompression();
        var (accumulator, resumeIndex) = TryLoadLatestAccumulator(target, entries, compression);
        accumulator ??= ClutAccumulator.Create(
            target.RepositorySlug,
            PlatformId.Win32,
            compression,
            _options.BasePatchUrl);
        accumulator.BasePatchUrl = _options.BasePatchUrl;

        if (resumeIndex >= 0) {
            var resumeVersion = entries[resumeIndex].GameVersion.ToString();
            var resumePath = pathService.GetAbsolutePath("clut", target.RepositorySlug, resumeVersion);
            if (!await IsReadyArtifactAsync(
                    "clut",
                    target.RepositorySlug,
                    resumeVersion,
                    resumePath,
                    cancellationToken)) {
                await UpsertArtifactAsync(
                    "clut",
                    target.Region,
                    target.RepositorySlug,
                    resumeVersion,
                    resumePath,
                    cancellationToken);
            }

            logger.LogInformation(
                "Resuming CLUT generation for {RepositorySlug} after {VersionString} ({CompletedCount}/{TotalCount}).",
                target.RepositorySlug,
                resumeVersion,
                resumeIndex + 1,
                entries.Count);
        }

        for (var i = resumeIndex + 1; i < entries.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            accumulator.Apply(entry.GameVersion, luts[i]);

            var versionString = entry.GameVersion.ToString();
            var absolutePath = pathService.GetAbsolutePath("clut", target.RepositorySlug, versionString);
            await WriteArtifactStreamAsync(
                absolutePath,
                stream => accumulator.Write(stream),
                cancellationToken);
            await UpsertArtifactAsync(
                "clut",
                target.Region,
                target.RepositorySlug,
                versionString,
                absolutePath,
                cancellationToken);
        }
    }

    private (ClutAccumulator? Accumulator, int ResumeIndex) TryLoadLatestAccumulator(
        ArtifactTarget target,
        IReadOnlyList<ClutBuilder.ChainEntry> entries,
        CompressType compression)
    {
        for (var i = entries.Count - 1; i >= 0; i--) {
            var entry = entries[i];
            var versionString = entry.GameVersion.ToString();
            var absolutePath = pathService.GetAbsolutePath("clut", target.RepositorySlug, versionString);
            if (!File.Exists(absolutePath)) {
                continue;
            }

            try {
                using var stream = File.OpenRead(absolutePath);
                var accumulator = ClutAccumulator.Load(stream);
                if (accumulator.Repository != target.RepositorySlug
                    || accumulator.Platform != PlatformId.Win32
                    || accumulator.Compression != compression
                    || accumulator.Version != entry.GameVersion
                    || accumulator.PatchVersion != entry.PatchVersion) {
                    logger.LogWarning(
                        "Ignoring mismatched CLUT snapshot {ArtifactPath} while resuming {RepositorySlug}.",
                        absolutePath,
                        target.RepositorySlug);
                    continue;
                }

                return (accumulator, i);
            }
            catch (Exception ex) when (ex is IOException
                                           or InvalidDataException
                                           or LutException
                                           or ArgumentException
                                           or FormatException
                                           or OverflowException) {
                logger.LogWarning(
                    ex,
                    "Ignoring unreadable CLUT snapshot {ArtifactPath} while resuming {RepositorySlug}.",
                    absolutePath,
                    target.RepositorySlug);
            }
        }

        return (null, -1);
    }

    private static LutFile LoadLut(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        using var reader = new BinaryReader(stream);
        return new LutFile(reader);
    }

    private async Task<bool> IsReadyArtifactAsync(
        string kind,
        string repositorySlug,
        string versionString,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath)) {
            return false;
        }

        var relativePath = pathService.GetRelativePath(kind, repositorySlug, versionString);
        return await db.Artifacts
            .AsNoTracking()
            .AnyAsync(
                artifact => artifact.Kind == kind
                    && artifact.RepositorySlug == repositorySlug
                    && artifact.VersionString == versionString
                    && artifact.RelativePath == relativePath
                    && artifact.Status == "ready",
                cancellationToken);
    }

    private async Task NotifyRegionIfReadyAsync(string region, CancellationToken cancellationToken)
    {
        var targets = ArtifactTargetCatalog.ForRegion(region);
        var latestArtifacts = new List<XivArtifact>(targets.Count);

        foreach (var target in targets) {
            var readyArtifacts = await db.Artifacts
                .AsTracking()
                .Where(artifact => artifact.Kind == "clut")
                .Where(artifact => artifact.Region == region)
                .Where(artifact => artifact.RepositorySlug == target.RepositorySlug)
                .Where(artifact => artifact.Status == "ready")
                .ToListAsync(cancellationToken);
            var latestArtifact = readyArtifacts
                .OrderByDescending(artifact => VersionSortKey(artifact.VersionString), StringComparer.Ordinal)
                .ThenByDescending(artifact => artifact.ReadyAtUtc)
                .FirstOrDefault();

            if (latestArtifact is null) {
                return;
            }

            latestArtifacts.Add(latestArtifact);
        }

        if (latestArtifacts.All(artifact => artifact.NotifiedAtUtc.HasValue)) {
            return;
        }

        await webhookService.SendClutReadyAsync(region, latestArtifacts, cancellationToken);

        var notifiedAtUtc = DateTime.UtcNow;
        foreach (var artifact in latestArtifacts) {
            artifact.NotifiedAtUtc ??= notifiedAtUtc;
            artifact.UpdatedAtUtc = notifiedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private string? ResolveLocalPatchPath(XivPatch patch)
    {
        var localStoragePath = patch.LocalStoragePath;
        if (Path.IsPathRooted(localStoragePath) && File.Exists(localStoragePath)) {
            return localStoragePath;
        }

        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(_options.PatchRoot)
                ? localStoragePath
                : Path.Combine(_options.PatchRoot, localStoragePath.TrimStart('/', '\\')),
            localStoragePath.TrimStart('/', '\\')
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private async Task UpsertArtifactAsync(
        string kind,
        string region,
        string repositorySlug,
        string versionString,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var relativePath = pathService.GetRelativePath(kind, repositorySlug, versionString);
        var fileInfo = new FileInfo(absolutePath);
        var sha256 = await ComputeSha256Async(absolutePath, cancellationToken);

        var artifact = await db.Artifacts
            .AsTracking()
            .FirstOrDefaultAsync(
                artifact => artifact.Kind == kind
                    && artifact.RepositorySlug == repositorySlug
                    && artifact.VersionString == versionString,
                cancellationToken);

        if (artifact is null) {
            artifact = new XivArtifact
            {
                Kind = kind,
                Region = region,
                RepositorySlug = repositorySlug,
                VersionString = versionString,
                CreatedAtUtc = now
            };
            db.Artifacts.Add(artifact);
        }

        artifact.RelativePath = relativePath;
        artifact.Size = fileInfo.Length;
        artifact.Sha256 = sha256;
        artifact.Status = "ready";
        artifact.Error = null;
        artifact.UpdatedAtUtc = now;
        artifact.ReadyAtUtc ??= now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task WriteArtifactFileAsync(
        string absolutePath,
        Action<BinaryWriter> write,
        CancellationToken cancellationToken)
    {
        await WriteArtifactStreamAsync(
            absolutePath,
            stream => {
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                write(writer);
            },
            cancellationToken);
    }

    private static async Task WriteArtifactStreamAsync(
        string absolutePath,
        Action<Stream> write,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var tempPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";

        try {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, absolutePath, true);
        }
        finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string absolutePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(absolutePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private CompressType ParseCompression() =>
        Enum.TryParse<CompressType>(_options.Compression, ignoreCase: true, out var compression)
            ? compression
            : CompressType.Brotli;

    private static PatchVersion ToPatchVersion(XivPatch patch)
    {
        var fileName = Path.GetFileNameWithoutExtension(patch.RemoteOriginPath);
        return new PatchVersion(fileName);
    }

    private static string VersionSortKey(string versionString) =>
        versionString.TrimStart('H', 'D');
}
