using Microsoft.Extensions.Configuration;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Webhooks;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Webhooks;

public class JsonWebhookServiceTests
{
    [Fact]
    public async Task SendPatchBatchDiscoveredAsync_SendsOnlyToSubscribedRegions()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webhooks:JsonEndpoints:0:Name"] = "tc",
                ["Webhooks:JsonEndpoints:0:Url"] = "https://example.test/tc",
                ["Webhooks:JsonEndpoints:0:Regions:0"] = "TC",
                ["Webhooks:JsonEndpoints:1:Name"] = "korea",
                ["Webhooks:JsonEndpoints:1:Url"] = "https://example.test/korea",
                ["Webhooks:JsonEndpoints:1:Regions:0"] = "Korea"
            })
            .Build();
        var service = new JsonWebhookService(client, configuration);
        var patch = new XivPatch
        {
            RemoteOriginPath = "https://mydownloadakamai.ffxiv.com.tw/ffxiv/260515/ex5/2026-05-15-0001-0000.patch",
            Size = 17942043,
            RepoVersion = new XivRepoVersion
            {
                VersionString = "2026.05.15.0000.0000",
                Repository = new XivRepository
                {
                    Id = 25,
                    Name = "traditional_chinese/win32/release/ex5",
                    ServiceId = 4
                }
            }
        };

        await service.SendPatchBatchDiscoveredAsync("TC", [patch], PatchDiscoveryType.Offered,
            new DateTime(2026, 6, 11, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 11, 18, 3, 0, DateTimeKind.Utc));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://example.test/tc", request.Uri?.ToString());
        Assert.Contains("\"event\":\"patch.batch.discovered\"", request.Body);
        Assert.Contains("\"region\":\"TC\"", request.Body);
        Assert.Contains("\"patchCount\":1", request.Body);
        Assert.Contains("\"repositoryName\":\"traditional_chinese/win32/release/ex5\"", request.Body);
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

    private sealed record RecordedRequest(Uri? Uri, string Body);
}
