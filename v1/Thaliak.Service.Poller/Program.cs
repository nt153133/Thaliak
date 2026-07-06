using Downloader;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using Serilog.Events;
using Thaliak.Common.Database;
using Thaliak.Service.Poller.Download;
using Thaliak.Service.Poller.Installation;
using Thaliak.Service.Poller.Notifications;
using Thaliak.Service.Poller.Patch;
using Thaliak.Service.Poller.Polling;
using Thaliak.Service.Poller.Polling.Actoz;
using Thaliak.Service.Poller.Polling.Shanda;
using Thaliak.Service.Poller.Polling.Sqex;
using Thaliak.Service.Poller.Polling.Sqex.Lodestone.Maintenance;
using Thaliak.Service.Poller.Polling.TraditionalChinese;
using Thaliak.Service.Poller.Util;
using Thaliak.Service.Poller.Webhooks;

// set up logging
using var log = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .CreateLogger();
Log.Logger = log;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton(_ => new DownloadService(new DownloadConfiguration
        {
            ParallelDownload = true,
            BufferBlockSize = 8000,
            ChunkCount = 8,
            MaxTryAgainOnFailover = 10,
            OnTheFlyDownload = false,
            Timeout = 10000,
            TempDirectory = Path.GetTempPath(),
            RequestConfiguration = new RequestConfiguration
            {
                UserAgent = "FFXIV PATCH CLIENT",
                Accept = "*/*"
            }
        }));
        services.AddHostedService<DownloaderService>();
        services.AddHostedService<PatchAlertDispatcherHostedService>();
        services.Configure<InstallationOptions>(
            ctx.Configuration.GetSection(InstallationOptions.SectionName));
        services.AddSingleton<InstallationSignal>();
        services.AddSingleton<IPatchApplicationService, PatchApplicationService>();
        services.AddScoped<RegionalInstallationService>();
        services.AddHostedService<RegionalInstallationCoordinator>();

        services.AddScoped<LodestoneMaintenanceService>();
        services.AddScoped<TraditionalChineseMaintenanceService>();
        services.AddScoped<PollingScheduleService>();
        services.AddScoped<PatchReconciliationService>();
        services.AddScoped<PatchAlertQueueService>();
        services.AddScoped<PatchAlertDispatchService>();
        services.AddScoped<PatchAlertNotificationService>();
        services.AddScoped<IPatchDiscordAlertSender, DiscordWebhookPatchAlertSender>();

        services.AddScoped<SqexFutureScraperService>();

        services.AddScoped<SqexPollerService>();
        services.AddScoped<ActozPollerService>();
        services.AddScoped<ShandaPollerService>();
        services.AddScoped<TraditionalChinesePollerService>();
        services.AddScoped<JsonWebhookService>();

        services.AddScoped<HttpClient>(_ =>
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
            };
            return new HttpClient(handler);
        });

        // set up the db context
        services.AddDbContext<ThaliakContext>(o =>
        {
            var connectionString = ctx.Configuration.GetConnectionString("sqlite") ?? "Data Source=/data/thaliak.db";
            EnsureSqliteDirectoryExists(connectionString);

            o.UseSqlite(connectionString,
                co => co.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention();
            o.LogTo(Log.Verbose);
        });

        if (PollingConfiguration.ShouldRegisterPolling(ctx.Configuration))
        {
            services.AddQuartz(q =>
            {
                q.UseMicrosoftDependencyInjectionJobFactory();

                q.AddPollJob<LodestoneMaintenancePollJob, LodestoneMaintenanceService>();
                q.AddPollJob<TraditionalChineseMaintenancePollJob, TraditionalChineseMaintenanceService>();

                // start patch pollers at a slight delay to allow maintenance pollers to work first
                var delayedPatchStart = DateTime.UtcNow.AddSeconds(30);
                q.AddPollJob<SqexLoginPollJob, SqexPollerService>(delayedPatchStart);
                q.AddPollJob<SqexFutureScrapeJob, SqexFutureScraperService>(delayedPatchStart);

                // KR/CN/TC can start instantly
                if (PollingConfiguration.ShouldRegisterKoreaChecks(ctx.Configuration)) {
                    q.AddPollJob<ActozPatchListPollJob, ActozPollerService>(delayedPatchStart);
                } else {
                    Log.Information("Korean patch checks disabled by {ConfigKey}", PollingConfiguration.DisableKoreaChecksKey);
                }

                q.AddPollJob<ShandaPatchListPollJob, ShandaPollerService>(delayedPatchStart);
                q.AddPollJob<TraditionalChinesePatchListPollJob, TraditionalChinesePollerService>(delayedPatchStart);
            });

            services.AddQuartzHostedService(o => { o.WaitForJobsToComplete = true; });
        }
        else
        {
            Log.Information("Polling disabled by {ConfigKey}", PollingConfiguration.EnabledKey);
        }
    })
    .UseSerilog()
    .Build();

// apply migrations on boot
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ThaliakContext>();
    db.Database.Migrate();
}

// go!
await host.RunAsync();

static void EnsureSqliteDirectoryExists(string connectionString)
{
    var builder = new SqliteConnectionStringBuilder(connectionString);
    var dataSource = builder.DataSource;
    if (dataSource is null || string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:") {
        return;
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (directory is not null) {
        Directory.CreateDirectory(directory);
    }
}
