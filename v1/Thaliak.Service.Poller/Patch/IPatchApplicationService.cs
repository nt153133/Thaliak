namespace Thaliak.Service.Poller.Patch;

public interface IPatchApplicationService
{
    Task ApplyAsync(
        FileInfo patchFile,
        DirectoryInfo targetDirectory,
        CancellationToken cancellationToken = default);
}
