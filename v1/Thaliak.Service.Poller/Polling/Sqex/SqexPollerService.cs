using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Util;

namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class SqexPollerService : IPoller
{
    private readonly ThaliakContext _db;
    private readonly HttpClient _client;
    private readonly ISqexLauncherClient _launcherClient;
    private readonly SqexAccountProvider _accountProvider;
    private readonly PatchReconciliationService _reconciliationService;
    private readonly IPatchApplicationService _patchApplicationService;
    private readonly GlobalExpansionSweepCoordinator _expansionSweepCoordinator;
    private readonly string _patchDownloadPath;

    public const int BootRepoId = 1;
    public const int GameRepoId = 2;

    private TempDirectory? _tempBootDir;
    private readonly DirectoryInfo _gameDir;

    public SqexPollerService(
        ThaliakContext db,
        HttpClient client,
        ISqexLauncherClient launcherClient,
        SqexAccountProvider accountProvider,
        PatchReconciliationService reconciliationService,
        IPatchApplicationService patchApplicationService,
        GlobalExpansionSweepCoordinator expansionSweepCoordinator,
        IConfiguration configuration)
    {
        _db = db;
        _client = client;
        _launcherClient = launcherClient;
        _accountProvider = accountProvider;
        _reconciliationService = reconciliationService;
        _patchApplicationService = patchApplicationService;
        _expansionSweepCoordinator = expansionSweepCoordinator;
        _patchDownloadPath = Path.GetFullPath(configuration.GetValue<string>("Directories:Patches"));

        var bootDirectoryName = configuration.GetValue<string>("Directories:Boot");
        if (string.IsNullOrWhiteSpace(bootDirectoryName)) {
            _tempBootDir = new TempDirectory();
            _gameDir = _tempBootDir;
        } else {
            _gameDir = new DirectoryInfo(bootDirectoryName);
            Directory.CreateDirectory(_gameDir.FullName);
        }
    }

    public async Task Poll()
    {
        Log.Information("SqexPollerService: starting poll operation");

        var account = _accountProvider.GetRequired(XivAccountPurpose.Routine);
        var bootRepository = _db.Repositories
            .Include(repository => repository.RepoVersions)
            .FirstOrDefault(repository => repository.Id == BootRepoId);
        var gameRepository = _db.Repositories
            .Include(repository => repository.RepoVersions)
            .FirstOrDefault(repository => repository.Id == GameRepoId);
        if (bootRepository is null || gameRepository is null) {
            throw new InvalidDataException("Could not find boot/game repo in the Repository table.");
        }

        try {
            await CheckBootAsync(bootRepository, null);
            await CheckBootAsync(bootRepository, _gameDir);

            var routineResult = await CheckGameAsync(gameRepository, _gameDir, account);
            await _expansionSweepCoordinator.TryRunAsync(
                gameRepository,
                _gameDir,
                routineResult.NewlyOfferedPatches);
        } finally {
            Log.Information("SqexPollerService: poll complete");
            _tempBootDir?.Dispose();
            _tempBootDir = null;
        }
    }

    private async Task CheckBootAsync(XivRepository repository, DirectoryInfo? gameDirectory)
    {
        var bootPatches = await _launcherClient.CheckBootVersionAsync(gameDirectory);
        if (bootPatches.Length > 0) {
            Log.Information("Discovered JP boot patches: {BootPatches}", bootPatches);
            await _reconciliationService.ReconcileAsync(repository, bootPatches);

            if (gameDirectory is not null) {
                var latest = bootPatches.Last().VersionId;
                var currentBoot = Repository.Boot.GetVer(gameDirectory);
                if (currentBoot != latest) {
                    Log.Information("Boot needs patching (current: {CurrentBoot}, latest: {LatestBoot})",
                        currentBoot, latest);
                    await PatchBootAsync(gameDirectory, FilterPatchesFromVersion(bootPatches, currentBoot));
                } else {
                    Log.Information("Boot already up to date at version {CurrentBoot}", currentBoot);
                }
            }
        } else if (gameDirectory is null) {
            Log.Warning("No JP boot patches found on the remote server, not reconciling");
        }
    }

    private async Task<PatchReconciliationResult> CheckGameAsync(
        XivRepository repository,
        DirectoryInfo gameDirectory,
        XivAccount account)
    {
        var loginResult = await _launcherClient.LoginAsync(account, gameDirectory, forceBaseVersion: true);
        if (loginResult.State != LoginState.NeedsPatchGame) {
            Log.Warning("Received unexpected LoginState: {LoginState}. Not reconciling game patches.",
                loginResult.State);
            return PatchReconciliationResult.Empty;
        }

        if (loginResult.PendingPatches.Length == 0) {
            Log.Warning("No JP game patches found on the remote server, not reconciling");
            return PatchReconciliationResult.Empty;
        }

        Log.Information("Discovered JP game patches: {GamePatches}", loginResult.PendingPatches);
        return await _reconciliationService.ReconcileAsync(repository, loginResult.PendingPatches);
    }

    private async Task PatchBootAsync(DirectoryInfo gameDirectory, PatchListEntry[] patches)
    {
        Log.Information("Starting boot patch process for {PatchCount} patches", patches.Length);
        await WaitForPatchDownloadsAsync(patches);

        var installer = new PatchInstaller(gameDirectory, _patchApplicationService);
        foreach (var patch in patches) {
            installer.QueueInstall(new PatchInstallData
            {
                PatchFile = GetPatchFileInfo(patch),
                Repo = Repository.Boot,
                VersionId = patch.VersionId
            });
        }

        await installer.InstallAllQueuedPatchesAsync();
        Log.Information("Boot patch complete");
    }

    private static PatchListEntry[] FilterPatchesFromVersion(PatchListEntry[] patches, string fromVersion)
    {
        var found = false;
        var result = new List<PatchListEntry>();

        foreach (var patch in patches) {
            if (!found && patch.VersionId == fromVersion) {
                found = true;
                continue;
            }

            if (found) {
                result.Add(patch);
            }
        }

        return found ? result.ToArray() : patches;
    }

    private async Task WaitForPatchDownloadsAsync(
        PatchListEntry[] patches,
        CancellationToken cancellationToken = default)
    {
        const int pollIntervalMilliseconds = 1000;
        const int timeoutSeconds = 60 * 10;
        var maxAttempts = timeoutSeconds * 1000 / pollIntervalMilliseconds;

        Log.Information("Waiting for {PatchCount} patches to be downloaded", patches.Length);

        if (!IsDownloadQueueEnabled()) {
            Log.Information("General download queue is disabled; downloading required boot patches directly");
            foreach (var patch in patches) {
                var patchFile = GetPatchFileInfo(patch);
                if (!patchFile.Exists) {
                    await DownloadRequiredPatchAsync(patch, patchFile, cancellationToken);
                }
            }
        }

        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();

            if (patches.All(patch => GetPatchFileInfo(patch).Exists)) {
                Log.Information("All patches downloaded successfully");
                return;
            }

            await Task.Delay(pollIntervalMilliseconds, cancellationToken);
        }

        throw new TimeoutException($"Timeout waiting for patches to download after {timeoutSeconds} seconds");
    }

    private async Task DownloadRequiredPatchAsync(
        PatchListEntry patch,
        FileInfo patchFile,
        CancellationToken cancellationToken)
    {
        Log.Information("Downloading required boot patch {PatchUrl} to {PatchPath}",
            patch.Url, patchFile.FullName);

        patchFile.Directory?.Create();
        await using var remote = await _client.GetStreamAsync(patch.Url, cancellationToken);
        await using var local = File.Create(patchFile.FullName);
        await remote.CopyToAsync(local, cancellationToken);
    }

    private static bool IsDownloadQueueEnabled()
    {
        var enableDownloads = Environment.GetEnvironmentVariable("ENABLE_DOWNLOADS");
        return !string.IsNullOrEmpty(enableDownloads) &&
               enableDownloads.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private FileInfo GetPatchFileInfo(PatchListEntry patch)
    {
        var databasePatch = _db.Patches
            .Include(candidate => candidate.RepoVersion)
            .FirstOrDefault(candidate => candidate.RemoteOriginPath == patch.Url);
        if (databasePatch is null) {
            throw new InvalidOperationException($"Could not find patch in database: {patch.Url}");
        }

        return new FileInfo(Path.Combine(_patchDownloadPath, databasePatch.LocalStoragePath));
    }
}
