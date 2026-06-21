using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Xunit;

namespace Thaliak.Service.Api.Tests;

public sealed class FfxivDownloaderCompatibilityTests(FfxivDownloaderCompatibilityFixture fixture)
    : IClassFixture<FfxivDownloaderCompatibilityFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetRepository_ReturnsRepositoryV2WireShape()
    {
        using var response = await fixture.Client.GetAsync($"/api/v2beta/repositories/{TestData.RepositorySlug}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("service_id", out var serviceId));
        Assert.True(root.TryGetProperty("latest_patch", out var latestPatch));
        Assert.True(latestPatch.TryGetProperty("version_string", out var versionString));
        Assert.True(latestPatch.TryGetProperty("first_offered", out _));
        Assert.True(latestPatch.TryGetProperty("last_offered", out _));
        Assert.False(root.TryGetProperty("serviceId", out _));
        Assert.False(root.TryGetProperty("latestPatch", out _));

        var repository = JsonSerializer.Deserialize<RepositoryV2Contract>(json, JsonOptions);
        Assert.NotNull(repository);
        Assert.Equal("jp", serviceId.GetString());
        Assert.Equal(TestData.RepositorySlug, repository.Slug);
        Assert.Equal(TestData.LatestVersion, versionString.GetString());
        Assert.Equal(TestData.LatestVersion, repository.LatestPatch!.VersionString);
    }

    [Fact]
    public async Task GraphQlMetadata_DeserializesThroughRepositoryResponseShape()
    {
        var request = new
        {
            query = """
            query($repoId: String!) {
                repository(slug: $repoId) {
                    name
                    description
                    latestVersion {
                        versionString
                    }
                }
            }
            """,
            variables = new Dictionary<string, string>
            {
                ["repoId"] = TestData.RepositorySlug
            }
        };

        using var response = await fixture.Client.PostAsJsonAsync("/graphql/2022-08-14", request);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<GraphQlEnvelope<RepositoryResponseContract>>(JsonOptions);
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        Assert.Equal("ffxivneo/win32/release/game", envelope.Data.Repository.Name);
        Assert.Equal(TestData.LatestVersion, envelope.Data.Repository.LatestVersion!.VersionString);
    }

    [Fact]
    public async Task GraphQlVersions_ReturnEnoughDataForCurrentPatchChainClient()
    {
        var request = new
        {
            query = """
            query($repoId: String!) {
                repository(slug: $repoId) {
                    versions {
                        versionString
                        isActive
                        prerequisiteVersions {
                            versionString
                        }
                        patches {
                            url
                            size
                        }
                    }
                }
            }
            """,
            variables = new Dictionary<string, string>
            {
                ["repoId"] = TestData.RepositorySlug
            }
        };

        using var response = await fixture.Client.PostAsJsonAsync("/graphql/2022-08-14", request);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<GraphQlEnvelope<RepositoryResponseContract>>(JsonOptions);
        var versions = envelope!.Data!.Repository.Versions!;
        var latest = versions.Single(version => version.VersionString == TestData.LatestVersion);

        Assert.True(latest.IsActive);
        Assert.Contains(latest.PrerequisiteVersions, version => version.VersionString == TestData.PreviousVersion);
        Assert.Collection(
            latest.Patches,
            patch =>
            {
                Assert.Equal(TestData.LatestPatchUrl, patch.Url);
                Assert.Equal(TestData.LatestPatchSize, patch.Size);
            });

        var chain = BuildClientStylePatchChain(versions, TestData.LatestVersion);
        Assert.Collection(
            chain,
            item => Assert.Equal(TestData.PreviousVersion, item.VersionString),
            item => Assert.Equal(TestData.LatestVersion, item.VersionString));
    }

    [Fact]
    public async Task UnknownRepositoryOrVersion_ReturnsNotFound()
    {
        using var unknownRepositoryResponse = await fixture.Client.GetAsync("/api/v2beta/repositories/not-real");
        Assert.Equal(HttpStatusCode.NotFound, unknownRepositoryResponse.StatusCode);

        using var unknownVersionResponse = await fixture.Client.GetAsync(
            $"/api/v2beta/repositories/{TestData.RepositorySlug}/patches/2099.01.01.0000.0000");
        Assert.Equal(HttpStatusCode.NotFound, unknownVersionResponse.StatusCode);
    }

    private static IReadOnlyList<AnnotatedVersionContract> BuildClientStylePatchChain(
        IReadOnlyList<AnnotatedVersionContract> versionList,
        string targetVersion)
    {
        var versions = versionList.ToDictionary(version => version.VersionString, StringComparer.Ordinal);
        var result = new List<AnnotatedVersionContract>();
        var nextVersion = versions.GetValueOrDefault(targetVersion);

        while (nextVersion is not null) {
            Assert.Single(nextVersion.Patches);
            result.Add(nextVersion);

            nextVersion = nextVersion.PrerequisiteVersions
                .Where(prerequisite => result.All(version => version.VersionString != prerequisite.VersionString))
                .Select(prerequisite => versions.GetValueOrDefault(prerequisite.VersionString))
                .Where(prerequisite => prerequisite is not null)
                .Where(prerequisite => !nextVersion.IsActive || prerequisite!.IsActive)
                .OrderByDescending(prerequisite => prerequisite!.VersionString, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        result.Reverse();
        return result;
    }

    private sealed record RepositoryV2Contract
    {
        [JsonPropertyName("service_id")]
        public required string ServiceId { get; init; }

        [JsonPropertyName("slug")]
        public required string Slug { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("latest_patch")]
        public LatestPatchContract? LatestPatch { get; init; }
    }

    private sealed record LatestPatchContract
    {
        [JsonPropertyName("version_string")]
        public required string VersionString { get; init; }

        [JsonPropertyName("first_offered")]
        public required DateTime FirstOffered { get; init; }

        [JsonPropertyName("last_offered")]
        public required DateTime LastOffered { get; init; }
    }

    private sealed record GraphQlEnvelope<T>(T Data);

    private sealed record RepositoryResponseContract(RepositoryContract Repository);

    private sealed record RepositoryContract
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public VersionContract? LatestVersion { get; init; }
        public List<AnnotatedVersionContract>? Versions { get; init; }
    }

    private record VersionContract
    {
        public required string VersionString { get; init; }
    }

    private sealed record AnnotatedVersionContract : VersionContract
    {
        public required bool IsActive { get; init; }
        public required List<VersionContract> PrerequisiteVersions { get; init; }
        public required List<PatchContract> Patches { get; init; }
    }

    private sealed record PatchContract
    {
        public required string Url { get; init; }
        public required long Size { get; init; }
    }
}

public sealed class FfxivDownloaderCompatibilityFixture : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"thaliak-api-tests-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await SeedDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:sqlite"] = $"Data Source={_databasePath}"
                });
            });
        });

        Client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        Client.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath)) {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    private async Task SeedDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<ThaliakContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new ThaliakContext(options);
        await db.Database.EnsureCreatedAsync();

        var repository = await db.Repositories.SingleAsync(repository => repository.Slug == TestData.RepositorySlug);

        var previousVersion = new XivRepoVersion
        {
            RepositoryId = repository.Id,
            VersionString = TestData.PreviousVersion
        };
        var latestVersion = new XivRepoVersion
        {
            RepositoryId = repository.Id,
            VersionString = TestData.LatestVersion
        };

        db.RepoVersions.AddRange(previousVersion, latestVersion);
        await db.SaveChangesAsync();

        db.Patches.AddRange(
            new XivPatch
            {
                RepoVersionId = previousVersion.Id,
                RemoteOriginPath = TestData.PreviousPatchUrl,
                FirstSeen = TestData.PreviousOfferedAt.AddMinutes(-5),
                LastSeen = TestData.PreviousOfferedAt,
                FirstOffered = TestData.PreviousOfferedAt,
                LastOffered = TestData.PreviousOfferedAt,
                Size = TestData.PreviousPatchSize,
                IsActive = true,
                HashType = "sha1",
                HashBlockSize = 4096,
                Hashes = ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]
            },
            new XivPatch
            {
                RepoVersionId = latestVersion.Id,
                RemoteOriginPath = TestData.LatestPatchUrl,
                FirstSeen = TestData.LatestOfferedAt.AddMinutes(-5),
                LastSeen = TestData.LatestOfferedAt,
                FirstOffered = TestData.LatestOfferedAt,
                LastOffered = TestData.LatestOfferedAt,
                Size = TestData.LatestPatchSize,
                IsActive = true,
                HashType = "sha1",
                HashBlockSize = 4096,
                Hashes = ["bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]
            });

        db.UpgradePaths.Add(new XivUpgradePath
        {
            RepositoryId = repository.Id,
            RepoVersionId = latestVersion.Id,
            PreviousRepoVersionId = previousVersion.Id,
            FirstOffered = TestData.LatestOfferedAt,
            LastOffered = TestData.LatestOfferedAt,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }
}

internal static class TestData
{
    public const string RepositorySlug = "4e9a232b";
    public const string PreviousVersion = "2026.06.10.0000.0000";
    public const string LatestVersion = "2026.06.11.0000.0000";
    public const string PreviousPatchUrl = "http://patch-dl.ffxiv.com/game/4e9a232b/D2026.06.10.0000.0000.patch";
    public const string LatestPatchUrl = "http://patch-dl.ffxiv.com/game/4e9a232b/D2026.06.11.0000.0000.patch";
    public const long PreviousPatchSize = 1024;
    public const long LatestPatchSize = 2048;

    public static readonly DateTime PreviousOfferedAt = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime LatestOfferedAt = new(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);
}
