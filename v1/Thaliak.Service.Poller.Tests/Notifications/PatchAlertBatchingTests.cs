using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Webhooks;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Notifications;

public sealed class PatchAlertBatchingTests
{
    [Fact]
    public async Task QueueEligiblePatchesAsync_SuppressesBootPatchesByDefault()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var patch = await AddPatchAsync(db, repositoryId: 1, "2026.06.12.0000.0000");
        var queue = new PatchAlertQueueService(db, CreateConfiguration());

        await queue.QueueEligiblePatchesAsync([patch], PatchDiscoveryType.Offered, DateTime.UtcNow);

        Assert.Null(patch.NotificationQueuedAtUtc);
    }

    [Fact]
    public async Task QueueEligiblePatchesAsync_QueuesOfferedGamePatchesByRegion()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var patch = await AddPatchAsync(db, repositoryId: 25, "2026.06.12.0000.0000");
        var detectedAt = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        var queue = new PatchAlertQueueService(db, CreateConfiguration());

        await queue.QueueEligiblePatchesAsync([patch], PatchDiscoveryType.Offered, detectedAt);

        Assert.Equal(detectedAt, patch.NotificationQueuedAtUtc);
        Assert.Equal("Offered", patch.NotificationDiscoveryType);
    }

    [Fact]
    public async Task QueueEligiblePatchesAsync_SkipsScrapedPatchesByDefault()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var patch = await AddPatchAsync(db, repositoryId: 25, "2026.06.12.0000.0000");
        var queue = new PatchAlertQueueService(db, CreateConfiguration());

        await queue.QueueEligiblePatchesAsync([patch], PatchDiscoveryType.Scraped, DateTime.UtcNow);

        Assert.Null(patch.NotificationQueuedAtUtc);
    }

    [Fact]
    public async Task QueueEligiblePatchesAsync_DoesNotRequeueSentPatches()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var patch = await AddPatchAsync(db, repositoryId: 25, "2026.06.12.0000.0000");
        patch.NotificationQueuedAtUtc = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        patch.NotificationSentAtUtc = new DateTime(2026, 6, 12, 18, 5, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        var queue = new PatchAlertQueueService(db, CreateConfiguration());

        await queue.QueueEligiblePatchesAsync([patch], PatchDiscoveryType.Offered,
            new DateTime(2026, 6, 12, 19, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc), patch.NotificationQueuedAtUtc);
    }

    [Fact]
    public async Task DispatchReadyBatchesAsync_SendsOneDiscordAndOneJsonBatchPerRegion()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var queuedAt = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        await AddPatchAsync(db, 24, "2026.06.12.0000.0000", queuedAt);
        await AddPatchAsync(db, 25, "2026.06.12.0000.0000", queuedAt.AddSeconds(30));
        var handler = new RecordingHandler();
        var discord = new RecordingDiscordSender();
        var dispatcher = CreateDispatcher(db, handler, discord);

        var dispatched = await dispatcher.DispatchReadyBatchesAsync(queuedAt.AddMinutes(4));

        Assert.Equal(1, dispatched);
        Assert.Single(discord.Batches);
        Assert.Equal("TC", discord.Batches[0].Region);
        Assert.Equal(2, discord.Batches[0].PatchCount);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("\"event\":\"patch.batch.discovered\"", request.Body);
        Assert.Contains("\"patchCount\":2", request.Body);
    }

    [Fact]
    public async Task DispatchReadyBatchesAsync_LaterPatchResetsQuietWindow()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var queuedAt = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        await AddPatchAsync(db, 24, "2026.06.12.0000.0000", queuedAt);
        await AddPatchAsync(db, 25, "2026.06.12.0000.0000", queuedAt.AddMinutes(2));
        var dispatcher = CreateDispatcher(db, new RecordingHandler(), new RecordingDiscordSender());

        Assert.Equal(0, await dispatcher.DispatchReadyBatchesAsync(queuedAt.AddMinutes(4)));
        Assert.Equal(1, await dispatcher.DispatchReadyBatchesAsync(queuedAt.AddMinutes(5).AddSeconds(1)));
    }

    [Fact]
    public async Task DispatchReadyBatchesAsync_UnsentQueuedPatchesSurviveFreshContext()
    {
        var databasePath = CreateDatabasePath();
        var queuedAt = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateContext(databasePath)) {
            await db.Database.MigrateAsync();
            await AddPatchAsync(db, 25, "2026.06.12.0000.0000", queuedAt);
        }

        await using (var db = CreateContext(databasePath)) {
            var handler = new RecordingHandler();
            var dispatched = await CreateDispatcher(db, handler, new RecordingDiscordSender())
                .DispatchReadyBatchesAsync(queuedAt.AddMinutes(4));

            Assert.Equal(1, dispatched);
            Assert.Single(handler.Requests);
        }
    }

    private static PatchAlertDispatchService CreateDispatcher(ThaliakContext db, RecordingHandler handler,
        RecordingDiscordSender discord)
    {
        var configuration = CreateConfiguration();
        var json = new JsonWebhookService(new HttpClient(handler), configuration);
        var notifications = new PatchAlertNotificationService(discord, json);
        return new PatchAlertDispatchService(db, configuration, notifications);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NotificationConfiguration.QuietWindowMinutesKey] = "3",
                ["Webhooks:JsonEndpoints:0:Name"] = "tc",
                ["Webhooks:JsonEndpoints:0:Url"] = "https://example.test/tc",
                ["Webhooks:JsonEndpoints:0:Regions:0"] = "TC"
            })
            .Build();
    }

    private static async Task<XivPatch> AddPatchAsync(ThaliakContext db, int repositoryId, string version,
        DateTime? queuedAt = null)
    {
        var repo = await db.Repositories.SingleAsync(r => r.Id == repositoryId);
        var patch = new XivPatch
        {
            RepoVersion = new XivRepoVersion
            {
                Repository = repo,
                RepositoryId = repo.Id,
                VersionString = version
            },
            RemoteOriginPath = $"https://example.test/ffxiv/260612/ex0/{version}.patch",
            Size = 1024,
            FirstSeen = queuedAt,
            LastSeen = queuedAt,
            FirstOffered = queuedAt,
            LastOffered = queuedAt,
            IsActive = true,
            NotificationQueuedAtUtc = queuedAt,
            NotificationDiscoveryType = queuedAt is null ? null : PatchDiscoveryType.Offered.ToString()
        };
        db.Patches.Add(patch);
        await db.SaveChangesAsync();
        return patch;
    }

    private static ThaliakContext CreateContext(string? databasePath = null)
    {
        databasePath ??= CreateDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<ThaliakContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ThaliakContext(options);
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), "thaliak-tests", $"{Guid.NewGuid():N}.db");
    }

    private sealed class RecordingDiscordSender : IPatchDiscordAlertSender
    {
        public List<DiscordBatch> Batches { get; } = [];

        public Task SendBatchAsync(string region, IReadOnlyCollection<XivPatch> patches,
            PatchDiscoveryType discoveryType, DateTime quietWindowEndedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(new DiscordBatch(region, patches.Count));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }

    private sealed record DiscordBatch(string Region, int PatchCount);
    private sealed record RecordedRequest(Uri? Uri, string Body);
}
