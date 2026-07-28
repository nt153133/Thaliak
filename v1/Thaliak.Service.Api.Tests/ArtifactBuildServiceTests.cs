using System.Text;
using FFXIVDownloader.Lut;
using FFXIVDownloader.Thaliak;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Services;
using Xunit;
using static FFXIVDownloader.ZiPatch.Config.ZiPatchConfig;

namespace Thaliak.Service.Api.Tests;

public sealed class ArtifactBuildServiceTests
{
    private const string RepositorySlug = "c38effbc";
    private const string TargetFile = "sqpack/ffxiv/000000.win32.dat0";

    [Fact]
    public async Task BuildAllAsync_WithValidPrefixAndCorruptSuffix_ResumesFromPrefix()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"thaliak-artifact-build-{Guid.NewGuid():N}");
        var artifactRoot = Path.Combine(testRoot, "artifacts");
        var patchRoot = Path.Combine(testRoot, "patches");
        var databasePath = Path.Combine(testRoot, "thaliak.db");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(patchRoot);

        try {
            var dbOptions = new DbContextOptionsBuilder<ThaliakContext>()
                .UseSqlite($"Data Source={databasePath}")
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new ThaliakContext(dbOptions);
            await db.Database.EnsureCreatedAsync();

            var versions = await SeedPatchChainAsync(db, patchRoot);
            var options = Options.Create(new ArtifactOptions
            {
                Root = artifactRoot,
                PatchRoot = patchRoot,
                Compression = nameof(CompressType.None),
                BasePatchUrl = "https://api.example.test/patches"
            });
            var pathService = new ArtifactPathService(options);
            var luts = versions
                .Select((version, index) => CreateLut(
                    version.PatchVersion,
                    blockOffset: index * 32,
                    patchOffset: 1_024 + (index * 1_024)))
                .ToArray();

            for (var i = 0; i < versions.Count; i++) {
                WriteLut(pathService, versions[i].PatchVersion, luts[i]);
            }

            var prefix = ClutAccumulator.Create(
                RepositorySlug,
                PlatformId.Win32,
                CompressType.None,
                "https://old.example.test/patches");
            prefix.Apply(versions[0].GameVersion, luts[0]);
            prefix.Apply(versions[1].GameVersion, luts[1]);
            WriteClut(pathService, versions[1].GameVersion, prefix);

            var corruptSuffixPath = pathService.GetAbsolutePath(
                "clut",
                RepositorySlug,
                versions[2].GameVersion.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(corruptSuffixPath)!);
            await File.WriteAllTextAsync(corruptSuffixPath, "not a clut");

            var readService = new ArtifactReadService(db, pathService, options);
            using var httpClient = new HttpClient();
            var webhookService = new ArtifactWebhookService(
                httpClient,
                readService,
                options,
                NullLogger<ArtifactWebhookService>.Instance);
            var buildService = new ArtifactBuildService(
                db,
                pathService,
                webhookService,
                options,
                NullLogger<ArtifactBuildService>.Instance);

            var repositoryId = await db.Repositories
                .Where(repository => repository.Slug == RepositorySlug)
                .Select(repository => repository.Id)
                .SingleAsync();
            var resolvedChain = await new PatchChainResolver(db).ResolveAsync(
                repositoryId,
                toVersion: versions[^1].GameVersion.ToString());
            Assert.NotNull(resolvedChain);
            Assert.Equal(versions.Count, resolvedChain.Count);
            Assert.All(resolvedChain, patch => Assert.True(File.Exists(Path.Combine(
                patchRoot,
                patch.LocalStoragePath.Replace('/', Path.DirectorySeparatorChar)))));

            await buildService.BuildAllAsync(CancellationToken.None);

            var expected = ClutAccumulator.Create(
                RepositorySlug,
                PlatformId.Win32,
                CompressType.None,
                options.Value.BasePatchUrl);
            for (var i = 0; i < versions.Count; i++) {
                expected.Apply(versions[i].GameVersion, luts[i]);
            }

            Assert.Equal(Serialize(expected), await File.ReadAllBytesAsync(corruptSuffixPath));
            Assert.True(await db.Artifacts.AnyAsync(artifact =>
                artifact.Kind == "clut"
                && artifact.RepositorySlug == RepositorySlug
                && artifact.VersionString == versions[2].GameVersion.ToString()
                && artifact.Status == "ready"));

            await db.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }
        finally {
            if (Directory.Exists(testRoot)) {
                try {
                    Directory.Delete(testRoot, recursive: true);
                }
                catch (IOException) {
                }
            }
        }
    }

    private static async Task<IReadOnlyList<TestVersion>> SeedPatchChainAsync(
        ThaliakContext db,
        string patchRoot)
    {
        var repository = await db.Repositories.SingleAsync(repository => repository.Slug == RepositorySlug);
        var versions = new[]
        {
            new TestVersion("2090.01.01.0000.0000", "D2090.01.01.0000.0000"),
            new TestVersion("2090.01.02.0000.0000", "D2090.01.02.0000.0000"),
            new TestVersion("2090.01.03.0000.0000", "D2090.01.03.0000.0000")
        };
        var entities = versions
            .Select(version => new XivRepoVersion
            {
                RepositoryId = repository.Id,
                VersionString = version.GameVersion.ToString()
            })
            .ToArray();
        db.RepoVersions.AddRange(entities);
        await db.SaveChangesAsync();

        for (var i = 0; i < versions.Length; i++) {
            var patch = new XivPatch
            {
                RepoVersionId = entities[i].Id,
                RemoteOriginPath = $"https://patch.example/game/{RepositorySlug}/{versions[i].PatchVersion}.patch",
                Size = 128,
                IsActive = true
            };
            db.Patches.Add(patch);
            db.UpgradePaths.Add(new XivUpgradePath
            {
                RepositoryId = repository.Id,
                RepoVersionId = entities[i].Id,
                PreviousRepoVersionId = i == 0 ? null : entities[i - 1].Id,
                IsActive = true
            });

            var patchPath = Path.Combine(
                patchRoot,
                patch.LocalStoragePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
            await File.WriteAllBytesAsync(patchPath, []);
        }

        await db.SaveChangesAsync();
        return versions;
    }

    private static LutFile CreateLut(PatchVersion patchVersion, long blockOffset, long patchOffset)
    {
        var lut = new LutFile
        {
            Header = new LutHeader
            {
                Repository = RepositorySlug,
                Version = patchVersion,
                Compression = CompressType.None
            }
        };
        lut.Chunks.Add(CreateAddDataChunk(blockOffset, patchOffset));
        return lut;
    }

    private static LutChunk CreateAddDataChunk(long blockOffset, long patchOffset)
    {
        using var dataStream = new MemoryStream();
        using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8, leaveOpen: true)) {
            dataWriter.Write(blockOffset);
            dataWriter.Write(32L);
            dataWriter.Write(0L);
            dataWriter.Write(patchOffset);
        }

        using var chunkStream = new MemoryStream();
        using (var writer = new BinaryWriter(chunkStream, Encoding.UTF8, leaveOpen: true)) {
            writer.Write((byte)ChunkType.SqpkAddData);
            writer.Write(1);
            writer.Write(0);
            writer.Write(checked((int)dataStream.Length));
            writer.Write(dataStream.GetBuffer().AsSpan(0, checked((int)dataStream.Length)));
        }

        chunkStream.Position = 0;
        using var reader = new BinaryReader(chunkStream);
        return new LutChunk(reader, [TargetFile]);
    }

    private static void WriteLut(ArtifactPathService pathService, PatchVersion patchVersion, LutFile lut)
    {
        var path = pathService.GetAbsolutePath("lut", RepositorySlug, patchVersion.ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        lut.Write(writer);
    }

    private static void WriteClut(
        ArtifactPathService pathService,
        GameVersion gameVersion,
        ClutAccumulator accumulator)
    {
        var path = pathService.GetAbsolutePath("clut", RepositorySlug, gameVersion.ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        accumulator.Write(stream);
    }

    private static byte[] Serialize(ClutAccumulator accumulator)
    {
        using var stream = new MemoryStream();
        accumulator.Write(stream);
        return stream.ToArray();
    }

    private sealed record TestVersion(GameVersion GameVersion, PatchVersion PatchVersion)
    {
        public TestVersion(string gameVersion, string patchVersion)
            : this(new GameVersion(gameVersion), new PatchVersion(patchVersion))
        {
        }
    }
}
