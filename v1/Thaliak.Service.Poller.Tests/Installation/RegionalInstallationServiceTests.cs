using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Installation;
using Thaliak.Service.Poller.Patch;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Installation;

public sealed class RegionalInstallationServiceTests
{
    [Fact]
    public void InstallationOptions_DoNotPrepopulateRegions()
    {
        Assert.Empty(new InstallationOptions().Regions);
    }

    [Fact]
    public async Task ReconcileAsync_WithCompleteTcChains_InstallsAndResumesAllRepositories()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        await SeedTcPatchesAsync(db, paths.PatchRoot, expansionCount: 6);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();
        await service.ReconcileAsync();

        Assert.Equal(6, applier.AppliedPatches.Count);
        var states = await db.InstallationStates.OrderBy(state => state.RepositoryId).ToListAsync();
        Assert.Equal(6, states.Count);
        Assert.All(states, state => Assert.Equal(InstallationStatus.Current, state.Status));

        var regionRoot = new DirectoryInfo(Path.Combine(paths.InstallationRoot, "tc"));
        Assert.Equal("2026.01.01.0000.0000", Repository.Ffxiv.GetVer(regionRoot));
        Assert.Equal("2026.01.06.0000.0000", Repository.Ex5.GetVer(regionRoot));
    }

    [Fact]
    public async Task ReconcileAsync_WithUnavailableExpansion_MarksItIncomplete()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        await SeedTcPatchesAsync(db, paths.PatchRoot, expansionCount: 1);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();

        Assert.Single(applier.AppliedPatches);
        Assert.Equal(
            InstallationStatus.Current,
            (await db.InstallationStates.SingleAsync(state => state.RepositoryId == 20)).Status);
        Assert.Equal(
            InstallationStatus.Incomplete,
            (await db.InstallationStates.SingleAsync(state => state.RepositoryId == 21)).Status);
    }

    [Fact]
    public async Task ReconcileAsync_WhenPatchApplicationFails_DoesNotAdvanceState()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        await SeedTcPatchesAsync(db, paths.PatchRoot, expansionCount: 1);
        var applier = new RecordingPatchApplicationService { Failure = new IOException("apply failed") };
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();

        var state = await db.InstallationStates.SingleAsync(item => item.RepositoryId == 20);
        Assert.Equal(InstallationStatus.Failed, state.Status);
        Assert.Null(state.LastAppliedPatchId);
        Assert.Contains("apply failed", state.LastError);
    }

    [Fact]
    public async Task ReconcileAsync_WhenPatchFileIsMissing_LeavesRepositoryPending()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        await SeedTcPatchesAsync(db, paths.PatchRoot, expansionCount: 1, createFiles: false);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();

        var state = await db.InstallationStates.SingleAsync(item => item.RepositoryId == 20);
        Assert.Equal(InstallationStatus.Pending, state.Status);
        Assert.Empty(applier.AppliedPatches);
    }

    [Fact]
    public async Task ReconcileAsync_WhenInstalledPatchWasSuperseded_AppliesReplacementSuffixOnce()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        var patches = await SeedSupersededTcPatchAsync(db, paths, hasSharedAncestor: true);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();
        await service.ReconcileAsync();

        Assert.Single(applier.AppliedPatches);
        Assert.Equal(
            new FileInfo(Path.Combine(paths.PatchRoot, patches.Replacement.LocalStoragePath)).FullName,
            applier.AppliedPatches[0]);
        var state = await db.InstallationStates.SingleAsync(item => item.RepositoryId == 20);
        Assert.Equal(InstallationStatus.Current, state.Status);
        Assert.Equal(patches.Replacement.Id, state.LastAppliedPatchId);
        Assert.Equal(patches.Replacement.RepoVersion.VersionString, state.InstalledVersion);
        var regionRoot = new DirectoryInfo(Path.Combine(paths.InstallationRoot, "tc"));
        Assert.Equal(patches.Replacement.RepoVersion.VersionString, Repository.Ffxiv.GetVer(regionRoot));
    }

    [Fact]
    public async Task ReconcileAsync_WhenReplacementPatchIsUnavailable_LeavesSupersededStatePending()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        var patches = await SeedSupersededTcPatchAsync(
            db,
            paths,
            hasSharedAncestor: true,
            createReplacementFile: false);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();

        Assert.Empty(applier.AppliedPatches);
        var state = await db.InstallationStates.SingleAsync(item => item.RepositoryId == 20);
        Assert.Equal(InstallationStatus.Pending, state.Status);
        Assert.Equal(patches.Superseded.Id, state.LastAppliedPatchId);
        Assert.Equal(patches.Superseded.RepoVersion.VersionString, state.InstalledVersion);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSupersededHistoryHasNoSharedAncestor_StillNeedsRebuild()
    {
        using var paths = new TestPaths();
        await using var db = CreateContext(paths.DatabasePath);
        await db.Database.MigrateAsync();
        var patches = await SeedSupersededTcPatchAsync(db, paths, hasSharedAncestor: false);
        var applier = new RecordingPatchApplicationService();
        var service = CreateService(db, applier, paths);

        await service.ReconcileAsync();

        Assert.Empty(applier.AppliedPatches);
        var state = await db.InstallationStates.SingleAsync(item => item.RepositoryId == 20);
        Assert.Equal(InstallationStatus.NeedsRebuild, state.Status);
        Assert.Equal(patches.Superseded.Id, state.LastAppliedPatchId);
        Assert.Contains("no safe superseded-chain rebase", state.LastError);
    }

    private static RegionalInstallationService CreateService(
        ThaliakContext db,
        IPatchApplicationService applier,
        TestPaths paths)
    {
        var options = Options.Create(new InstallationOptions
        {
            Enabled = true,
            Root = paths.InstallationRoot,
            Regions = ["TC"]
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Directories:Patches"] = paths.PatchRoot
            })
            .Build();

        return new RegionalInstallationService(db, applier, options, configuration);
    }

    private static ThaliakContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ThaliakContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ThaliakContext(options);
    }

    private static async Task SeedTcPatchesAsync(
        ThaliakContext db,
        string patchRoot,
        int expansionCount,
        bool createFiles = true)
    {
        for (var expansionId = 0; expansionId < expansionCount; expansionId++)
        {
            var repositoryId = 20 + expansionId;
            var versionString = $"2026.01.{expansionId + 1:00}.0000.0000";
            var version = new XivRepoVersion
            {
                RepositoryId = repositoryId,
                VersionString = versionString
            };
            var patch = new XivPatch
            {
                RepoVersion = version,
                RemoteOriginPath =
                    $"https://mydownloadakamai.ffxiv.com.tw/ffxiv/260101/ex{expansionId}/{versionString}.patch",
                Size = 16,
                IsActive = true,
                FirstOffered = DateTime.UtcNow,
                LastOffered = DateTime.UtcNow
            };
            version.Patches.Add(patch);
            db.RepoVersions.Add(version);
            db.UpgradePaths.Add(new XivUpgradePath
            {
                RepositoryId = repositoryId,
                RepoVersion = version,
                IsActive = true,
                FirstOffered = DateTime.UtcNow,
                LastOffered = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            if (createFiles)
            {
                var patchPath = Path.Combine(patchRoot, patch.LocalStoragePath);
                Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
                await File.WriteAllBytesAsync(
                    patchPath,
                    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
            }
        }
    }

    private static async Task<SupersededPatchSet> SeedSupersededTcPatchAsync(
        ThaliakContext db,
        TestPaths paths,
        bool hasSharedAncestor,
        bool createReplacementFile = true)
    {
        const int repositoryId = 20;
        var shared = CreatePatch(repositoryId, "2026.01.01.0000.0000", isActive: true);
        var superseded = CreatePatch(repositoryId, "2026.01.02.0000.0000", isActive: false);
        var replacement = CreatePatch(repositoryId, "2026.01.03.0000.0000", isActive: true);

        db.RepoVersions.AddRange(shared.RepoVersion, superseded.RepoVersion, replacement.RepoVersion);
        db.UpgradePaths.AddRange(
            CreateUpgradePath(repositoryId, shared.RepoVersion, previousVersion: null, isActive: true),
            CreateUpgradePath(
                repositoryId,
                superseded.RepoVersion,
                hasSharedAncestor ? shared.RepoVersion : null,
                isActive: false),
            CreateUpgradePath(repositoryId, replacement.RepoVersion, shared.RepoVersion, isActive: true));
        await db.SaveChangesAsync();

        db.InstallationStates.Add(new XivInstallationState
        {
            RepositoryId = repositoryId,
            LastAppliedPatchId = superseded.Id,
            InstalledVersion = superseded.RepoVersion.VersionString,
            Status = InstallationStatus.NeedsRebuild
        });
        await db.SaveChangesAsync();

        if (createReplacementFile)
        {
            await CreatePatchFileAsync(paths.PatchRoot, replacement);
        }

        var regionRoot = new DirectoryInfo(Path.Combine(paths.InstallationRoot, "tc"));
        regionRoot.Create();
        Repository.Ffxiv.SetVer(regionRoot, superseded.RepoVersion.VersionString);
        return new SupersededPatchSet(superseded, replacement);
    }

    private static XivPatch CreatePatch(int repositoryId, string versionString, bool isActive)
    {
        var version = new XivRepoVersion
        {
            RepositoryId = repositoryId,
            VersionString = versionString
        };
        var patch = new XivPatch
        {
            RepoVersion = version,
            RemoteOriginPath =
                $"https://mydownloadakamai.ffxiv.com.tw/ffxiv/260101/game/{versionString}.patch",
            Size = 16,
            IsActive = isActive,
            FirstOffered = DateTime.UtcNow,
            LastOffered = DateTime.UtcNow
        };
        version.Patches.Add(patch);
        return patch;
    }

    private static XivUpgradePath CreateUpgradePath(
        int repositoryId,
        XivRepoVersion version,
        XivRepoVersion? previousVersion,
        bool isActive) =>
        new()
        {
            RepositoryId = repositoryId,
            RepoVersion = version,
            PreviousRepoVersion = previousVersion,
            IsActive = isActive,
            FirstOffered = DateTime.UtcNow,
            LastOffered = DateTime.UtcNow
        };

    private static async Task CreatePatchFileAsync(string patchRoot, XivPatch patch)
    {
        var patchPath = Path.Combine(patchRoot, patch.LocalStoragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
        await File.WriteAllBytesAsync(
            patchPath,
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    }

    private sealed class RecordingPatchApplicationService : IPatchApplicationService
    {
        public List<string> AppliedPatches { get; } = [];

        public Exception? Failure { get; init; }

        public Task ApplyAsync(
            FileInfo patchFile,
            DirectoryInfo targetDirectory,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }

            targetDirectory.Create();
            AppliedPatches.Add(patchFile.FullName);
            return Task.CompletedTask;
        }
    }

    private sealed record SupersededPatchSet(XivPatch Superseded, XivPatch Replacement);

    private sealed class TestPaths : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "thaliak-install-tests",
            Guid.NewGuid().ToString("N"));

        public TestPaths()
        {
            Directory.CreateDirectory(_root);
        }

        public string DatabasePath => Path.Combine(_root, "thaliak.db");

        public string PatchRoot => Path.Combine(_root, "patches");

        public string InstallationRoot => Path.Combine(_root, "installations");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
