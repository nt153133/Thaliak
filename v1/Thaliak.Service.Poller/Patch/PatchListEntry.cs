using System.Text.RegularExpressions;

namespace Thaliak.Service.Poller.Patch;

public class PatchListEntry
{
    private static readonly Regex UrlRegex = new(".*/((game|boot|ffxiv)/([a-zA-Z0-9]+)/.*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string VersionId { get; set; }
    public string HashType { get; set; }
    public string Url { get; set; }
    public long HashBlockSize { get; set; }
    public string[] Hashes { get; set; }
    public long Length { get; set; }

    public override string ToString() => $"{this.GetRepoName()}/{VersionId}";

    private Match Deconstruct() => UrlRegex.Match(Url);

    public string GetRepoName()
    {
        var match = Deconstruct();
        var rootType = match.Groups[2].Value;
        var name = match.Groups[3].Value;

        if (rootType == "ffxiv") {
            return "ffxiv";
        }

        // The URL doesn't have the "ffxiv" part for ffxiv repo. Let's fake it for readability.
        return name == "4e9a232b" ? "ffxiv" : name;
    }

    public string GetUrlPath() => Deconstruct().Groups[1].Value;

    public string GetFilePath() => GetUrlPath().Replace('/', Path.DirectorySeparatorChar);
}
