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

    [Fact]
    public async Task GetServices_ReturnsMaintainedRegionsAndExpansionSlugs()
    {
        using var response = await fixture.Client.GetAsync("/api/v2beta/services");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var services = document.RootElement.GetProperty("services").EnumerateArray().ToArray();

        var global = services.Single(service => service.GetProperty("region").GetString() == "global");
        var globalSlugs = global.GetProperty("repositories")
            .EnumerateArray()
            .Select(repository => repository.GetProperty("slug").GetString())
            .ToArray();

        Assert.Contains("4e9a232b", globalSlugs);
        Assert.Contains("6cfeab11", globalSlugs);

        var tc = services.Single(service => service.GetProperty("region").GetString() == "tc");
        var tcGame = tc.GetProperty("repositories")
            .EnumerateArray()
            .Single(repository => repository.GetProperty("expansion").GetString() == "game");
        Assert.Equal("961a4536", tcGame.GetProperty("slug").GetString());
        Assert.Contains(tcGame.GetProperty("aliases").EnumerateArray(), alias => alias.GetString() == "TC");
    }

    [Fact]
    public async Task GetStatus_ReturnsDatabaseAndArtifactState()
    {
        using var response = await fixture.Client.GetAsync("/api/v2beta/status");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("ok", root.GetProperty("database").GetProperty("status").GetString());
        Assert.False(root.GetProperty("artifacts").GetProperty("generator_enabled").GetBoolean());
    }

    [Fact]
    public async Task ArtifactRegions_ReturnMetadataForMaintainedRegions()
    {
        using var response = await fixture.Client.GetAsync("/api/v2beta/artifacts/regions");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var regions = document.RootElement.GetProperty("regions").EnumerateArray().ToArray();

        Assert.Contains(regions, region => region.GetProperty("region").GetString() == "global");
        var tc = regions.Single(region => region.GetProperty("region").GetString() == "tc");
        Assert.False(tc.GetProperty("is_ready").GetBoolean());
        Assert.Contains(
            tc.GetProperty("repositories").EnumerateArray(),
            repository => repository.GetProperty("slug").GetString() == "961a4536"
                && repository.GetProperty("latest_clut").ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task ArtifactFileEndpoints_ReturnNotFoundForMissingArtifact()
    {
        using var response = await fixture.Client.GetAsync("/cluts/4e9a232b/2099.01.01.0000.0000.clut");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClutEndpoint_ServesTcAliasWithRangeAndCacheHeaders()
    {
        using var response = await fixture.Client.GetAsync($"/cluts/TC/{TestData.LatestVersion}.clut");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal(TestData.ArtifactSha256, response.Headers.ETag?.Tag.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(TestData.ArtifactBytes, bytes);
    }

    [Fact]
    public async Task PatchMetadata_AdvertisesArchiveBeforeOrigin()
    {
        using var response = await fixture.Client.GetAsync(
            $"/api/v2beta/repositories/{TestData.RepositorySlug}/patches/D{TestData.LatestVersion}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();

        Assert.Equal(TestData.LatestVersion, root.GetProperty("version_string").GetString());
        Assert.Equal("archive", sources[0].GetProperty("type").GetString());
        Assert.Equal(
            $"https://api.example.test/patches/{TestData.RepositorySlug}/D{TestData.LatestVersion}.patch",
            sources[0].GetProperty("url").GetString());
        Assert.True(sources[0].GetProperty("supports_range_requests").GetBoolean());
        Assert.True(sources[0].GetProperty("supports_multipart_ranges").GetBoolean());
        Assert.Equal(64, sources[0].GetProperty("max_ranges_per_request").GetInt32());
        Assert.Equal("origin", sources[1].GetProperty("type").GetString());
        Assert.Equal(TestData.LatestPatchUrl, sources[1].GetProperty("url").GetString());
        Assert.False(sources[1].GetProperty("supports_multipart_ranges").GetBoolean());
    }

    [Fact]
    public async Task PatchArchive_ServesSingleAndMultipartRanges()
    {
        var path = $"/patches/{TestData.RepositorySlug}/D{TestData.LatestVersion}.patch";
        using var singleRequest = new HttpRequestMessage(HttpMethod.Get, path);
        singleRequest.Headers.Range = new(10, 19);
        using var singleResponse = await fixture.Client.SendAsync(singleRequest);

        Assert.Equal(HttpStatusCode.PartialContent, singleResponse.StatusCode);
        Assert.Equal("bytes", singleResponse.Headers.AcceptRanges.Single());
        Assert.Equal(
            $"bytes 10-19/{TestData.LatestPatchSize}",
            singleResponse.Content.Headers.ContentRange?.ToString());
        Assert.Equal(TestData.PatchBytes[10..20], await singleResponse.Content.ReadAsByteArrayAsync());

        using var multipartRequest = new HttpRequestMessage(HttpMethod.Get, path);
        multipartRequest.Headers.TryAddWithoutValidation("Range", "bytes=10-13,100-103");
        using var multipartResponse = await fixture.Client.SendAsync(multipartRequest);

        Assert.Equal(HttpStatusCode.PartialContent, multipartResponse.StatusCode);
        Assert.Equal("multipart/byteranges", multipartResponse.Content.Headers.ContentType?.MediaType);
        var body = await multipartResponse.Content.ReadAsByteArrayAsync();
        var bodyText = System.Text.Encoding.ASCII.GetString(body);
        Assert.Contains($"Content-Range: bytes 10-13/{TestData.LatestPatchSize}", bodyText);
        Assert.Contains($"Content-Range: bytes 100-103/{TestData.LatestPatchSize}", bodyText);
        Assert.Contains(System.Text.Encoding.ASCII.GetString(TestData.PatchBytes[10..14]), bodyText);
        Assert.Contains(System.Text.Encoding.ASCII.GetString(TestData.PatchBytes[100..104]), bodyText);
    }

    [Fact]
    public async Task PatchArchive_RejectsUnknownOrUnsatisfiableRanges()
    {
        using var unknownResponse = await fixture.Client.GetAsync(
            $"/patches/{TestData.RepositorySlug}/D2099.01.01.0000.0000.patch");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);

        using var missingArchiveResponse = await fixture.Client.GetAsync(
            $"/patches/{TestData.RepositorySlug}/D{TestData.PreviousVersion}.patch");
        Assert.Equal(HttpStatusCode.NotFound, missingArchiveResponse.StatusCode);

        using var metadataResponse = await fixture.Client.GetAsync(
            $"/api/v2beta/repositories/{TestData.RepositorySlug}/patches/D{TestData.PreviousVersion}");
        metadataResponse.EnsureSuccessStatusCode();
        using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
        var source = Assert.Single(metadata.RootElement.GetProperty("sources").EnumerateArray());
        Assert.Equal("origin", source.GetProperty("type").GetString());

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/patches/{TestData.RepositorySlug}/D{TestData.LatestVersion}.patch");
        request.Headers.Range = new(TestData.LatestPatchSize + 1, null);
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal(
            $"bytes */{TestData.LatestPatchSize}",
            response.Content.Headers.ContentRange?.ToString());
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

    private readonly string _artifactRoot =
        Path.Combine(Path.GetTempPath(), $"thaliak-api-artifacts-{Guid.NewGuid():N}");

    private readonly string _patchRoot =
        Path.Combine(Path.GetTempPath(), $"thaliak-api-patches-{Guid.NewGuid():N}");

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
                    ["ConnectionStrings:sqlite"] = $"Data Source={_databasePath}",
                    ["Artifacts:Enabled"] = "false",
                    ["Artifacts:Root"] = _artifactRoot,
                    ["Artifacts:PatchRoot"] = _patchRoot,
                    ["Artifacts:PublicBaseUrl"] = "https://api.example.test"
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

        if (Directory.Exists(_artifactRoot)) {
            Directory.Delete(_artifactRoot, recursive: true);
        }
        if (Directory.Exists(_patchRoot)) {
            Directory.Delete(_patchRoot, recursive: true);
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

        var previousPatch = new XivPatch
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
        };
        var latestPatch = new XivPatch
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
        };
        db.Patches.AddRange(previousPatch, latestPatch);

        var latestPatchPath = Path.Combine(
            _patchRoot,
            latestPatch.LocalStoragePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(latestPatchPath)!);
        await File.WriteAllBytesAsync(latestPatchPath, TestData.PatchBytes);

        db.UpgradePaths.Add(new XivUpgradePath
        {
            RepositoryId = repository.Id,
            RepoVersionId = latestVersion.Id,
            PreviousRepoVersionId = previousVersion.Id,
            FirstOffered = TestData.LatestOfferedAt,
            LastOffered = TestData.LatestOfferedAt,
            IsActive = true
        });

        var artifactRelativePath = Path.Combine("cluts", TestData.TcRepositorySlug, $"{TestData.LatestVersion}.clut")
            .Replace('\\', '/');
        var artifactPath = Path.Combine(_artifactRoot, artifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        await File.WriteAllBytesAsync(artifactPath, TestData.ArtifactBytes);

        db.Artifacts.Add(new XivArtifact
        {
            Kind = "clut",
            Region = "tc",
            RepositorySlug = TestData.TcRepositorySlug,
            VersionString = TestData.LatestVersion,
            RelativePath = artifactRelativePath,
            Size = TestData.ArtifactBytes.Length,
            Sha256 = TestData.ArtifactSha256,
            Status = "ready",
            CreatedAtUtc = TestData.LatestOfferedAt,
            UpdatedAtUtc = TestData.LatestOfferedAt,
            ReadyAtUtc = TestData.LatestOfferedAt
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
    public const string TcRepositorySlug = "961a4536";
    public const string ArtifactSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const long PreviousPatchSize = 1024;
    public const long LatestPatchSize = 2048;

    public static readonly DateTime PreviousOfferedAt = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime LatestOfferedAt = new(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);
    public static readonly byte[] ArtifactBytes = [0x43, 0x4c, 0x55, 0x54];
    public static readonly byte[] PatchBytes =
        Enumerable.Range(0, (int)LatestPatchSize)
            .Select(index => (byte)('A' + index % 26))
            .ToArray();
}
