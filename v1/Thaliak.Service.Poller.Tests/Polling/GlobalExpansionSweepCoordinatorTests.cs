using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Polling;
using Thaliak.Service.Poller.Polling.Sqex;
using Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Polling;

public sealed class GlobalExpansionSweepCoordinatorTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "thaliak-tests", Guid.NewGuid().ToString("N"));

    private ThaliakContext _db = null!;
    private FakeLauncherClient _launcher = null!;
    private RecordingFailureNotifier _notifier = null!;
    private GlobalExpansionSweepCoordinator _coordinator = null!;
    private MutableTimeProvider _timeProvider = null!;
    private XivRepository _gameRepository = null!;
    private DirectoryInfo _gameDirectory = null!;
    private string _armPath = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _gameDirectory = Directory.CreateDirectory(Path.Combine(_root, "game"));
        _armPath = Path.Combine(_root, "control", "expansion.arm");

        var databasePath = Path.Combine(_root, "thaliak.db");
        _db = new ThaliakContext(
            new DbContextOptionsBuilder<ThaliakContext>()
                .UseSqlite($"Data Source={databasePath}")
                .UseSnakeCaseNamingConvention()
                .Options);
        await _db.Database.MigrateAsync();
        _gameRepository = await _db.Repositories.SingleAsync(
            repository => repository.Id == SqexPollerService.GameRepoId);
        _launcher = new FakeLauncherClient();
        _notifier = new RecordingFailureNotifier();

        var configuration = new ConfigurationBuilder().Build();
        var reconciliation = new PatchReconciliationService(
            _db,
            new PatchAlertQueueService(_db, configuration));
        var options = Options.Create(new GlobalExpansionSweepOptions
        {
            Enabled = true,
            RequiredMaxExpansion = 5,
            ManualArmPath = _armPath
        });
        _timeProvider = new MutableTimeProvider(Now);
        _coordinator = new GlobalExpansionSweepCoordinator(
            _db,
            _launcher,
            new SqexAccountProvider(_db),
            reconciliation,
            new LodestoneMaintenanceService(new HttpClient()),
            new ExpansionSweepManualArmStore(options),
            _notifier,
            options,
            _timeProvider);

        LodestoneMaintenanceService.MaintenanceList.Clear();
    }

    public async Task DisposeAsync()
    {
        LodestoneMaintenanceService.MaintenanceList.Clear();
        await _db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TryRunAsync_NewBasePatchOutsideMaintenance_DoesNotLogin()
    {
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        LodestoneMaintenanceService.MaintenanceList.Add(new MaintenanceInfo(
            Now.UtcDateTime.AddMinutes(30),
            Now.UtcDateTime.AddHours(2),
            "Upcoming All Worlds Maintenance"));

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);

        Assert.Equal(0, _launcher.LoginCount);
        Assert.Equal(ExpansionSweepStatus.Pending,
            (await _db.ExpansionSweepAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task TryRunAsync_PendingBasePatchWhenMaintenanceStarts_LogsIn()
    {
        await AddExpansionAccountAsync();
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        LodestoneMaintenanceService.MaintenanceList.Add(new MaintenanceInfo(
            Now.UtcDateTime.AddMinutes(30),
            Now.UtcDateTime.AddHours(2),
            "Upcoming All Worlds Maintenance"));
        _launcher.Result = CreateSuccessfulLogin(CreatePatchEntry(5, "2026.07.26.0000.0000"));

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);
        _timeProvider.SetUtcNow(Now.AddMinutes(40));
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(1, _launcher.LoginCount);
        Assert.Equal(ExpansionSweepStatus.Succeeded,
            (await _db.ExpansionSweepAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task TryRunAsync_ActiveMaintenanceWithoutNewBasePatch_DoesNotLogin()
    {
        AddActiveMaintenance();

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(0, _launcher.LoginCount);
        Assert.Empty(await _db.ExpansionSweepAttempts.ToListAsync());
    }

    [Fact]
    public async Task TryRunAsync_NewBasePatchDuringMaintenance_LogsInOnlyOnce()
    {
        await AddExpansionAccountAsync();
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        AddActiveMaintenance();
        _launcher.Result = CreateSuccessfulLogin(
            CreatePatchEntry(0, "2026.07.26.0000.0000"),
            CreatePatchEntry(1, "2026.07.26.0000.0000"),
            CreatePatchEntry(2, "2026.07.26.0000.0000"),
            CreatePatchEntry(3, "2026.07.26.0000.0000"),
            CreatePatchEntry(4, "2026.07.26.0000.0000"),
            CreatePatchEntry(5, "2026.07.26.0000.0000"));

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);

        Assert.Equal(1, _launcher.LoginCount);
        var attempt = await _db.ExpansionSweepAttempts.SingleAsync();
        Assert.Equal(ExpansionSweepStatus.Succeeded, attempt.Status);
        Assert.Equal(5, attempt.MaxExpansion);
        Assert.Equal(6, attempt.DiscoveredPatchCount);
        var repositoryIds = await _db.Patches
            .Where(patch => patch.RepoVersion.VersionString == "2026.07.26.0000.0000")
            .Select(patch => patch.RepoVersion.RepositoryId)
            .Distinct()
            .Order()
            .ToListAsync();
        Assert.Equal([2, 3, 4, 5, 6, 17], repositoryIds);
    }

    [Fact]
    public async Task TryRunAsync_ManualArmOutsideMaintenance_RunsAndConsumesRequest()
    {
        await AddExpansionAccountAsync();
        await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        Directory.CreateDirectory(Path.GetDirectoryName(_armPath)!);
        await File.WriteAllTextAsync(_armPath, $"{Guid.NewGuid()}\n");
        _launcher.Result = CreateSuccessfulLogin(CreatePatchEntry(5, "2026.07.26.0000.0000"));

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(1, _launcher.LoginCount);
        Assert.False(File.Exists(_armPath));
        Assert.Equal(ExpansionSweepTrigger.Manual,
            (await _db.ExpansionSweepAttempts.SingleAsync()).Trigger);
    }

    [Fact]
    public async Task TryRunAsync_ManualArmWithoutOfferedBaseVersion_IsConsumedOnce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_armPath)!);
        await File.WriteAllTextAsync(_armPath, $"{Guid.NewGuid()}\n");

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.False(File.Exists(_armPath));
        Assert.Empty(await _db.ExpansionSweepAttempts.ToListAsync());
        Assert.Equal(0, _launcher.LoginCount);
    }

    [Fact]
    public async Task TryRunAsync_InsufficientEntitlement_FailsWithoutAutomaticRetry()
    {
        await AddExpansionAccountAsync();
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        AddActiveMaintenance();
        _launcher.Result = CreateSuccessfulLogin(maxExpansion: 3);

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);

        Assert.Equal(1, _launcher.LoginCount);
        var attempt = await _db.ExpansionSweepAttempts.SingleAsync();
        Assert.Equal(ExpansionSweepStatus.Failed, attempt.Status);
        Assert.Contains("ex3", attempt.LastError);
        Assert.Single(_notifier.Failures);
    }

    [Fact]
    public async Task TryRunAsync_FailedAutomaticAttempt_CanBeRetriedByManualArm()
    {
        await AddExpansionAccountAsync();
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        AddActiveMaintenance();
        _launcher.Result = CreateSuccessfulLogin(maxExpansion: 3);
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, [basePatch]);

        Directory.CreateDirectory(Path.GetDirectoryName(_armPath)!);
        await File.WriteAllTextAsync(_armPath, $"{Guid.NewGuid()}\n");
        _launcher.Result = CreateSuccessfulLogin(CreatePatchEntry(5, "2026.07.26.0000.0000"));
        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(2, _launcher.LoginCount);
        var attempts = await _db.ExpansionSweepAttempts.OrderBy(attempt => attempt.Id).ToListAsync();
        Assert.Equal(
            [ExpansionSweepStatus.Failed, ExpansionSweepStatus.Succeeded],
            attempts.Select(attempt => attempt.Status));
    }

    [Fact]
    public async Task TryRunAsync_MissingExpansionAccount_FailsAndConsumesManualRequest()
    {
        await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        Directory.CreateDirectory(Path.GetDirectoryName(_armPath)!);
        await File.WriteAllTextAsync(_armPath, $"{Guid.NewGuid()}\n");

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(0, _launcher.LoginCount);
        Assert.False(File.Exists(_armPath));
        Assert.Equal(ExpansionSweepStatus.Failed,
            (await _db.ExpansionSweepAttempts.SingleAsync()).Status);
        Assert.Single(_notifier.Failures);
    }

    [Fact]
    public async Task TryRunAsync_InterruptedAttempt_IsFailedWithoutLogin()
    {
        var basePatch = await AddOfferedBasePatchAsync("2026.07.26.0000.0000");
        _db.ExpansionSweepAttempts.Add(new XivExpansionSweepAttempt
        {
            TriggerKey = $"automatic:{basePatch.RepoVersionId}",
            TriggerRepoVersionId = basePatch.RepoVersionId,
            Trigger = ExpansionSweepTrigger.Automatic,
            Status = ExpansionSweepStatus.Running,
            DetectedAtUtc = Now.UtcDateTime,
            StartedAtUtc = Now.UtcDateTime
        });
        await _db.SaveChangesAsync();

        await _coordinator.TryRunAsync(_gameRepository, _gameDirectory, []);

        Assert.Equal(0, _launcher.LoginCount);
        var attempt = await _db.ExpansionSweepAttempts.SingleAsync();
        Assert.Equal(ExpansionSweepStatus.Failed, attempt.Status);
        Assert.Contains("Interrupted", attempt.LastError);
    }

    private async Task<XivPatch> AddOfferedBasePatchAsync(string version)
    {
        var repoVersion = new XivRepoVersion
        {
            RepositoryId = SqexPollerService.GameRepoId,
            VersionString = version
        };
        var patch = new XivPatch
        {
            RepoVersion = repoVersion,
            RemoteOriginPath = $"https://example.test/game/base/{version}.patch",
            Size = 1024,
            FirstSeen = Now.UtcDateTime,
            LastSeen = Now.UtcDateTime,
            FirstOffered = Now.UtcDateTime,
            LastOffered = Now.UtcDateTime,
            IsActive = true
        };
        _db.Patches.Add(patch);
        await _db.SaveChangesAsync();
        return patch;
    }

    private async Task AddExpansionAccountAsync()
    {
        _db.Accounts.Add(new XivAccount
        {
            Purpose = XivAccountPurpose.Expansion,
            Username = "full",
            Password = "secret",
            ApplicableRepositories = []
        });
        await _db.SaveChangesAsync();
    }

    private static LoginResult CreateSuccessfulLogin(params PatchListEntry[] patches)
    {
        return CreateSuccessfulLogin(5, patches);
    }

    private static LoginResult CreateSuccessfulLogin(int maxExpansion, params PatchListEntry[] patches)
    {
        return new LoginResult
        {
            State = LoginState.NeedsPatchGame,
            OauthLogin = new OauthLoginResult
            {
                Playable = true,
                TermsAccepted = true,
                MaxExpansion = maxExpansion
            },
            PendingPatches = patches
        };
    }

    private static PatchListEntry CreatePatchEntry(int expansion, string version)
    {
        var repository = expansion == 0 ? "base" : $"ex{expansion}";
        return new PatchListEntry
        {
            VersionId = version,
            HashType = "sha1",
            Url = $"https://example.test/game/{repository}/{version}.patch",
            Length = 1024,
            Hashes = []
        };
    }

    private static void AddActiveMaintenance()
    {
        LodestoneMaintenanceService.MaintenanceList.Add(new MaintenanceInfo(
            Now.UtcDateTime.AddMinutes(-10),
            Now.UtcDateTime.AddHours(1),
            "All Worlds Maintenance"));
    }

    private sealed class FakeLauncherClient : ISqexLauncherClient
    {
        public int LoginCount { get; private set; }
        public LoginResult Result { get; set; } = CreateSuccessfulLogin();

        public Task<PatchListEntry[]> CheckBootVersionAsync(
            DirectoryInfo? gamePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<PatchListEntry>());
        }

        public Task<LoginResult> LoginAsync(
            XivAccount account,
            DirectoryInfo gamePath,
            bool forceBaseVersion,
            CancellationToken cancellationToken = default)
        {
            LoginCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingFailureNotifier : IExpansionSweepFailureNotifier
    {
        public List<(string Version, string Reason)> Failures { get; } = [];

        public Task SendFailureAsync(
            string triggerVersion,
            string reason,
            DateTime failedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Failures.Add((triggerVersion, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            _now = value;
        }
    }
}
