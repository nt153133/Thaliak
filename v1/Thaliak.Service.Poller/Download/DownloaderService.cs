using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Downloader;
using Serilog;
using Thaliak.Service.Poller.Installation;

namespace Thaliak.Service.Poller.Download;

public class DownloaderService : BackgroundService
{
    private static readonly Channel<DownloadJob> PendingJobs = Channel.CreateUnbounded<DownloadJob>();
    private readonly DownloadService _downloadService;
    private readonly string _downloadPath;
    private readonly InstallationSignal _installationSignal;

    public DownloaderService(
        DownloadService downloadService,
        IConfiguration config,
        InstallationSignal installationSignal)
    {
        _downloadService = downloadService;
        _installationSignal = installationSignal;
        _downloadPath = Path.GetFullPath(config.GetValue<string>("Directories:Patches"));
        Directory.CreateDirectory(_downloadPath);

        _downloadService.DownloadFileCompleted += (sender, args) =>
        {
            var url = string.Empty;
            if (args.UserState is DownloadPackage pkg)
            {
                url = pkg.Address;
            }

            if (args.Error != null)
            {
                Log.Error("Download failed for {0}: {1}", url, args.Error);
                return;
            }

            Log.Information("Download complete for {0}", url);
        };
    }

    public static void AddToQueue(DownloadJob job)
    {
        var enableDownloads = Environment.GetEnvironmentVariable("ENABLE_DOWNLOADS");
        if (string.IsNullOrEmpty(enableDownloads) || !enableDownloads.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("Skipping download queue (ENABLE_DOWNLOADS not set to 'true'): {0}", job.Url);
            return;
        }

        Log.Information("Adding to download queue: {0}", job.Url);
        PendingJobs.Writer.WriteAsync(job);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in PendingJobs.Reader.ReadAllAsync(stoppingToken))
        {
            var dest = Path.Join(_downloadPath, job.Destination);
            if (File.Exists(dest))
            {
                Log.Information("Skipping download of {0} as it already exists locally at {1}", job.Url, dest);
                _installationSignal.Notify();
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            Log.Information("Starting download of URL {0} to {1}", job.Url, dest);
            await _downloadService.DownloadFileTaskAsync(job.Url, dest);
            if (File.Exists(dest))
            {
                _installationSignal.Notify();
            }
        }
    }
}
