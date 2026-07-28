using System.Text.RegularExpressions;

namespace Thaliak.Service.Poller.Exceptions;

[Serializable]
public class OauthLoginException : Exception
{
    private static Regex errorMessageRegex =
        new(@"window.external.user\(""login=auth,ng,err,(?<errorMessage>.*)\""\);", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? OauthErrorMessage { get; private set; }

    public OauthLoginException(string document)
        : base(GetErrorMessage(document) ?? "Unknown error")
    {
        this.OauthErrorMessage = GetErrorMessage(document);
    }

    private static string? GetErrorMessage(string document)
    {
        var matches = errorMessageRegex.Matches(document);

        if (matches.Count is 0 or > 1)
        {
            return null;
        }

        return matches[0].Groups["errorMessage"].Value;
    }
}
