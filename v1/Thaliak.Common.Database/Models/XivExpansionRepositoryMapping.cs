using System.Text.RegularExpressions;

namespace Thaliak.Common.Database.Models;

public class XivExpansionRepositoryMapping
{
    private static readonly Regex ExpansionRegex = new(@"(?:https?:\/\/.*\/)?(game|boot)\/(?:ex(\d)|\w+)\/(.*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TraditionalChineseExpansionRegex = new(@"(?:https?:\/\/.*\/)?ffxiv\/[^\/]+\/ex(\d)\/.*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int GameRepositoryId { get; set; }
    public XivRepository GameRepository { get; set; }

    public int ExpansionId { get; set; }

    public int ExpansionRepositoryId { get; set; }
    public XivRepository ExpansionRepository { get; set; }

    public static int GetExpansionId(string patchName)
    {
        var tcMatch = TraditionalChineseExpansionRegex.Match(patchName);
        if (tcMatch.Success)
        {
            return int.Parse(tcMatch.Groups[1].Value);
        }

        var match = ExpansionRegex.Match(patchName);
        if (!match.Success)
        {
            return 0;
        }

        var expansionId = match.Groups[2].Value;
        if (string.IsNullOrEmpty(expansionId))
        {
            return 0;
        }

        return int.Parse(expansionId);
    }
}
