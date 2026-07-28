namespace Thaliak.Service.Api.Artifacts;

public sealed class ArtifactReadyWebhookOptions
{
    public string? Name { get; set; }

    public string? Url { get; set; }

    public bool Enabled { get; set; } = true;

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
