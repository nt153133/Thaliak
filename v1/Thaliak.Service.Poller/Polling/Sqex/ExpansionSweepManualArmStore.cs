using Microsoft.Extensions.Options;

namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class ExpansionSweepManualArmStore(IOptions<GlobalExpansionSweepOptions> options)
{
    private readonly string _path = Path.GetFullPath(options.Value.ManualArmPath);

    public string? ReadTriggerKey()
    {
        if (!File.Exists(_path)) {
            return null;
        }

        var value = File.ReadAllText(_path).Trim();
        return Guid.TryParse(value, out var requestId)
            ? $"manual:{requestId:N}"
            : $"manual:{Guid.NewGuid():N}";
    }

    public void Consume()
    {
        if (File.Exists(_path)) {
            File.Delete(_path);
        }
    }
}
