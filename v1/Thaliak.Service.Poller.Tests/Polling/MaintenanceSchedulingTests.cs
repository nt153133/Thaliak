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

    [Fact]
    public void TryParseMaintenanceDateTaiwan_WhenTitleHasSlashDate_ReturnsMaintenanceDate()
    {
        var parsed = TraditionalChineseMaintenanceService.TryParseMaintenanceDateTaiwan(
            "06/23 \u7dad\u8b77\u66f4\u65b0\u4f5c\u696d",
            "\u904a\u6232\u670d\u52d9\u5c07\u66ab\u505c\u670d\u52d9",
            new DateOnly(2026, 6, 16),
            out var maintenanceDate);

        Assert.True(parsed);
        Assert.Equal(new DateOnly(2026, 6, 23), maintenanceDate);
    }

    [Fact]
    public void GetMaintenancePollingDates_WhenMaintenanceDateParsed_UsesMaintenanceDate()
    {
        var notice = new TwMaintenanceNotice(
            "187",
            "06/23 \u7dad\u8b77\u66f4\u65b0\u4f5c\u696d",
            new Uri("https://www.ffxiv.com.tw/web/news/news_in.aspx?id=187"),
            new DateOnly(2026, 6, 16),
            new DateOnly(2026, 6, 23));

        var dates = TraditionalChineseMaintenanceService.GetMaintenancePollingDates(notice);

        Assert.Equal([new DateOnly(2026, 6, 23), new DateOnly(2026, 6, 24)], dates);
    }

    [Fact]
    public async Task GetMaintenanceNoticesAsync_WhenNoticePostedEarlier_ParsesMaintenanceDateFromTitle()
    {
        using var http = new HttpClient(new TwMaintenanceHttpHandler());
        var service = new TraditionalChineseMaintenanceService(http);

        var notice = Assert.Single(await service.GetMaintenanceNoticesAsync(maxPages: 1));

        Assert.Equal("187", notice.Id);
        Assert.Equal(new DateOnly(2026, 6, 16), notice.PublishedDate);
        Assert.Equal(new DateOnly(2026, 6, 23), notice.MaintenanceDate);
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

    private sealed class TwMaintenanceHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var html = request.RequestUri?.AbsolutePath.Contains("news_in.aspx", StringComparison.Ordinal) == true
                ? ArticleHtml
                : ListHtml;

            return Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent(html)
            });
        }

        private const string ListHtml = """
            <html>
            <body>
                <div class="list news_list">
                    <div class="item">
                        <div class="news_id">187</div>
                        <div class="second_block">
                            <div class="title">
                                <a href="/web/news/news_in.aspx?id=187">06/23 &#x7dad;&#x8b77;&#x66f4;&#x65b0;&#x4f5c;&#x696d;</a>
                            </div>
                        </div>
                        <div class="publish_date">2026/06/16</div>
                    </div>
                </div>
            </body>
            </html>
            """;

        private const string ArticleHtml = """
            <html>
            <body>
                <div class="article">&#x904a;&#x6232;&#x670d;&#x52d9;&#x5c07;&#x66ab;&#x505c;&#x670d;&#x52d9;</div>
            </body>
            </html>
            """;
    }
}
