using Microsoft.Extensions.Configuration;

namespace Thaliak.Service.Poller.Polling;

public static class PollingConfiguration
{
    public const string EnabledKey = "Polling:Enabled";
    public const string DisableKoreaChecksKey = "Polling:DisableKoreaChecks";

    public static bool ShouldRegisterPolling(IConfiguration configuration) =>
        configuration.GetValue(EnabledKey, true);

    public static bool ShouldRegisterKoreaChecks(IConfiguration configuration)
    {
        return !configuration.GetValue<bool>(DisableKoreaChecksKey);
    }
}
