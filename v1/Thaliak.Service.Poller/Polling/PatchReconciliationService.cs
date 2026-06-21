using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Download;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Patch;

namespace Thaliak.Service.Poller.Polling;

public class PatchReconciliationService(ThaliakContext db, PatchAlertQueueService alertQueueService)
{
    public async Task ReconcileAsync(XivRepository repo, PatchListEntry[] remotePatches,
        PatchDiscoveryType discoveryType = PatchDiscoveryType.Offered, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expansions = db.ExpansionRepositoryMappings
            .Include(erp => erp.ExpansionRepository)
            .Include(erp => erp.GameRepository)
            .Where(erp => erp.GameRepositoryId == repo.Id)
            .ToList();

        db.Repositories.Attach(repo);
        db.Repositories.AttachRange(expansions.Select(erp => erp.ExpansionRepository));

        var repoIds = new[] {repo.Id}.Union(expansions.Select(erp => erp.ExpansionRepositoryId)).ToArray();
        var localPatches = db.Patches
            .Include(p => p.RepoVersion)
            .Where(p => repoIds.Contains(p.RepoVersion.RepositoryId));

        var newPatchList = new List<XivPatch>();
        foreach (var remotePatch in remotePatches) {
            var effectiveRepoId = GetEffectiveRepositoryId(expansions, repo.Id, remotePatch.Url);
            var localPatch = localPatches.FirstOrDefault(p =>
                p.RepoVersion.VersionString == remotePatch.VersionId && p.RepoVersion.RepositoryId == effectiveRepoId);
            if (localPatch == null) {
                var newPatch = RecordNewPatchData(now, effectiveRepoId, remotePatch, discoveryType);
                newPatchList.Add(newPatch);
            } else {
                var alert = localPatch.FirstOffered == null &&
                            discoveryType == PatchDiscoveryType.Offered;
                UpdateExistingPatchData(now, localPatch, remotePatch, discoveryType);
                if (discoveryType == PatchDiscoveryType.Offered) {
                    DownloaderService.AddToQueue(new DownloadJob(localPatch));
                }

                if (alert) {
                    newPatchList.Add(localPatch);
                }
            }

            db.SaveChanges();
        }

        foreach (var repoId in repoIds) {
            var expansionPatches = remotePatches.Where(p =>
                GetEffectiveRepositoryId(expansions, repo.Id, p.Url) == repoId);
            RecordUpgradePathData(now, repoId, expansionPatches);
        }

        foreach (var repoId in repoIds) {
            RecordActiveStatus(now, repoId);
        }

        if (newPatchList.Count < 1) {
            return;
        }

        var newPatchIds = newPatchList.Select(p => p.Id).ToArray();
        var alertPatches = db.Patches
            .Include(p => p.RepoVersion)
            .ThenInclude(rv => rv.Repository)
            .Where(p => newPatchIds.Contains(p.Id))
            .ToList();

        await alertQueueService.QueueEligiblePatchesAsync(alertPatches, discoveryType, now, cancellationToken);
    }

    private void RecordUpgradePathData(DateTime now, int effectiveRepoId, IEnumerable<PatchListEntry> remotePatches)
    {
        Log.Information("Logging upgrade path data for repo {repoId}", effectiveRepoId);

        remotePatches = remotePatches.OrderBy(p => p.VersionId);
        var recordedPaths = new HashSet<(int RepoVersionId, int? PreviousRepoVersionId)>();

        PatchListEntry? previousPatch = null;
        foreach (var remotePatch in remotePatches) {
            var dbVersions = db.RepoVersions
                .Where(rv => rv.RepositoryId == effectiveRepoId)
                .Where(rv => rv.VersionString == remotePatch.VersionId ||
                            (previousPatch != null && rv.VersionString == previousPatch.VersionId))
                .ToList();

            var dbVersion = dbVersions.FirstOrDefault(rv => rv.VersionString == remotePatch.VersionId);
            if (dbVersion == null) {
                Log.Error("Could not find version in DB: {0}. Backing out of upgrade path recording.",
                    remotePatch.VersionId);
                return;
            }

            int? previousRepoVersionId = null;
            if (previousPatch != null) {
                var dbPreviousVersion = dbVersions.FirstOrDefault(rv => rv.VersionString == previousPatch.VersionId);
                if (dbPreviousVersion == null) {
                    Log.Error("Could not find previous version in DB: {0}. Backing out of upgrade path recording.",
                        previousPatch.VersionId);
                    return;
                }

                previousRepoVersionId = dbPreviousVersion.Id;
            }

            var pathKey = (dbVersion.Id, previousRepoVersionId);
            if (!recordedPaths.Add(pathKey)) {
                previousPatch = remotePatch;
                continue;
            }

            var path = db.UpgradePaths.Local.FirstOrDefault(p =>
                           p.RepoVersionId == dbVersion.Id && p.PreviousRepoVersionId == previousRepoVersionId) ??
                       db.UpgradePaths.FirstOrDefault(p =>
                           p.RepoVersionId == dbVersion.Id && p.PreviousRepoVersionId == previousRepoVersionId);
            if (path is null) {
                db.UpgradePaths.Add(new XivUpgradePath
                {
                    RepositoryId = effectiveRepoId,
                    FirstOffered = now,
                    LastOffered = now,
                    IsActive = true,
                    RepoVersionId = dbVersion.Id,
                    PreviousRepoVersionId = previousRepoVersionId
                });
            } else {
                path.LastOffered = now;
                path.IsActive = true;
            }

            previousPatch = remotePatch;
        }

        db.SaveChanges();

        Log.Information("Successfully logged upgrade path data for repo {repoId}", effectiveRepoId);
    }

    private void RecordActiveStatus(DateTime now, int effectiveRepoId)
    {
        var patches = db.Patches
            .Include(p => p.RepoVersion)
            .Where(p => p.RepoVersion.RepositoryId == effectiveRepoId)
            .Where(p => p.LastOffered < now)
            .Where(p => p.IsActive)
            .ToList();
        foreach (var item in patches) {
            item.IsActive = false;
        }

        var upgradePaths = db.UpgradePaths.Where(p => p.RepositoryId == effectiveRepoId)
            .Where(p => p.LastOffered < now)
            .Where(p => p.IsActive)
            .ToList();
        foreach (var item in upgradePaths) {
            item.IsActive = false;
        }

        db.SaveChanges();
    }

    private XivPatch RecordNewPatchData(DateTime now, int effectiveRepoId, PatchListEntry remotePatch,
        PatchDiscoveryType discoveryType)
    {
        Log.Information("Discovered new patch: {@0}", remotePatch);

        var version = db.RepoVersions.FirstOrDefault(v =>
            v.VersionString == remotePatch.VersionId && v.RepositoryId == effectiveRepoId);
        if (version == null) {
            version = new XivRepoVersion
            {
                VersionString = remotePatch.VersionId,
                RepositoryId = effectiveRepoId
            };
        } else {
            db.RepoVersions.Attach(version);
        }

        var newPatch = new XivPatch
        {
            RepoVersion = version,
            RemoteOriginPath = remotePatch.Url,
            Size = remotePatch.Length,
        };

        if (discoveryType == PatchDiscoveryType.Offered) {
            newPatch.FirstOffered = now;
            newPatch.LastOffered = now;
            newPatch.IsActive = true;

            SetLauncherPatchMetadata(newPatch, remotePatch);
        }

        newPatch.FirstSeen = now;
        newPatch.LastSeen = now;

        db.Patches.Add(newPatch);

        DownloaderService.AddToQueue(new DownloadJob(newPatch));

        return newPatch;
    }

    private void UpdateExistingPatchData(DateTime now, XivPatch localPatch, PatchListEntry remotePatch,
        PatchDiscoveryType discoveryType)
    {
        Log.Verbose("Patch already present: {@0}", remotePatch);

        localPatch.LastSeen = now;
        if (discoveryType == PatchDiscoveryType.Offered) {
            localPatch.IsActive = true;
            localPatch.LastOffered = now;

            if (localPatch.FirstOffered == null) {
                localPatch.FirstOffered = now;
                SetLauncherPatchMetadata(localPatch, remotePatch);
            }
        }

        db.Patches.Update(localPatch);
    }

    private void SetLauncherPatchMetadata(XivPatch localPatch, PatchListEntry remotePatch)
    {
        localPatch.Size = remotePatch.Length;
        localPatch.HashType = remotePatch.Url == remotePatch.HashType ? null : remotePatch.HashType;
        localPatch.HashBlockSize = remotePatch.HashBlockSize == 0 ? null : remotePatch.HashBlockSize;
        localPatch.Hashes = remotePatch.Hashes;
    }

    private int GetEffectiveRepositoryId(List<XivExpansionRepositoryMapping> expansions, int repositoryId,
        string patchUrl)
    {
        var expansionId = XivExpansionRepositoryMapping.GetExpansionId(patchUrl);
        if (expansionId == 0) {
            return repositoryId;
        }

        foreach (var erp in expansions) {
            if (erp.ExpansionId == expansionId) {
                return erp.ExpansionRepositoryId;
            }
        }

        throw new InvalidDataException($"Unknown expansion ID {expansionId} for repository ID {repositoryId}!");
    }
}
