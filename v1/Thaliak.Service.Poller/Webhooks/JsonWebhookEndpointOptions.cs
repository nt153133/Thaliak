namespace Thaliak.Service.Poller.Webhooks;

public sealed class JsonWebhookEndpointOptions
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public List<string> Regions { get; set; } = [];

    public bool IsSubscribedTo(string region)
    {
        return Regions.Any(r => string.Equals(r, region, StringComparison.OrdinalIgnoreCase));
    }
}
