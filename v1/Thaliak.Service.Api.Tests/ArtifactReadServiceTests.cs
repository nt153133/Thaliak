using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Models;
using Thaliak.Service.Api.Services;
using Xunit;

namespace Thaliak.Service.Api.Tests;

public sealed class ArtifactReadServiceTests
{
    private const string CurrentVersion = "2090.01.02.0000.0000";
    private const string StaleVersion = "2090.01.01.0000.0000";

    [Fact]
    public async Task GetRegionAsync_WhenEveryClutMatchesLatestActivePatch_ReturnsReady()
    {
        var region = await ReadGlobalRegionAsync();

        Assert.True(region.IsReady);
        Assert.NotNull(region.ReadyAtUtc);
    }

    [Fact]
    public async Task GetRegionAsync_WhenOneClutPredatesLatestActivePatch_ReturnsNotReady()
    {
        var region = await ReadGlobalRegionAsync(staleRepositorySlug: "6cfeab11");

        Assert.False(region.IsReady);
        Assert.Null(region.ReadyAtUtc);
        Assert.Equal(
            StaleVersion,
            region.Repositories.Single(repository => repository.Slug == "6cfeab11").LatestClut?.VersionString);
    }

    private static async Task<ArtifactRegionDto> ReadGlobalRegionAsync(string? staleRepositorySlug = null)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<ThaliakContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ThaliakContext(dbOptions);
        await db.Database.EnsureCreatedAsync();

        var targets = ArtifactTargetCatalog.ForRegion("global");
        var targetSlugs = targets.Select(target => target.RepositorySlug).ToArray();
        var repositories = await db.Repositories
            .Where(repository => targetSlugs.Contains(repository.Slug))
            .ToDictionaryAsync(repository => repository.Slug, StringComparer.OrdinalIgnoreCase);
        var readyAtUtc = new DateTime(2090, 1, 2, 12, 0, 0, DateTimeKind.Utc);

        foreach (var target in targets) {
            var artifactVersion = string.Equals(
                target.RepositorySlug,
                staleRepositorySlug,
                StringComparison.OrdinalIgnoreCase)
                ? StaleVersion
                : CurrentVersion;
            var repoVersion = new XivRepoVersion
            {
                RepositoryId = repositories[target.RepositorySlug].Id,
                VersionString = CurrentVersion
            };
            db.Patches.Add(new XivPatch
            {
                RepoVersion = repoVersion,
                RemoteOriginPath =
                    $"https://patch-dl.ffxiv.com/game/{target.RepositorySlug}/D{CurrentVersion}.patch",
                IsActive = true,
                FirstSeen = readyAtUtc,
                LastSeen = readyAtUtc,
                FirstOffered = readyAtUtc,
                LastOffered = readyAtUtc,
                Size = 1024
            });
            db.Artifacts.Add(new XivArtifact
            {
                Kind = "clut",
                Region = target.Region,
                RepositorySlug = target.RepositorySlug,
                VersionString = artifactVersion,
                RelativePath = $"cluts/{target.RepositorySlug}/{artifactVersion}.clut",
                Size = 1024,
                Sha256 = new string('a', 64),
                Status = "ready",
                CreatedAtUtc = readyAtUtc,
                UpdatedAtUtc = readyAtUtc,
                ReadyAtUtc = readyAtUtc
            });
        }

        await db.SaveChangesAsync();

        var options = Options.Create(new ArtifactOptions
        {
            Root = Path.GetTempPath(),
            PublicBaseUrl = "https://api.example.test"
        });
        var service = new ArtifactReadService(db, new ArtifactPathService(options), options);

        return Assert.IsType<ArtifactRegionDto>(
            await service.GetRegionAsync("global", CancellationToken.None));
    }
}
