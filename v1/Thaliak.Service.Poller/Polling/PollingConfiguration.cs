using Microsoft.Extensions.Configuration;

namespace Thaliak.Service.Poller.Polling;

public static class PollingConfiguration
{
    public const string DisableKoreaChecksKey = "Polling:DisableKoreaChecks";

    public static bool ShouldRegisterKoreaChecks(IConfiguration configuration)
    {
        return !configuration.GetValue<bool>(DisableKoreaChecksKey);
    }
}
