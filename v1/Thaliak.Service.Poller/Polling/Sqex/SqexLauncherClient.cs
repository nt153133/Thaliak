using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Exceptions;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Util;

namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class SqexLauncherClient(HttpClient client) : ISqexLauncherClient
{
    private const string OauthTopUrl =
        "https://ffxiv-login.square-enix.com/oauth/ffxivarr/login/top?lng=en&rgn=3&isft=0&cssmode=1&isnew=1&launchver=3";

    private const string UserAgentTemplate = "SQEXAuthor/2.0.0(Windows 6.2; ja-jp; {0})";
    private readonly string _userAgent = string.Format(UserAgentTemplate, MakeComputerId());

    private static readonly string[] FilesToHash =
    [
        "ffxivboot.exe",
        "ffxivboot64.exe",
        "ffxivlauncher.exe",
        "ffxivlauncher64.exe",
        "ffxivupdater.exe",
        "ffxivupdater64.exe"
    ];

    public async Task<PatchListEntry[]> CheckBootVersionAsync(
        DirectoryInfo? gamePath,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://patch-bootver.ffxiv.com/http/win32/ffxivneo_release_boot/{(gamePath is null ? Constants.BASE_GAME_VERSION : Repository.Boot.GetVer(gamePath))}/?time=" +
            GetLauncherFormattedTimeLongRounded());

        request.Headers.AddWithoutValidation("User-Agent", Constants.PATCHER_USER_AGENT);
        request.Headers.AddWithoutValidation("Host", "patch-bootver.ffxiv.com");

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrEmpty(text)) {
            return [];
        }

        Log.Verbose("Boot patching is needed... List:\n{PatchList}", text);
        return PatchListParser.Parse(text);
    }

    public async Task<LoginResult> LoginAsync(
        XivAccount account,
        DirectoryInfo gamePath,
        bool forceBaseVersion,
        CancellationToken cancellationToken = default)
    {
        Log.Information("XivGame::Login using {AccountPurpose} account {AccountId}", account.Purpose, account.Id);

        var oauthLoginResult = await OauthLoginAsync(account.Username, account.Password, cancellationToken);

        Log.Information(
            "OAuth login successful - playable:{Playable} terms:{TermsAccepted} region:{Region} expack:{MaxExpansion}",
            oauthLoginResult.Playable,
            oauthLoginResult.TermsAccepted,
            oauthLoginResult.Region,
            oauthLoginResult.MaxExpansion);

        if (!oauthLoginResult.Playable) {
            return new LoginResult
            {
                OauthLogin = oauthLoginResult,
                State = LoginState.NoService
            };
        }

        if (!oauthLoginResult.TermsAccepted) {
            return new LoginResult
            {
                OauthLogin = oauthLoginResult,
                State = LoginState.NoTerms
            };
        }

        PatchListEntry[] pendingPatches = [];
        string? uniqueId = null;
        LoginState loginState;

        try {
            (pendingPatches, uniqueId) = await CheckGameVersionAsync(
                oauthLoginResult,
                gamePath,
                forceBaseVersion,
                cancellationToken);
            loginState = pendingPatches.Length > 0 ? LoginState.NeedsPatchGame : LoginState.Ok;
        } catch (VersionCheckLoginException ex) {
            loginState = ex.State;
        }

        return new LoginResult
        {
            PendingPatches = pendingPatches,
            OauthLogin = oauthLoginResult,
            State = loginState,
            UniqueId = uniqueId
        };
    }

    private static string MakeComputerId()
    {
        var hashString = Environment.MachineName + Environment.UserName + Environment.OSVersion +
                         Environment.ProcessorCount;
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(hashString));
        var bytes = new byte[5];
        Array.Copy(hash, 0, bytes, 1, 4);
        bytes[0] = (byte)-(bytes[1] + bytes[2] + bytes[3] + bytes[4]);
        return Convert.ToHexStringLower(bytes);
    }

    private string GenerateFrontierReferer()
    {
        return $"https://launcher.finalfantasyxiv.com/v610/index.html?rc_lang=ja&time={GetLauncherFormattedTimeLong()}";
    }

    private async Task<string> GetOauthTopAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OauthTopUrl);
        request.Headers.AddWithoutValidation("Accept",
            "image/gif, image/jpeg, image/pjpeg, application/x-ms-application, application/xaml+xml, application/x-ms-xbap, */*");
        request.Headers.AddWithoutValidation("Referer", GenerateFrontierReferer());
        request.Headers.AddWithoutValidation("Accept-Encoding", "gzip, deflate");
        request.Headers.AddWithoutValidation("Accept-Language", "ja");
        request.Headers.AddWithoutValidation("User-Agent", _userAgent);
        request.Headers.AddWithoutValidation("Connection", "Keep-Alive");
        request.Headers.AddWithoutValidation("Cookie", "_rsid=\"\"");

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (text.Contains("window.external.user(\"restartup\");", StringComparison.Ordinal)) {
            throw new InvalidResponseException("Launcher requested restartup.", text);
        }

        var storedRegex = new Regex(@"\t<\s*input .* name=""_STORED_"" value=""(?<stored>.*)"">");
        var matches = storedRegex.Matches(text);
        if (matches.Count == 0) {
            throw new InvalidResponseException("Could not get launcher login token.", text);
        }

        return matches[0].Groups["stored"].Value;
    }

    private async Task<OauthLoginResult> OauthLoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var topResult = await GetOauthTopAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://ffxiv-login.square-enix.com/oauth/ffxivarr/login/login.send");
        request.Headers.AddWithoutValidation("Accept",
            "image/gif, image/jpeg, image/pjpeg, application/x-ms-application, application/xaml+xml, application/x-ms-xbap, */*");
        request.Headers.AddWithoutValidation("Referer", OauthTopUrl);
        request.Headers.AddWithoutValidation("Accept-Language", "ja");
        request.Headers.AddWithoutValidation("User-Agent", _userAgent);
        request.Headers.AddWithoutValidation("Accept-Encoding", "gzip, deflate");
        request.Headers.AddWithoutValidation("Host", "ffxiv-login.square-enix.com");
        request.Headers.AddWithoutValidation("Connection", "Keep-Alive");
        request.Headers.AddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.AddWithoutValidation("Cookie", "_rsid=\"\"");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_STORED_"] = topResult,
            ["sqexid"] = username,
            ["password"] = password,
            ["otppw"] = string.Empty
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var reply = await response.Content.ReadAsStringAsync(cancellationToken);
        var regex = new Regex(@"window.external.user\(""login=auth,ok,(?<launchParams>.*)\);");
        var matches = regex.Matches(reply);
        if (matches.Count == 0) {
            throw new OauthLoginException(reply);
        }

        var launchParams = matches[0].Groups["launchParams"].Value.Split(',');
        return new OauthLoginResult
        {
            SessionId = launchParams[1],
            Region = int.Parse(launchParams[5]),
            TermsAccepted = launchParams[3] != "0",
            Playable = launchParams[9] != "0",
            MaxExpansion = int.Parse(launchParams[13])
        };
    }

    private async Task<(PatchListEntry[] Patches, string UniqueId)> CheckGameVersionAsync(
        OauthLoginResult oauthLoginResult,
        DirectoryInfo gamePath,
        bool forceBaseVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://patch-gamever.ffxiv.com/http/win32/ffxivneo_release_game/{(forceBaseVersion ? Constants.BASE_GAME_VERSION : Repository.Ffxiv.GetVer(gamePath))}/{oauthLoginResult.SessionId}");
        request.Headers.AddWithoutValidation("X-Hash-Check", "enabled");
        request.Headers.AddWithoutValidation("User-Agent", Constants.PATCHER_USER_AGENT);
        request.Content = new StringContent(GetVersionReport(gamePath, oauthLoginResult.MaxExpansion,
            forceBaseVersion));

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict) {
            throw new VersionCheckLoginException(LoginState.NeedsPatchBoot);
        }

        if (!response.Headers.TryGetValues("X-Patch-Unique-Id", out var uidValues)) {
            throw new InvalidResponseException("Could not get patch unique ID.", text);
        }

        var uniqueId = uidValues.First();
        if (string.IsNullOrEmpty(text)) {
            return ([], uniqueId);
        }

        Log.Verbose("Game patching is needed... List:\n{PatchList}", text);
        return (PatchListParser.Parse(text), uniqueId);
    }

    private static string GetLauncherFormattedTimeLong()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm");
    }

    private static string GetLauncherFormattedTimeLongRounded()
    {
        var formatted = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm").ToCharArray();
        formatted[15] = '0';
        return new string(formatted);
    }

    private static string GetVersionReport(DirectoryInfo gamePath, int expansionLevel, bool forceBaseVersion)
    {
        var versionReport = GetBootVersionHash(gamePath);

        if (expansionLevel >= 1) {
            versionReport += $"\nex1\t{GetRepositoryVersion(Repository.Ex1, gamePath, forceBaseVersion)}";
        }

        if (expansionLevel >= 2) {
            versionReport += $"\nex2\t{GetRepositoryVersion(Repository.Ex2, gamePath, forceBaseVersion)}";
        }

        if (expansionLevel >= 3) {
            versionReport += $"\nex3\t{GetRepositoryVersion(Repository.Ex3, gamePath, forceBaseVersion)}";
        }

        if (expansionLevel >= 4) {
            versionReport += $"\nex4\t{GetRepositoryVersion(Repository.Ex4, gamePath, forceBaseVersion)}";
        }

        if (expansionLevel >= 5) {
            versionReport += $"\nex5\t{GetRepositoryVersion(Repository.Ex5, gamePath, forceBaseVersion)}";
        }

        return versionReport;
    }

    private static string GetRepositoryVersion(
        Repository repository,
        DirectoryInfo gamePath,
        bool forceBaseVersion)
    {
        return forceBaseVersion ? Constants.BASE_GAME_VERSION : repository.GetVer(gamePath);
    }

    private static string GetBootVersionHash(DirectoryInfo gamePath)
    {
        var result = Repository.Boot.GetVer(gamePath) + "=";
        foreach (var filename in FilesToHash) {
            var path = Path.Combine(gamePath.FullName, "boot", filename);
            if (File.Exists(path)) {
                result += $"{filename}/{GetFileHash(path)},";
            }
        }

        return result.TrimEnd(',');
    }

    private static string GetFileHash(string file)
    {
        using var stream = File.OpenRead(file);
        var hash = SHA1.HashData(stream);
        var length = stream.Length;
        return $"{length}/{Convert.ToHexStringLower(hash)}";
    }
}
