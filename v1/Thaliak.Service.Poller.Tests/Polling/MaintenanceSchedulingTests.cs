using Microsoft.Extensions.Configuration;
using Thaliak.Service.Poller.Polling;
using Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;
using Thaliak.Service.Poller.Polling.TraditionalChinese;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Polling;

public sealed class MaintenanceSchedulingTests : IDisposable
{
    public MaintenanceSchedulingTests()
    {
        LodestoneMaintenanceService.MaintenanceList.Clear();
        TraditionalChineseMaintenanceService.MaintenanceDatesTaiwan.Clear();
    }

    [Fact]
    public void GetNextGlobalOrChinaPatchPoll_WhenNoMaintenance_ReturnsNextDailyPacificRun()
    {
        var schedule = CreateScheduleService();
        var nowUtc = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);

        var next = schedule.GetNextGlobalOrChinaPatchPoll(nowUtc);

        Assert.InRange(next,
            new DateTime(2026, 6, 12, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 12, 16, 1, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetNextGlobalOrChinaPatchPoll_WhenMaintenanceNear_ReturnsFrequentPoll()
    {
        var nowUtc = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        LodestoneMaintenanceService.MaintenanceList.Add(new MaintenanceInfo(
            nowUtc.AddMinutes(-30),
            nowUtc.AddHours(1),
            "All Worlds Maintenance"));
        var schedule = CreateScheduleService();

        var next = schedule.GetNextGlobalOrChinaPatchPoll(nowUtc);

        Assert.InRange(next, nowUtc.AddMinutes(2), nowUtc.AddMinutes(3));
    }

    [Fact]
    public void GetNextTraditionalChinesePatchPoll_WhenMaintenanceDateKnown_ReturnsTwiceHourlyPoll()
    {
        var nowUtc = new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc);
        var taiwanNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, PollingScheduleService.TaiwanTimeZone);
        TraditionalChineseMaintenanceService.MaintenanceDatesTaiwan.Add(DateOnly.FromDateTime(taiwanNow));
        var schedule = CreateScheduleService();

        var next = schedule.GetNextTraditionalChinesePatchPoll(nowUtc);

        Assert.InRange(next, nowUtc.AddMinutes(30), nowUtc.AddMinutes(31));
    }

    [Fact]
    public void ParseMaintenanceInfos_ReturnsAllWorldsMaintenanceFromAtomFeed()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>All Worlds Maintenance (Jun. 12)</title>
                <category term="Maintenance" />
                <content type="html">[Date &amp; Time]&lt;br&gt;Jun. 12, 2026 1:00 a.m. to 3:00 a.m. (PDT)</content>
              </entry>
              <entry>
                <title>The Lodestone Maintenance (Jun. 12)</title>
                <category term="Maintenance" />
                <content type="html">[Date &amp; Time]&lt;br&gt;Jun. 12, 2026 1:00 a.m. to 3:00 a.m. (PDT)</content>
              </entry>
            </feed>
            """;

        var maint = Assert.Single(LodestoneMaintenanceService.ParseMaintenanceInfos(xml,
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc), maint.StartTime);
        Assert.Equal(new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc), maint.EndTime);
    }

    [Fact]
    public void IsMaintenanceNotice_FiltersOutNonGameTraditionalChineseMaintenance()
    {
        Assert.True(TraditionalChineseMaintenanceService.IsMaintenanceNotice(
            "\u5168\u4f3a\u670d\u5668\u7dad\u8b77\u66f4\u65b0\u4f5c\u696d",
            "\u904a\u6232\u670d\u52d9\u5c07\u66ab\u505c\u670d\u52d9"));
        Assert.False(TraditionalChineseMaintenanceService.IsMaintenanceNotice(
            "\u5b98\u65b9\u7db2\u7ad9\u7dad\u8b77",
            "\u5b98\u7db2\u5c07\u9032\u884c\u7dad\u8b77"));
    }

    public void Dispose()
    {
        LodestoneMaintenanceService.MaintenanceList.Clear();
        TraditionalChineseMaintenanceService.MaintenanceDatesTaiwan.Clear();
    }

    private static PollingScheduleService CreateScheduleService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Polling:DailyCheckTimePacific"] = "09:00",
                ["Polling:MaintenanceActivePollMinutes"] = "2",
                ["Polling:TraditionalChineseMaintenancePollMinutes"] = "30"
            })
            .Build();

        return new PollingScheduleService(
            configuration,
            new LodestoneMaintenanceService(new HttpClient()),
            new TraditionalChineseMaintenanceService(new HttpClient()));
    }
}
