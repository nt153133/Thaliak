using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Polling;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Database;

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task Migrate_CreatesFreshSqliteDatabaseWithSeedData()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        Assert.Equal(4, await db.Services.CountAsync());
        Assert.True(await db.Repositories.AnyAsync(r =>
            r.Name == "traditional_chinese/win32/release/ex5" && r.ServiceId == 4));
        Assert.True(await db.ExpansionRepositoryMappings.AnyAsync(m =>
            m.GameRepositoryId == 20 && m.ExpansionId == 5 && m.ExpansionRepositoryId == 25));
        Assert.Empty(await db.InstallationStates.ToListAsync());
        Assert.Empty(await db.ExpansionSweepAttempts.ToListAsync());
    }

    [Fact]
    public async Task Migrate_ExistingAccount_AssignsRoutinePurpose()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260708164036_AddArtifactTracking");
        await db.Database.ExecuteSqlRawAsync(
            "insert into accounts (username, password) values ('trial', 'secret')");

        await migrator.MigrateAsync();

        var account = await db.Accounts.SingleAsync();
        Assert.Equal(XivAccountPurpose.Routine, account.Purpose);
    }

    [Fact]
    public async Task Accounts_WhenPurposeIsDuplicated_RejectsSecondAccount()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.Accounts.AddRange(
            new XivAccount
            {
                Purpose = XivAccountPurpose.Routine,
                Username = "first",
                Password = "secret",
                ApplicableRepositories = []
            },
            new XivAccount
            {
                Purpose = XivAccountPurpose.Routine,
                Username = "second",
                Password = "secret",
                ApplicableRepositories = []
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReconcileAsync_WhenUpgradePathAlreadyExists_UpdatesExistingRows()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var service = new PatchReconciliationService(db, CreatePatchAlertQueueService(db));
        var repo = await db.Repositories.SingleAsync(r => r.Id == 20);
        PatchListEntry[] remotePatches =
        [
            CreateTcPatch("2026.05.15.0000.0000"),
            CreateTcPatch("2026.05.16.0000.0000")
        ];

        await service.ReconcileAsync(repo, remotePatches, PatchDiscoveryType.Offered);
        await service.ReconcileAsync(repo, remotePatches, PatchDiscoveryType.Offered);

        var upgradePaths = await db.UpgradePaths
            .Where(p => p.RepositoryId == 20)
            .OrderBy(p => p.RepoVersion.VersionString)
            .ToListAsync();

        Assert.Equal(2, upgradePaths.Count);
        Assert.Single(upgradePaths.Where(p => p.PreviousRepoVersionId is null));
        Assert.Single(upgradePaths.Where(p => p.PreviousRepoVersionId is not null));
        Assert.All(upgradePaths, p => Assert.True(p.IsActive));

        var chain = await new PatchChainResolver(db).ResolveAsync(20);
        Assert.NotNull(chain);
        Assert.Equal(
            ["2026.05.15.0000.0000", "2026.05.16.0000.0000"],
            chain.Select(patch => patch.RepoVersion.VersionString));
    }

    [Fact]
    public async Task ReconcileAsync_WhenVersionIsRepeated_DoesNotCreateSelfLoop()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var service = new PatchReconciliationService(db, CreatePatchAlertQueueService(db));
        var repo = await db.Repositories.SingleAsync(r => r.Id == 20);
        PatchListEntry[] remotePatches =
        [
            CreateTcPatch("2026.01.01.0000.0000"),
            CreateTcPatch("2026.01.01.0000.0000"),
            CreateTcPatch("2026.02.01.0000.0000")
        ];

        await service.ReconcileAsync(repo, remotePatches, PatchDiscoveryType.Offered);

        var paths = await db.UpgradePaths
            .Where(path => path.RepositoryId == 20)
            .ToListAsync();
        Assert.DoesNotContain(paths, path => path.RepoVersionId == path.PreviousRepoVersionId);
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public async Task ReconcileAsync_PreservesHistoricalSectionOrder()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var service = new PatchReconciliationService(db, CreatePatchAlertQueueService(db));
        var repo = await db.Repositories.SingleAsync(r => r.Id == 20);
        PatchListEntry[] remotePatches =
        [
            CreateTcPatch("H2024.05.31.0000.0000z"),
            CreateTcPatch("H2024.05.31.0000.0000aa"),
            CreateTcPatch("2024.05.31.0000.0000")
        ];

        await service.ReconcileAsync(repo, remotePatches, PatchDiscoveryType.Offered);

        var chain = await new PatchChainResolver(db).ResolveAsync(20);
        Assert.NotNull(chain);
        Assert.Equal(
            [
                "H2024.05.31.0000.0000z",
                "H2024.05.31.0000.0000aa",
                "2024.05.31.0000.0000"
            ],
            chain.Select(patch => patch.RepoVersion.VersionString));
    }

    [Fact]
    public async Task ReconcileAsync_WhenScrapedPatchBecomesOffered_ReturnsItOnlyOnce()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var service = new PatchReconciliationService(db, CreatePatchAlertQueueService(db));
        var repo = await db.Repositories.SingleAsync(repository => repository.Id == 20);
        PatchListEntry[] patches = [CreateTcPatch("2026.06.23.0000.0000")];

        var scraped = await service.ReconcileAsync(repo, patches, PatchDiscoveryType.Scraped);
        var offered = await service.ReconcileAsync(repo, patches, PatchDiscoveryType.Offered);
        var repeated = await service.ReconcileAsync(repo, patches, PatchDiscoveryType.Offered);

        Assert.Empty(scraped.NewlyOfferedPatches);
        Assert.Single(offered.NewlyOfferedPatches);
        Assert.Empty(repeated.NewlyOfferedPatches);
    }

    [Fact]
    public async Task ReconcileAsync_AfterFullExpansionSweep_LimitedRoutinePollPreservesUnrepresentedExpansions()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var service = new PatchReconciliationService(db, CreatePatchAlertQueueService(db));
        var repo = await db.Repositories.SingleAsync(repository => repository.Id == 2);
        var fullSweepPatches = Enumerable.Range(0, 6)
            .Select(expansion => CreateGlobalPatch(expansion, $"2026.07.{10 + expansion:00}.0000.0000"))
            .ToArray();

        await service.ReconcileAsync(repo, fullSweepPatches, PatchDiscoveryType.Offered);
        await service.ReconcileAsync(
            repo,
            [
                .. Enumerable.Range(0, 3)
                    .Select(expansion => CreateGlobalPatch(expansion, $"2026.07.{10 + expansion:00}.0000.0000")),
                CreateGlobalPatch(3, "2026.07.20.0000.0000")
            ],
            PatchDiscoveryType.Offered);

        var patchStates = await db.Patches
            .Include(patch => patch.RepoVersion)
            .Where(patch => new[] { 5, 6, 17 }.Contains(patch.RepoVersion.RepositoryId))
            .ToDictionaryAsync(
                patch => (patch.RepoVersion.RepositoryId, patch.RepoVersion.VersionString),
                patch => patch.IsActive);

        Assert.False(patchStates[(5, "2026.07.13.0000.0000")]);
        Assert.True(patchStates[(5, "2026.07.20.0000.0000")]);
        Assert.True(patchStates[(6, "2026.07.14.0000.0000")]);
        Assert.True(patchStates[(17, "2026.07.15.0000.0000")]);
    }

    [Fact]
    public async Task PatchChainResolver_IgnoresSelfLoopAndOmitsStubPayload()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var first = CreateVersion(20, "2026.01.01.0000.0000", 1_024);
        var stub = CreateVersion(20, "2026.01.02.0000.0000", 12);
        var latest = CreateVersion(20, "2026.01.03.0000.0000", 2_048);
        db.RepoVersions.AddRange(first, stub, latest);
        db.UpgradePaths.AddRange(
            CreatePath(20, first),
            CreatePath(20, stub, first),
            CreatePath(20, stub, stub),
            CreatePath(20, latest, stub));
        await db.SaveChangesAsync();

        var chain = await new PatchChainResolver(db).ResolveAsync(20);

        Assert.NotNull(chain);
        Assert.Equal(
            ["2026.01.01.0000.0000", "2026.01.03.0000.0000"],
            chain.Select(patch => patch.RepoVersion.VersionString));
    }

    private static ThaliakContext CreateContext()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "thaliak-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var options = new DbContextOptionsBuilder<ThaliakContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ThaliakContext(options);
    }

    private static PatchAlertQueueService CreatePatchAlertQueueService(ThaliakContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new PatchAlertQueueService(db, configuration);
    }

    private static PatchListEntry CreateTcPatch(string version) =>
        new()
        {
            VersionId = version,
            HashType = "sha1",
            Url = $"https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex0/{version}.patch",
            HashBlockSize = 0,
            Hashes = [],
            Length = 1024
        };

    private static PatchListEntry CreateGlobalPatch(int expansion, string version) =>
        new()
        {
            VersionId = version,
            HashType = "sha1",
            Url = expansion == 0
                ? $"https://patch-dl.ffxiv.com/game/4e9a232b/D{version}.patch"
                : $"https://patch-dl.ffxiv.com/game/ex{expansion}/D{version}.patch",
            HashBlockSize = 0,
            Hashes = [],
            Length = 1024
        };

    private static XivRepoVersion CreateVersion(int repositoryId, string version, long patchSize)
    {
        var repoVersion = new XivRepoVersion
        {
            RepositoryId = repositoryId,
            VersionString = version
        };
        repoVersion.Patches.Add(new XivPatch
        {
            RepoVersion = repoVersion,
            RemoteOriginPath = $"https://example.test/game/{repositoryId}/{version}.patch",
            Size = patchSize,
            IsActive = true,
            FirstOffered = DateTime.UtcNow,
            LastOffered = DateTime.UtcNow
        });
        return repoVersion;
    }

    private static XivUpgradePath CreatePath(
        int repositoryId,
        XivRepoVersion version,
        XivRepoVersion? previous = null) =>
        new()
        {
            RepositoryId = repositoryId,
            RepoVersion = version,
            PreviousRepoVersion = previous,
            IsActive = true,
            FirstOffered = DateTime.UtcNow,
            LastOffered = DateTime.UtcNow
        };
}
