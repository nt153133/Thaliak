using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Serilog;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;

namespace Thaliak.Service.Poller.Webhooks;

public sealed class JsonWebhookService
{
    private readonly HttpClient _client;
    private readonly List<JsonWebhookEndpointOptions> _endpoints;

    public JsonWebhookService(HttpClient client, IConfiguration configuration)
    {
        _client = client;
        _endpoints = configuration
            .GetSection("Webhooks:JsonEndpoints")
            .Get<List<JsonWebhookEndpointOptions>>() ?? [];
    }

    public async Task SendPatchBatchDiscoveredAsync(string region, IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime detectedAtUtc, DateTime quietWindowEndedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (_endpoints.Count == 0 || patches.Count == 0) {
            return;
        }

        var payload = JsonWebhookBatchPayload.FromPatches(region, patches, discoveryType, detectedAtUtc,
            quietWindowEndedAtUtc);
        foreach (var endpoint in _endpoints.Where(e => e.IsSubscribedTo(region))) {
            await SendToEndpointAsync(endpoint, payload, cancellationToken);
        }
    }

    private async Task SendToEndpointAsync(JsonWebhookEndpointOptions endpoint, JsonWebhookBatchPayload payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Url)) {
            Log.Warning("Skipping JSON webhook endpoint {Name}: URL is empty", endpoint.Name);
            return;
        }

        try {
            var response = await _client.PostAsJsonAsync(endpoint.Url, payload, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                Log.Warning("JSON webhook endpoint {Name} returned HTTP {StatusCode}", endpoint.Name,
                    response.StatusCode);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            Log.Warning(ex, "Error calling JSON webhook endpoint {Name}", endpoint.Name);
        }
    }
}
