using System.Collections.Concurrent;
using Serilog;

namespace Thaliak.Service.Poller.Patch;

public class PatchInstaller
{
    private readonly ConcurrentQueue<PatchInstallData> queuedInstalls = new();
    private readonly DirectoryInfo gameDirectory;
    private readonly IPatchApplicationService patchApplicationService;

    public PatchInstaller(DirectoryInfo gameDirectory, IPatchApplicationService patchApplicationService)
    {
        this.gameDirectory = gameDirectory;
        this.patchApplicationService = patchApplicationService;
    }

    public void QueueInstall(PatchInstallData installData)
    {
        queuedInstalls.Enqueue(installData);
    }

    public async Task InstallAllQueuedPatchesAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("[PATCHER] Starting batch patch installation");

        while (queuedInstalls.TryDequeue(out var installData))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await InstallPatchAsync(installData, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PATCHER] Patch install failed for {PatchFile}", installData.PatchFile.FullName);
                throw;
            }
        }

        Log.Information("[PATCHER] Batch patch installation complete");
    }

    private async Task InstallPatchAsync(PatchInstallData installData, CancellationToken cancellationToken)
    {
        // Ensure that subdirs exist
        if (!gameDirectory.Exists) gameDirectory.Create();

        gameDirectory.CreateSubdirectory("game");
        gameDirectory.CreateSubdirectory("boot");

        await patchApplicationService.ApplyAsync(
            installData.PatchFile,
            new DirectoryInfo(Path.Combine(
                gameDirectory.FullName,
                installData.Repo == Repository.Boot ? "boot" : "game")),
            cancellationToken);

        try
        {
            installData.Repo.SetVer(gameDirectory, installData.VersionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not set ver file");
            throw;
        }
    }

}
