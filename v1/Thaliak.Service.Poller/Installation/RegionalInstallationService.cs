using System.Diagnostics;
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
        var stopwatch = Stopwatch.StartNew();
        var regionRoot = new DirectoryInfo(Path.Combine(installationRoot.FullName, region.DirectoryName));
        regionRoot.Create();

        var mappings = await db.ExpansionRepositoryMappings
            .AsNoTracking()
            .Where(mapping => mapping.GameRepositoryId == region.GameRepositoryId)
            .OrderBy(mapping => mapping.ExpansionId)
            .ToListAsync(cancellationToken);

        var isComplete = true;
        var appliedPatchCount = 0;
        long appliedPatchBytes = 0;
        foreach (var mapping in mappings)
        {
            var repository = mapping.ExpansionId == 0
                ? Repository.Ffxiv
                : (Repository)((int)Repository.Ex1 + mapping.ExpansionId - 1);

            var result = await ReconcileRepositoryAsync(
                region,
                regionRoot,
                mapping.ExpansionRepositoryId,
                repository,
                cancellationToken);

            appliedPatchCount += result.AppliedPatchCount;
            appliedPatchBytes += result.AppliedPatchBytes;
            if (!result.IsComplete)
            {
                isComplete = false;
            }
        }

        stopwatch.Stop();
        Log.Information(
            "[INSTALL-TIMING] Region {Region} completed with status {Status} in {Elapsed}; " +
            "applied {PatchCount} patches ({PatchBytes} bytes)",
            region.Name,
            isComplete ? "Current" : "Incomplete",
            stopwatch.Elapsed,
            appliedPatchCount,
            appliedPatchBytes);
    }

    private async Task<RepositoryReconciliationResult> ReconcileRepositoryAsync(
        RegionDefinition region,
        DirectoryInfo regionRoot,
        int repositoryId,
        Repository repository,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var appliedPatchCount = 0;
        long appliedPatchBytes = 0;
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
            return FinishRepository(
                region.Name,
                repositoryId,
                isComplete: false,
                appliedPatchCount,
                appliedPatchBytes,
                stopwatch);
        }

        Log.Information(
            "Region {Region} repository {RepositoryId} planned {PatchCount} patches " +
            "from {FirstVersion} through {LastVersion} ({PatchBytes} bytes)",
            region.Name,
            repositoryId,
            chain.Count,
            chain[0].RepoVersion.VersionString,
            chain[^1].RepoVersion.VersionString,
            chain.Sum(patch => patch.Size));

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
                return FinishRepository(
                    region.Name,
                    repositoryId,
                    isComplete: false,
                    appliedPatchCount,
                    appliedPatchBytes,
                    stopwatch);
            }

            startIndex = installedIndex + 1;
        }

        var unavailablePatch = chain
            .Skip(startIndex)
            .Select(patch => (Patch: patch, File: GetPatchFile(patch)))
            .FirstOrDefault(item =>
                !item.File.Exists || (item.Patch.Size > 0 && item.File.Length != item.Patch.Size));
        if (unavailablePatch.Patch is not null)
        {
            var actualSize = unavailablePatch.File.Exists ? unavailablePatch.File.Length : 0;
            SetStatus(
                state,
                InstallationStatus.Pending,
                $"Patch file is unavailable or incomplete: {unavailablePatch.File.FullName} " +
                $"({actualSize}/{unavailablePatch.Patch.Size} bytes).");
            await db.SaveChangesAsync(cancellationToken);
            return FinishRepository(
                region.Name,
                repositoryId,
                isComplete: false,
                appliedPatchCount,
                appliedPatchBytes,
                stopwatch);
        }

        for (var index = startIndex; index < chain.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var patch = chain[index];
            var patchFile = GetPatchFile(patch);
            state.Status = InstallationStatus.Installing;
            state.LastAttemptedAtUtc = DateTime.UtcNow;
            state.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                var patchStopwatch = Stopwatch.StartNew();
                await patchApplicationService.ApplyAsync(
                    patchFile,
                    new DirectoryInfo(Path.Combine(regionRoot.FullName, "game")),
                    cancellationToken);
                patchStopwatch.Stop();
                appliedPatchCount++;
                appliedPatchBytes += patch.Size;
                Log.Information(
                    "[INSTALL-TIMING] Region {Region} repository {RepositoryId} patch {PatchId} " +
                    "({PatchBytes} bytes) applied in {Elapsed}",
                    region.Name,
                    repositoryId,
                    patch.Id,
                    patch.Size,
                    patchStopwatch.Elapsed);

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
                return FinishRepository(
                    region.Name,
                    repositoryId,
                    isComplete: false,
                    appliedPatchCount,
                    appliedPatchBytes,
                    stopwatch);
            }
        }

        state.Status = InstallationStatus.Current;
        state.LastCompletedAtUtc = DateTime.UtcNow;
        state.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return FinishRepository(
            region.Name,
            repositoryId,
            isComplete: true,
            appliedPatchCount,
            appliedPatchBytes,
            stopwatch);
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

    private static RepositoryReconciliationResult FinishRepository(
        string region,
        int repositoryId,
        bool isComplete,
        int appliedPatchCount,
        long appliedPatchBytes,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        Log.Information(
            "[INSTALL-TIMING] Region {Region} repository {RepositoryId} completed with status {Status} " +
            "in {Elapsed}; applied {PatchCount} patches ({PatchBytes} bytes)",
            region,
            repositoryId,
            isComplete ? "Current" : "Incomplete",
            stopwatch.Elapsed,
            appliedPatchCount,
            appliedPatchBytes);
        return new(isComplete, appliedPatchCount, appliedPatchBytes);
    }

    private sealed record RegionDefinition(string Name, int GameRepositoryId, string DirectoryName);

    private sealed record RepositoryReconciliationResult(
        bool IsComplete,
        int AppliedPatchCount,
        long AppliedPatchBytes);
}
