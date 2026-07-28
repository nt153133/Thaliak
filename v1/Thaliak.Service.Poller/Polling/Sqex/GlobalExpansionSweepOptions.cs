namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class GlobalExpansionSweepOptions
{
    public const string SectionName = "Polling:GlobalExpansionSweep";

    public bool Enabled { get; set; } = true;

    public int RequiredMaxExpansion { get; set; } = 5;

    public string ManualArmPath { get; set; } = "./data/control/global-expansion-sweep.arm";
}
