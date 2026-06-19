using Microsoft.Extensions.Configuration;

namespace Thaliak.Service.Poller.Notifications;

public static class NotificationConfiguration
{
    public const string QuietWindowMinutesKey = "Notifications:QuietWindowMinutes";
    public const string NotifyScrapedPatchesKey = "Notifications:NotifyScrapedPatches";
    public const string SuppressBootPatchAlertsKey = "Notifications:SuppressBootPatchAlerts";

    public static TimeSpan GetQuietWindow(IConfiguration configuration)
    {
        var minutes = configuration.GetValue<int?>(QuietWindowMinutesKey) ?? 3;
        return TimeSpan.FromMinutes(Math.Max(0, minutes));
    }

    public static bool ShouldNotifyScrapedPatches(IConfiguration configuration)
    {
        return configuration.GetValue<bool>(NotifyScrapedPatchesKey);
    }

    public static bool ShouldSuppressBootPatchAlerts(IConfiguration configuration)
    {
        return configuration.GetValue<bool?>(SuppressBootPatchAlertsKey) ?? true;
    }
}
