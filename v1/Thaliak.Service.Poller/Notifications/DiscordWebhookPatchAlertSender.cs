using Discord;
using Discord.Webhook;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;

namespace Thaliak.Service.Poller.Notifications;

public sealed class DiscordWebhookPatchAlertSender(ThaliakContext db) : IPatchDiscordAlertSender
{
    public async Task SendBatchAsync(string region, IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime quietWindowEndedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var discordHooks = await db.DiscordHooks.ToListAsync(cancellationToken);
        if (discordHooks.Count == 0 || patches.Count == 0) {
            return;
        }

        foreach (var hookEntry in discordHooks) {
            Log.Information("Sending batched Discord patch alert to webhook: {@hookEntry}", hookEntry);

            try {
                var hookClient = new DiscordWebhookClient(hookEntry.Url);
                await hookClient.SendMessageAsync(
                    "",
                    false,
                    [BuildEmbed(region, patches, discoveryType, quietWindowEndedAtUtc)],
                    "Thaliak",
                    "https://thaliak.xiv.dev/logo512.png");
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                Log.Warning(ex, "Error calling Discord webhook");
            }
        }
    }

    private static Embed BuildEmbed(string region, IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime quietWindowEndedAtUtc)
    {
        var color = discoveryType == PatchDiscoveryType.Offered ? Color.Green : Color.LightOrange;
        var title = discoveryType == PatchDiscoveryType.Offered
            ? $"New FFXIV {region} patches offered by launcher"
            : $"New FFXIV {region} patches seen on patch server";

        var orderedPatches = patches
            .OrderBy(p => p.RepoVersion.Repository.Name)
            .ThenBy(p => p.RepoVersion.VersionString)
            .ToArray();

        var repositories = string.Join("\n", orderedPatches.Select(p =>
            $"{p.RepoVersion.Repository.Name} ({p.RepoVersion.Repository.Slug})"));
        var versions = string.Join("\n", orderedPatches.Select(p => p.RepoVersion.VersionString).Distinct());
        var urls = string.Join("\n", orderedPatches.Select(p => p.RemoteOriginPath));

        return new EmbedBuilder
        {
            Color = color,
            Title = title,
            Timestamp = quietWindowEndedAtUtc,
            Fields =
            [
                new EmbedFieldBuilder {Name = "Region", Value = region},
                new EmbedFieldBuilder {Name = "Patch Count", Value = patches.Count.ToString()},
                new EmbedFieldBuilder {Name = "Versions", Value = TruncateEmbedValue(versions)},
                new EmbedFieldBuilder {Name = "Repositories", Value = TruncateEmbedValue(repositories)},
                new EmbedFieldBuilder {Name = "URLs", Value = TruncateEmbedValue(urls)}
            ],
            Footer = new EmbedFooterBuilder {Text = "thaliak.xiv.dev"}
        }.Build();
    }

    private static string TruncateEmbedValue(string value)
    {
        return value.Length <= 1024 ? value : value[..1020] + "...";
    }
}
