using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Api.Artifacts;

namespace Thaliak.Service.Api.Services;

public sealed class ArtifactWebhookService(
    HttpClient httpClient,
    ArtifactReadService artifactReadService,
    IOptions<ArtifactOptions> options,
    ILogger<ArtifactWebhookService> logger)
{
    private readonly ArtifactOptions _options = options.Value;

    public async Task SendClutReadyAsync(
        string region,
        IReadOnlyList<XivArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var enabledEndpoints = _options.ReadyWebhooks
            .Where(endpoint => endpoint.Enabled)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Url))
            .ToArray();

        if (enabledEndpoints.Length == 0) {
            return;
        }

        var payload = new
        {
            @event = "artifact.clut.ready",
            region,
            ready_at_utc = DateTime.UtcNow,
            artifacts = artifacts
                .OrderBy(artifact => artifact.RepositorySlug, StringComparer.Ordinal)
                .Select(artifact => new
                {
                    kind = artifact.Kind,
                    repository_slug = artifact.RepositorySlug,
                    version_string = artifact.VersionString,
                    url = artifactReadService.BuildArtifactUrl(artifact.Kind, artifact.RepositorySlug, artifact.VersionString),
                    artifact.Size,
                    sha256 = artifact.Sha256
                })
                .ToArray()
        };

        foreach (var endpoint in enabledEndpoints) {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = JsonContent.Create(payload)
            };

            foreach (var (name, value) in endpoint.Headers) {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                logger.LogWarning(
                    "Artifact webhook {EndpointName} failed with HTTP {StatusCode}",
                    endpoint.Name ?? endpoint.Url,
                    (int)response.StatusCode);
            }
        }
    }
}
