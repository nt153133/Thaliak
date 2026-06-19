using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Thaliak.Common.Database;
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
}
