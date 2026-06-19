using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Service.Poller.Patch;

namespace Thaliak.Service.Poller.Polling.TraditionalChinese;

public class TraditionalChinesePollerService(ThaliakContext db, HttpClient client,
    PatchReconciliationService reconciliationService) : IPoller
{
    public const int GameRepoId = 20;

    private const string PatchListUrl = "https://user-cdn.ffxiv.com.tw/launcher/patch/v2.txt";

    public async Task Poll()
    {
        Log.Information("TraditionalChinesePollerService: starting poll operation");

        var gameRepo = db.Repositories
            .Include(r => r.RepoVersions)
            .FirstOrDefault(r => r.Id == GameRepoId);
        if (gameRepo == null) {
            throw new InvalidDataException("Could not find TC game repo in the Repository table!");
        }

        try {
            var pendingPatches = await CheckGameVersion();

            if (pendingPatches.Length > 0) {
                Log.Information("Discovered TC game patches: {0}", pendingPatches);
                await reconciliationService.ReconcileAsync(gameRepo, pendingPatches);
            } else {
                Log.Warning("No TC game patches found on the remote server, not reconciling");
            }
        } finally {
            Log.Information("TraditionalChinesePollerService: poll complete");
        }
    }

    public async Task<PatchListEntry[]> CheckGameVersion()
    {
        var response = await client.GetAsync(PatchListUrl);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) {
            return [];
        }

        Log.Verbose("TC game patching is needed... List:\n{PatchList}", text);

        return TCPatchListParser.Parse(text).OrderBy(p => p.VersionId).ToArray();
    }
}
