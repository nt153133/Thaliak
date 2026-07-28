namespace Thaliak.Service.Api.Artifacts;

public sealed class ArtifactOptions
{
    public const string SectionName = "Artifacts";

    public bool Enabled { get; set; }

    public string Root { get; set; } = "/srv/thaliak/artifacts";

    public string? PatchRoot { get; set; }

    public int PollIntervalSeconds { get; set; } = 300;

    public string Compression { get; set; } = "Brotli";

    public string? BasePatchUrl { get; set; }

    public string? PublicBaseUrl { get; set; }

    public List<ArtifactReadyWebhookOptions> ReadyWebhooks { get; set; } = [];
}
