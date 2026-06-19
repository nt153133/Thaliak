using System.Text.Json.Serialization;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;

namespace Thaliak.Service.Poller.Webhooks;

public sealed record JsonWebhookBatchPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("discoveryType")] string DiscoveryType,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("patchCount")] int PatchCount,
    [property: JsonPropertyName("patches")] IReadOnlyCollection<JsonWebhookPatchPayload> Patches,
    [property: JsonPropertyName("detectedAtUtc")] DateTime DetectedAtUtc,
    [property: JsonPropertyName("quietWindowEndedAtUtc")] DateTime QuietWindowEndedAtUtc)
{
    public static JsonWebhookBatchPayload FromPatches(string region, IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime detectedAtUtc, DateTime quietWindowEndedAtUtc)
    {
        return new JsonWebhookBatchPayload(
            "patch.batch.discovered",
            discoveryType.ToString(),
            region,
            patches.Count,
            patches.Select(JsonWebhookPatchPayload.FromPatch).ToArray(),
            detectedAtUtc,
            quietWindowEndedAtUtc);
    }
}

public sealed record JsonWebhookPatchPayload(
    [property: JsonPropertyName("repositoryName")] string RepositoryName,
    [property: JsonPropertyName("repositorySlug")] string RepositorySlug,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("size")] long Size)
{
    public static JsonWebhookPatchPayload FromPatch(XivPatch patch)
    {
        return new JsonWebhookPatchPayload(
            patch.RepoVersion.Repository.Name,
            patch.RepoVersion.Repository.Slug,
            patch.RepoVersion.VersionString,
            patch.RemoteOriginPath,
            patch.Size);
    }
}
