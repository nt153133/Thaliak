using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Exceptions;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;

namespace Thaliak.Service.Poller.Polling.Sqex;

public sealed class GlobalExpansionSweepCoordinator(
    ThaliakContext db,
    ISqexLauncherClient launcherClient,
    SqexAccountProvider accountProvider,
    PatchReconciliationService reconciliationService,
    LodestoneMaintenanceService maintenanceService,
    ExpansionSweepManualArmStore manualArmStore,
    IExpansionSweepFailureNotifier failureNotifier,
    IOptions<GlobalExpansionSweepOptions> options,
    TimeProvider timeProvider)
{
    public async Task TryRunAsync(
        XivRepository gameRepository,
        DirectoryInfo gameDirectory,
        IReadOnlyCollection<XivPatch> newlyOfferedPatches,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await MarkInterruptedAttemptsFailedAsync(now, cancellationToken);
        await RecordAutomaticTriggerAsync(newlyOfferedPatches, now, cancellationToken);

        var manualTriggerKey = manualArmStore.ReadTriggerKey();
        if (manualTriggerKey is not null) {
            manualArmStore.Consume();
        }

        var attempt = manualTriggerKey is not null
            ? await CreateManualAttemptAsync(manualTriggerKey, now, cancellationToken)
            : await GetPendingAutomaticAttemptAsync(now, cancellationToken);
        if (attempt is null) {
            return;
        }

        attempt.Status = ExpansionSweepStatus.Running;
        attempt.StartedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        Log.Information(
            "Starting {Trigger} Global expansion sweep for base version {BaseVersion}",
            attempt.Trigger,
            attempt.TriggerRepoVersion.VersionString);

        try {
            var account = await accountProvider.GetOptionalAsync(
                XivAccountPurpose.Expansion,
                cancellationToken);
            if (account is null) {
                await FailAsync(attempt, attempt.TriggerRepoVersion.VersionString,
                    "No expansion account is configured.", now, cancellationToken);
                return;
            }

            var loginResult = await launcherClient.LoginAsync(
                account,
                gameDirectory,
                forceBaseVersion: true,
                cancellationToken);
            attempt.MaxExpansion = loginResult.OauthLogin.MaxExpansion;

            if (loginResult.OauthLogin.MaxExpansion < options.Value.RequiredMaxExpansion) {
                await FailAsync(attempt, attempt.TriggerRepoVersion.VersionString,
                    $"Expansion account entitlement is ex{loginResult.OauthLogin.MaxExpansion}; ex{options.Value.RequiredMaxExpansion} is required.",
                    now,
                    cancellationToken);
                return;
            }

            if (loginResult.State != LoginState.NeedsPatchGame) {
                await FailAsync(attempt, attempt.TriggerRepoVersion.VersionString,
                    $"Launcher returned {loginResult.State} instead of a patch list.",
                    now,
                    cancellationToken);
                return;
            }

            await reconciliationService.ReconcileAsync(
                gameRepository,
                loginResult.PendingPatches,
                cancellationToken: cancellationToken);

            attempt.DiscoveredPatchCount = loginResult.PendingPatches.Length;
            attempt.Status = ExpansionSweepStatus.Succeeded;
            attempt.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            attempt.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            Log.Information(
                "Global expansion sweep succeeded for {BaseVersion}: max expansion {MaxExpansion}, {PatchCount} patches discovered",
                attempt.TriggerRepoVersion.VersionString,
                attempt.MaxExpansion,
                attempt.DiscoveredPatchCount);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            var reason = GetSafeFailureReason(ex);
            Log.Warning(
                "Global expansion sweep failed for {BaseVersion}: {Reason} ({ErrorType})",
                attempt.TriggerRepoVersion.VersionString,
                reason,
                ex.GetType().Name);
            await FailAsync(attempt, attempt.TriggerRepoVersion.VersionString, reason,
                timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
    }

    private async Task RecordAutomaticTriggerAsync(
        IReadOnlyCollection<XivPatch> newlyOfferedPatches,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var patch = newlyOfferedPatches
            .Where(candidate => candidate.RepoVersion.RepositoryId == SqexPollerService.GameRepoId)
            .OrderByDescending(candidate => candidate.FirstOffered)
            .FirstOrDefault();
        if (patch is null) {
            return;
        }

        var triggerKey = $"automatic:{patch.RepoVersionId}";
        if (await db.ExpansionSweepAttempts
                .AsNoTracking()
                .AnyAsync(attempt => attempt.TriggerKey == triggerKey, cancellationToken)) {
            return;
        }

        var stalePending = await db.ExpansionSweepAttempts
            .Where(attempt => attempt.Trigger == ExpansionSweepTrigger.Automatic)
            .Where(attempt => attempt.Status == ExpansionSweepStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var pending in stalePending) {
            pending.Status = ExpansionSweepStatus.Failed;
            pending.CompletedAtUtc = now;
            pending.LastError = "Superseded by a newer Global base patch.";
        }

        db.ExpansionSweepAttempts.Add(new XivExpansionSweepAttempt
        {
            TriggerKey = triggerKey,
            TriggerRepoVersionId = patch.RepoVersionId,
            Trigger = ExpansionSweepTrigger.Automatic,
            Status = ExpansionSweepStatus.Pending,
            DetectedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        Log.Information("Queued automatic Global expansion sweep for base version {BaseVersion}",
            patch.RepoVersion.VersionString);
    }

    private async Task<XivExpansionSweepAttempt?> CreateManualAttemptAsync(
        string triggerKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await db.ExpansionSweepAttempts
            .AsNoTracking()
            .AnyAsync(attempt => attempt.TriggerKey == triggerKey, cancellationToken);
        if (existing) {
            return null;
        }

        var latestVersion = await db.Patches
            .Where(patch => patch.RepoVersion.RepositoryId == SqexPollerService.GameRepoId)
            .Where(patch => patch.FirstOffered != null)
            .OrderByDescending(patch => patch.FirstOffered)
            .Select(patch => patch.RepoVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestVersion is null) {
            return null;
        }

        var pendingAutomatic = await db.ExpansionSweepAttempts
            .Where(attempt => attempt.Trigger == ExpansionSweepTrigger.Automatic)
            .Where(attempt => attempt.Status == ExpansionSweepStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var pending in pendingAutomatic) {
            pending.Status = ExpansionSweepStatus.Failed;
            pending.CompletedAtUtc = now;
            pending.LastError = "Superseded by a manual expansion sweep.";
        }

        var attempt = new XivExpansionSweepAttempt
        {
            TriggerKey = triggerKey,
            TriggerRepoVersionId = latestVersion.Id,
            TriggerRepoVersion = latestVersion,
            Trigger = ExpansionSweepTrigger.Manual,
            Status = ExpansionSweepStatus.Pending,
            DetectedAtUtc = now
        };
        db.ExpansionSweepAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    private Task<XivExpansionSweepAttempt?> GetPendingAutomaticAttemptAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (maintenanceService.GetMaintenanceAt(now) is null) {
            return Task.FromResult<XivExpansionSweepAttempt?>(null);
        }

        return db.ExpansionSweepAttempts
            .Include(attempt => attempt.TriggerRepoVersion)
            .Where(attempt => attempt.Trigger == ExpansionSweepTrigger.Automatic)
            .Where(attempt => attempt.Status == ExpansionSweepStatus.Pending)
            .OrderBy(attempt => attempt.DetectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task MarkInterruptedAttemptsFailedAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var interrupted = await db.ExpansionSweepAttempts
            .Where(attempt => attempt.Status == ExpansionSweepStatus.Running)
            .ToListAsync(cancellationToken);
        if (interrupted.Count == 0) {
            return;
        }

        foreach (var attempt in interrupted) {
            attempt.Status = ExpansionSweepStatus.Failed;
            attempt.CompletedAtUtc = now;
            attempt.LastError = "Interrupted before completion; manual re-arm required.";
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FailAsync(
        XivExpansionSweepAttempt attempt,
        string triggerVersion,
        string reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        attempt.Status = ExpansionSweepStatus.Failed;
        attempt.CompletedAtUtc = failedAtUtc;
        attempt.LastError = reason.Length <= 256 ? reason : reason[..256];
        await db.SaveChangesAsync(cancellationToken);
        await failureNotifier.SendFailureAsync(triggerVersion, attempt.LastError, failedAtUtc, cancellationToken);
    }

    private static string GetSafeFailureReason(Exception exception)
    {
        return exception switch
        {
            OauthLoginException => "Square Enix rejected the expansion account login.",
            InvalidResponseException => "Square Enix returned an unexpected launcher response.",
            HttpRequestException => "The Square Enix launcher request failed.",
            _ => $"Unexpected {exception.GetType().Name}."
        };
    }
}
