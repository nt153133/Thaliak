using FFXIVDownloader.ZiPatch;
using FFXIVDownloader.ZiPatch.Config;
using FFXIVDownloader.ZiPatch.Util;
using Serilog;

namespace Thaliak.Service.Poller.Patch;

public sealed class PatchApplicationService : IPatchApplicationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ApplyAsync(
        FileInfo patchFile,
        DirectoryInfo targetDirectory,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            targetDirectory.Create();
            Log.Information("[PATCHER] Installing {PatchFile} to {TargetDirectory}",
                patchFile.FullName, targetDirectory.FullName);

            await using var config = new PersistentZiPatchConfig(targetDirectory.FullName)
            {
                Platform = ZiPatchConfig.PlatformId.Win32,
                IgnoreMissing = true,
                IgnoreOldMismatch = true
            };

            using var fileStream = patchFile.OpenRead();
            using var patchStream = new PositionedStream(fileStream);
            using var patchFileReader = new ZiPatchFile(patchStream);
            await foreach (var chunk in patchFileReader
                               .GetChunksAsync(cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await chunk.ApplyAsync(config);
            }

            Log.Information("[PATCHER] Patch {PatchFile} installed", patchFile.FullName);
        }
        finally
        {
            _gate.Release();
        }
    }
}
