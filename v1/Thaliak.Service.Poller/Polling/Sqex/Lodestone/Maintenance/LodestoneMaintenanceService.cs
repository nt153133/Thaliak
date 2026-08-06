using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Serilog;

namespace Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;

public class LodestoneMaintenanceService(HttpClient http) : IPoller
{
    private const string NewsFeedUrl = "https://na.finalfantasyxiv.com/lodestone/news/news.xml";

    private static readonly Regex StandardMaintenanceTimeRegex =
        new(@"\[Date & Time\][\n\r]+([\w\d,:. ]+) to ([\w\d,:. ]+) \((\w{3})\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScheduledMaintenanceTimeRegex =
        new(@"from\s+(?<startTime>\d{1,2}(?::\d{2})?\s+[ap]\.m\.)\s+on\s+" +
            @"(?<startDate>[a-z]+\.?\s+\d{1,2},\s+\d{4})\s+to\s+(?:approximately\s+)?" +
            @"(?<endTime>\d{1,2}(?::\d{2})?\s+[ap]\.m\.)\s+on\s+" +
            @"(?<endDate>[a-z]+\.?\s+\d{1,2},\s+\d{4})\s+\((?<zone>\w{3})\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static HashSet<MaintenanceInfo> MaintenanceList { get; } = new();

    public MaintenanceInfo? GetMaintenanceAt(DateTime time)
    {
        time = TimeZoneInfo.ConvertTimeToUtc(time);
        return MaintenanceList.FirstOrDefault(maint => maint.IsActiveAt(time));
    }

    public MaintenanceInfo? GetMaintenanceNear(DateTime time)
    {
        time = TimeZoneInfo.ConvertTimeToUtc(time);
        return MaintenanceList
            .OrderBy(maint => maint.StartTime)
            .FirstOrDefault(maint => time >= maint.StartTime.AddHours(-1) && time <= maint.EndTime.AddHours(2));
    }

    public MaintenanceInfo? GetNextMaintenance(DateTime time)
    {
        time = TimeZoneInfo.ConvertTimeToUtc(time);
        return MaintenanceList
            .Where(maint => maint.EndTime >= time)
            .OrderBy(maint => maint.StartTime)
            .FirstOrDefault();
    }

    public async Task Poll()
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any()) {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ThaliakPoller/1.0)");
        }

        var xml = await http.GetStringAsync(NewsFeedUrl);
        var maintInfo = ParseMaintenanceInfos(xml, DateTime.UtcNow);
        MaintenanceList.UnionWith(maintInfo);

        MaintenanceList.RemoveWhere(mi => DateTime.UtcNow - mi.EndTime > TimeSpan.FromDays(7));
    }

    public static IReadOnlyCollection<MaintenanceInfo> ParseMaintenanceInfos(string xml, DateTime nowUtc)
    {
        var feed = DeserializeFeed(xml);
        if (feed?.Entry is null) {
            return [];
        }

        return feed.Entry
            .Where(entry => IsMaintenanceCategory(entry.Category?.Term))
            .Where(entry => entry.Title.Contains("All Worlds", StringComparison.OrdinalIgnoreCase) &&
                            entry.Title.Contains("Maintenance", StringComparison.OrdinalIgnoreCase))
            .Select(entry => TryCreateMaintenanceInfo(entry, nowUtc))
            .Where(info => info is not null)
            .Cast<MaintenanceInfo>()
            .ToArray();
    }

    private static MaintenanceInfo? TryCreateMaintenanceInfo(LodestoneFeedEntry entry, DateTime nowUtc)
    {
        var text = entry.Content?.GetText() ?? string.Empty;
        if (!TryParseMaintenanceWindow(text, out var start, out var end, out var zone)) {
            Log.Warning("Could not find maintenance time for Lodestone feed entry {Title}", entry.Title);
            return null;
        }

        var utcOffset = zone switch
        {
            "PDT" => TimeSpan.FromHours(-7),
            "PST" => TimeSpan.FromHours(-8),
            _ => throw new InvalidDataException($"Unknown Lodestone maintenance timezone: {zone}")
        };

        var startUtc = new DateTimeOffset(start, utcOffset).UtcDateTime;
        var endUtc = new DateTimeOffset(end, utcOffset).UtcDateTime;
        if (endUtc < startUtc) {
            endUtc = endUtc.AddDays(1);
        }

        return endUtc < nowUtc.AddDays(-1)
            ? null
            : new MaintenanceInfo(startUtc, endUtc, entry.Title);
    }

    private static bool IsMaintenanceCategory(string? category)
    {
        return string.Equals(category, "Maintenance", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "Notices", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseMaintenanceWindow(
        string text,
        out DateTime start,
        out DateTime end,
        out string zone)
    {
        var standardMatch = StandardMaintenanceTimeRegex.Match(text);
        if (standardMatch.Success) {
            start = ParseFeedMaintenanceTime(standardMatch.Groups[1].Value);
            end = ParseFeedMaintenanceTime(standardMatch.Groups[2].Value, start);
            zone = standardMatch.Groups[3].Value.ToUpperInvariant();
            return true;
        }

        var scheduledMatch = ScheduledMaintenanceTimeRegex.Match(text);
        if (scheduledMatch.Success) {
            start = ParseFeedMaintenanceTime(
                $"{scheduledMatch.Groups["startDate"].Value} {scheduledMatch.Groups["startTime"].Value}");
            end = ParseFeedMaintenanceTime(
                $"{scheduledMatch.Groups["endDate"].Value} {scheduledMatch.Groups["endTime"].Value}");
            zone = scheduledMatch.Groups["zone"].Value.ToUpperInvariant();
            return true;
        }

        start = default;
        end = default;
        zone = string.Empty;
        return false;
    }

    private static DateTime ParseFeedMaintenanceTime(string value, DateTime? defaultDate = null)
    {
        string[] formats =
        [
            "MMM d, yyyy h:mm tt",
            "MMMM d, yyyy h:mm tt",
            "h:mm tt",
            "hh:mm tt",
            "h tt",
            "hh tt"
        ];

        var sanitized = value.Replace(".", string.Empty)
            .Replace("from ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("at ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (!DateTime.TryParseExact(sanitized, formats, CultureInfo.GetCultureInfo("en-US"),
                DateTimeStyles.NoCurrentDateDefault, out var parsed)) {
            throw new FormatException($"Could not parse Lodestone maintenance time: {value}");
        }

        if (parsed.Year == 1 && defaultDate is not null) {
            parsed = new DateTime(defaultDate.Value.Year, defaultDate.Value.Month, defaultDate.Value.Day,
                parsed.Hour, parsed.Minute, parsed.Second);
        }

        return parsed;
    }

    private static LodestoneFeed? DeserializeFeed(string xml)
    {
        var serializer = new XmlSerializer(typeof(LodestoneFeed));
        using var reader = new StringReader(xml);
        return serializer.Deserialize(reader) as LodestoneFeed;
    }

    [XmlRoot(ElementName = "feed", Namespace = "http://www.w3.org/2005/Atom")]
    public sealed class LodestoneFeed
    {
        private const string AtomNamespace = "http://www.w3.org/2005/Atom";

        [XmlElement(ElementName = "entry", Namespace = AtomNamespace)]
        public List<LodestoneFeedEntry> Entry { get; set; } = [];
    }

    public sealed class LodestoneFeedEntry
    {
        private const string AtomNamespace = "http://www.w3.org/2005/Atom";

        [XmlElement(ElementName = "title", Namespace = AtomNamespace)]
        public string Title { get; set; } = string.Empty;

        [XmlElement(ElementName = "category", Namespace = AtomNamespace)]
        public LodestoneFeedCategory? Category { get; set; }

        [XmlElement(ElementName = "content", Namespace = AtomNamespace)]
        public LodestoneFeedContent? Content { get; set; }
    }

    public sealed class LodestoneFeedCategory
    {
        [XmlAttribute(AttributeName = "term")]
        public string Term { get; set; } = string.Empty;
    }

    public sealed class LodestoneFeedContent
    {
        [XmlText]
        public string Text { get; set; } = string.Empty;

        public string GetText()
        {
            var text = Text.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);
            text = Regex.Replace(text, @"(\n)\1+", "$1");
            text = Regex.Replace(text, @"^\n+|\n+$", string.Empty);
            return text;
        }
    }
}
