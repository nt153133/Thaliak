using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Serilog;

namespace Thaliak.Service.Poller.Polling.TraditionalChinese;

public sealed class TraditionalChineseMaintenanceService(HttpClient http) : IPoller
{
    private static readonly Uri BaseUri = new("https://www.ffxiv.com.tw");
    private static readonly HtmlParser Parser = new();

    private static readonly string[] MaintenanceKeywords =
    [
        "\u7dad\u8b77",
        "\u505c\u6a5f",
        "\u66ab\u505c\u670d\u52d9",
        "\u670d\u52d9\u4e2d\u65b7"
    ];

    private static readonly string[] GameServiceKeywords =
    [
        "\u7dad\u8b77\u66f4\u65b0\u4f5c\u696d",
        "\u6240\u6709\u4f3a\u670d\u5668",
        "\u5168\u4f3a\u670d\u5668",
        "\u904a\u6232\u4f3a\u670d\u5668",
        "\u904a\u6232\u767b\u5165",
        "\u904a\u73a9\u670d\u52d9",
        "\u904a\u6232\u670d\u52d9",
        "\u505c\u6a5f"
    ];

    private static readonly string[] NonMaintenanceTitleKeywords =
    [
        "\u76ee\u524d\u5df2\u78ba\u8a8d\u7684\u7570\u5e38\u554f\u984c"
    ];

    private static readonly string[] NonGameServiceKeywords =
    [
        "\u5b98\u7db2",
        "\u5b98\u65b9\u7db2\u7ad9",
        "\u5546\u57ce",
        "\u6c34\u6676\u5546\u57ce",
        "\u91d1\u6d41",
        "\u5730\u5716\u670d\u52d9",
        "\u5967\u6c40"
    ];

    public static HashSet<DateOnly> MaintenanceDatesTaiwan { get; } = [];

    public bool HasMaintenanceTodayOrTomorrow(DateTime nowUtc)
    {
        var taiwanNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, PollingScheduleService.TaiwanTimeZone);
        var today = DateOnly.FromDateTime(taiwanNow);
        return MaintenanceDatesTaiwan.Contains(today) || MaintenanceDatesTaiwan.Contains(today.AddDays(1));
    }

    public async Task Poll()
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any()) {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ThaliakPoller/1.0)");
        }

        var notices = await GetMaintenanceNoticesAsync();
        foreach (var notice in notices) {
            MaintenanceDatesTaiwan.Add(notice.PublishedDate);
            MaintenanceDatesTaiwan.Add(notice.PublishedDate.AddDays(1));
        }

        var taiwanToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            PollingScheduleService.TaiwanTimeZone));
        MaintenanceDatesTaiwan.RemoveWhere(date => date < taiwanToday.AddDays(-1));
        Log.Information("Tracked {MaintenanceDateCount} TC maintenance/news dates", MaintenanceDatesTaiwan.Count);
    }

    public async Task<IReadOnlyCollection<TwMaintenanceNotice>> GetMaintenanceNoticesAsync(
        int maxPages = 2,
        CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, TwNewsListItem>(StringComparer.Ordinal);
        foreach (var url in GetListUrls(maxPages)) {
            var document = await GetDocumentAsync(url, cancellationToken);
            foreach (var item in ParseListItems(document)) {
                candidates.TryAdd(item.Id, item);
            }
        }

        var notices = new List<TwMaintenanceNotice>();
        foreach (var item in candidates.Values) {
            var articleText = await GetArticleTextAsync(item.Url, cancellationToken);
            if (!IsMaintenanceNotice(item.Title, articleText)) {
                continue;
            }

            notices.Add(new TwMaintenanceNotice(item.Id, item.Title, item.Url, item.PublishedDate));
        }

        return notices;
    }

    public static bool IsMaintenanceNotice(string title, string articleText)
    {
        if (ContainsAny(title, NonMaintenanceTitleKeywords)) {
            return false;
        }

        var noticeText = $"{title}\n{articleText}";
        if (ContainsAny(noticeText, NonGameServiceKeywords)) {
            return false;
        }

        return ContainsAny(noticeText, MaintenanceKeywords) && ContainsAny(noticeText, GameServiceKeywords);
    }

    private static IEnumerable<Uri> GetListUrls(int maxPages)
    {
        for (var page = 1; page <= maxPages; page++) {
            var pageQuery = page == 1 ? string.Empty : $"?page={page.ToString(CultureInfo.InvariantCulture)}";
            yield return new Uri(BaseUri, $"/web/news/news_list.aspx{pageQuery}");

            var categoryQuery = page == 1
                ? "?category=3"
                : $"?page={page.ToString(CultureInfo.InvariantCulture)}&category=3";
            yield return new Uri(BaseUri, $"/web/news/news_list.aspx{categoryQuery}");
        }
    }

    private async Task<IDocument> GetDocumentAsync(Uri url, CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync(url, cancellationToken);
        return await Parser.ParseDocumentAsync(html, cancellationToken);
    }

    private static IEnumerable<TwNewsListItem> ParseListItems(IDocument document)
    {
        foreach (var item in document.QuerySelectorAll(".list.news_list .item")) {
            var id = item.QuerySelector(".news_id")?.TextContent.Trim();
            var titleAnchor = item.QuerySelector(".second_block .title a");
            var title = titleAnchor?.TextContent.Trim();
            var href = titleAnchor?.GetAttribute("href");
            var dateText = item.QuerySelector(".publish_date")?.TextContent.Trim();

            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(href) ||
                string.IsNullOrWhiteSpace(dateText) ||
                !DateOnly.TryParseExact(dateText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var publishedDate)) {
                continue;
            }

            yield return new TwNewsListItem(id, title, new Uri(BaseUri, href), publishedDate);
        }
    }

    private async Task<string> GetArticleTextAsync(Uri url, CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(url, cancellationToken);
        return document.QuerySelector(".article")?.TextContent.Trim() ?? string.Empty;
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private sealed record TwNewsListItem(string Id, string Title, Uri Url, DateOnly PublishedDate);
}

public sealed record TwMaintenanceNotice(string Id, string Title, Uri Url, DateOnly PublishedDate);
