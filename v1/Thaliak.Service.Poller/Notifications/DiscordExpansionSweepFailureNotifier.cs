using Discord;
using Discord.Webhook;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Thaliak.Common.Database;

namespace Thaliak.Service.Poller.Notifications;

public sealed class DiscordExpansionSweepFailureNotifier(ThaliakContext db) : IExpansionSweepFailureNotifier
{
    public async Task SendFailureAsync(
        string triggerVersion,
        string reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var hooks = await db.DiscordHooks.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var hook in hooks) {
            try {
                using var client = new DiscordWebhookClient(hook.Url);
                await client.SendMessageAsync(
                    embeds:
                    [
                        new EmbedBuilder
                        {
                            Color = Color.Red,
                            Title = "Global expansion patch discovery failed",
                            Timestamp = failedAtUtc,
                            Fields =
                            [
                                new EmbedFieldBuilder {Name = "Base Version", Value = triggerVersion},
                                new EmbedFieldBuilder {Name = "Reason", Value = reason}
                            ],
                            Footer = new EmbedFooterBuilder {Text = "Manual re-arm required"}
                        }.Build()
                    ],
                    username: "Thaliak");
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                Log.Warning("Could not send expansion sweep failure to Discord webhook {WebhookId}: {ErrorType}",
                    hook.Id, ex.GetType().Name);
            }
        }
    }
}
