using Microsoft.Extensions.Configuration;
using Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;
using Thaliak.Service.Poller.Polling.TraditionalChinese;

namespace Thaliak.Service.Poller.Polling;

public sealed class PollingScheduleService(
    IConfiguration configuration,
    LodestoneMaintenanceService lodestoneMaintenanceService,
    TraditionalChineseMaintenanceService traditionalChineseMaintenanceService)
{
    public static TimeZoneInfo PacificTimeZone { get; } = FindTimeZone("America/Los_Angeles", "Pacific Standard Time");
    public static TimeZoneInfo TaiwanTimeZone { get; } = FindTimeZone("Asia/Taipei", "Taipei Standard Time");

    public DateTime GetNextGlobalOrChinaPatchPoll(DateTime nowUtc)
    {
        var nearMaintenance = lodestoneMaintenanceService.GetMaintenanceNear(nowUtc);
        if (nearMaintenance is not null) {
            return WithJitter(nowUtc.AddMinutes(GetConfiguredMinutes("Polling:MaintenanceActivePollMinutes", 2)));
        }

        var nextMaintenance = lodestoneMaintenanceService.GetNextMaintenance(nowUtc);
        if (nextMaintenance is not null) {
            var preMaintenancePoll = nextMaintenance.StartTime.AddHours(-1);
            if (preMaintenancePoll > nowUtc) {
                return Min(WithJitter(preMaintenancePoll), GetNextDailyPacificRun(nowUtc));
            }
        }

        return GetNextDailyPacificRun(nowUtc);
    }

    public DateTime GetNextSqexFutureScrapePoll(DateTime nowUtc)
    {
        return lodestoneMaintenanceService.GetMaintenanceNear(nowUtc) is not null
            ? WithJitter(nowUtc.AddSeconds(Random.Shared.Next(25, 35)))
            : GetNextGlobalOrChinaPatchPoll(nowUtc);
    }

    public DateTime GetNextLodestoneMaintenancePoll(DateTime nowUtc)
    {
        var nextMaintenance = lodestoneMaintenanceService.GetNextMaintenance(nowUtc);
        if (nextMaintenance is not null && nextMaintenance.StartTime - nowUtc <= TimeSpan.FromHours(24)) {
            return WithJitter(nowUtc.AddMinutes(30));
        }

        return GetNextDailyPacificRun(nowUtc);
    }

    public DateTime GetNextTraditionalChinesePatchPoll(DateTime nowUtc)
    {
        if (traditionalChineseMaintenanceService.HasMaintenanceTodayOrTomorrow(nowUtc)) {
            return WithJitter(nowUtc.AddMinutes(GetConfiguredMinutes(
                "Polling:TraditionalChineseMaintenancePollMinutes", 30)));
        }

        return GetNextDailyPacificRun(nowUtc);
    }

    public DateTime GetNextTraditionalChineseMaintenancePoll(DateTime nowUtc)
    {
        return GetNextDailyPacificRun(nowUtc);
    }

    public DateTime GetNextDailyPacificRun(DateTime nowUtc)
    {
        var configured = configuration.GetValue<string>("Polling:DailyCheckTimePacific") ?? "09:00";
        if (!TimeOnly.TryParse(configured, out var targetTime)) {
            targetTime = new TimeOnly(9, 0);
        }

        var pacificNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, PacificTimeZone);
        var nextPacific = pacificNow.Date.Add(targetTime.ToTimeSpan());
        if (nextPacific <= pacificNow) {
            nextPacific = nextPacific.AddDays(1);
        }

        return WithJitter(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextPacific, DateTimeKind.Unspecified),
            PacificTimeZone));
    }

    private int GetConfiguredMinutes(string key, int defaultValue)
    {
        return Math.Max(1, configuration.GetValue<int?>(key) ?? defaultValue);
    }

    private static DateTime WithJitter(DateTime timeUtc)
    {
        return timeUtc.AddSeconds(Random.Shared.Next(0, 60));
    }

    private static DateTime Min(DateTime left, DateTime right)
    {
        return left <= right ? left : right;
    }

    private static TimeZoneInfo FindTimeZone(string ianaId, string windowsId)
    {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        } catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
