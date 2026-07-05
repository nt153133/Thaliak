using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Patch;

namespace Thaliak.Service.Poller.Installation;

public sealed class RegionalInstallationService(
    ThaliakContext db,
    IPatchApplicationService patchApplicationService,
    IOptions<InstallationOptions> options,
    IConfiguration configuration)
{
    private static readonly IReadOnlyDictionary<string, RegionDefinition> Regions =
        new Dictionary<string, RegionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Global"] = new("Global", 2, "global"),
            ["China"] = new("China", 12, "china"),
            ["TC"] = new("TC", 20, "tc")
        };

    private readonly PatchChainResolver _chainResolver = new(db);
    private readonly string _patchRoot = Path.GetFullPath(
        configuration.GetValue<string>("Directories:Patches") ?? "./data/patches");

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var installationRoot = new DirectoryInfo(Path.GetFullPath(options.Value.Root));
        installationRoot.Create();

        foreach (var configuredRegion in options.Value.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Regions.TryGetValue(configuredRegion, out var region))
            {
                Log.Warning("Ignoring unknown installation region {Region}", configuredRegion);
                continue;
            }

            await ReconcileRegionAsync(region, installationRoot, cancellationToken);
        }
    }

    private async Task ReconcileRegionAsync(
        RegionDefinition region,
        DirectoryInfo installationRoot,
        CancellationToken cancellationToken)
    {
        var regionRoot = new DirectoryInfo(Path.Combine(installationRoot.FullName, region.DirectoryName));
        regionRoot.Create();

        var mappings = await db.ExpansionRepositoryMappings
            .AsNoTracking()
            .Where(mapping => mapping.GameRepositoryId == region.GameRepositoryId)
            .OrderBy(mapping => mapping.ExpansionId)
            .ToListAsync(cancellationToken);

        var isComplete = true;
        foreach (var mapping in mappings)
        {
            var repository = mapping.ExpansionId == 0
                ? Repository.Ffxiv
                : (Repository)((int)Repository.Ex1 + mapping.ExpansionId - 1);

            var repositoryComplete = await ReconcileRepositoryAsync(
                region,
                regionRoot,
                mapping.ExpansionRepositoryId,
                repository,
                cancellationToken);

            if (!repositoryComplete)
            {
                isComplete = false;
            }
        }

        Log.Information(
            "Regional installation {Region} reconciliation complete with status {Status}",
            region.Name,
            isComplete ? "Current" : "Incomplete");
    }

    private async Task<bool> ReconcileRepositoryAsync(
        RegionDefinition region,
        DirectoryInfo regionRoot,
        int repositoryId,
        Repository repository,
        CancellationToken cancellationToken)
    {
        var state = await db.InstallationStates
            .SingleOrDefaultAsync(item => item.RepositoryId == repositoryId, cancellationToken);
        if (state is null)
        {
            state = new XivInstallationState { RepositoryId = repositoryId };
            db.InstallationStates.Add(state);
        }

        var chain = await _chainResolver.ResolveAsync(repositoryId, cancellationToken: cancellationToken);
        if (chain is null || chain.Count == 0)
        {
            SetStatus(state, InstallationStatus.Incomplete, "No installable patch chain is available.");
            await db.SaveChangesAsync(cancellationToken);
            Log.Warning("Region {Region} repository {RepositoryId} has no installable patch chain",
                region.Name, repositoryId);
            return false;
        }

        BootstrapFromVersionFile(state, chain, repository, regionRoot);

        var startIndex = 0;
        if (state.LastAppliedPatchId is not null)
        {
            var installedIndex = FindPatchIndex(chain, state.LastAppliedPatchId.Value);
            if (installedIndex < 0)
            {
                SetStatus(
                    state,
                    InstallationStatus.NeedsRebuild,
                    $"Applied patch {state.LastAppliedPatchId} is not present in the active chain.");
                await db.SaveChangesAsync(cancellationToken);
                return false;
            }

            startIndex = installedIndex + 1;
        }

        for (var index = startIndex; index < chain.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var patch = chain[index];
            var patchFile = GetPatchFile(patch);

            if (!patchFile.Exists || (patch.Size > 0 && patchFile.Length != patch.Size))
            {
                var actualSize = patchFile.Exists ? patchFile.Length : 0;
                SetStatus(
                    state,
                    InstallationStatus.Pending,
                    $"Patch file is unavailable or incomplete: {patchFile.FullName} ({actualSize}/{patch.Size} bytes).");
                await db.SaveChangesAsync(cancellationToken);
                return false;
            }

            state.Status = InstallationStatus.Installing;
            state.LastAttemptedAtUtc = DateTime.UtcNow;
            state.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await patchApplicationService.ApplyAsync(
                    patchFile,
                    new DirectoryInfo(Path.Combine(regionRoot.FullName, "game")),
                    cancellationToken);

                repository.SetVer(regionRoot, patch.RepoVersion.VersionString);
                state.LastAppliedPatchId = patch.Id;
                state.InstalledVersion = patch.RepoVersion.VersionString;
                state.Status = InstallationStatus.Installing;
                state.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus(state, InstallationStatus.Failed, LimitError(ex.ToString()));
                await db.SaveChangesAsync(cancellationToken);
                Log.Error(ex, "Failed installing patch {PatchId} for region {Region}", patch.Id, region.Name);
                return false;
            }
        }

        state.Status = InstallationStatus.Current;
        state.LastCompletedAtUtc = DateTime.UtcNow;
        state.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private FileInfo GetPatchFile(XivPatch patch) =>
        new(Path.Combine(_patchRoot, patch.LocalStoragePath));

    private static void BootstrapFromVersionFile(
        XivInstallationState state,
        IReadOnlyList<XivPatch> chain,
        Repository repository,
        DirectoryInfo regionRoot)
    {
        if (state.LastAppliedPatchId is not null)
        {
            return;
        }

        var installedVersion = repository.GetVer(regionRoot);
        if (repository.IsBaseVer(regionRoot))
        {
            return;
        }

        var installedPatch = chain.LastOrDefault(patch =>
            string.Equals(patch.RepoVersion.VersionString, installedVersion, StringComparison.Ordinal));
        if (installedPatch is null)
        {
            return;
        }

        state.LastAppliedPatchId = installedPatch.Id;
        state.InstalledVersion = installedVersion;
    }

    private static int FindPatchIndex(IReadOnlyList<XivPatch> patches, int patchId)
    {
        for (var index = 0; index < patches.Count; index++)
        {
            if (patches[index].Id == patchId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void SetStatus(
        XivInstallationState state,
        InstallationStatus status,
        string error)
    {
        state.Status = status;
        state.LastError = LimitError(error);
    }

    private static string LimitError(string error) =>
        error.Length <= 2000 ? error : error[..2000];

    private sealed record RegionDefinition(string Name, int GameRepositoryId, string DirectoryName);
}
