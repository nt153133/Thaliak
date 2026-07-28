using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Patch;

namespace Thaliak.Service.Poller.Polling.Sqex;

public interface ISqexLauncherClient
{
    Task<PatchListEntry[]> CheckBootVersionAsync(
        DirectoryInfo? gamePath,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        XivAccount account,
        DirectoryInfo gamePath,
        bool forceBaseVersion,
        CancellationToken cancellationToken = default);
}
