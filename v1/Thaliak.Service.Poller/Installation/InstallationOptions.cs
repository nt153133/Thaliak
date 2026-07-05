namespace Thaliak.Service.Poller.Installation;

public sealed class InstallationOptions
{
    public const string SectionName = "Installations";

    public bool Enabled { get; set; }

    public string Root { get; set; } = "./data/installations";

    public string[] Regions { get; set; } = ["Global", "China", "TC"];
}
