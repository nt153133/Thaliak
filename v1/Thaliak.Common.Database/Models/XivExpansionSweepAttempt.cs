using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Thaliak.Common.Database.Models;

[Index(nameof(TriggerKey), IsUnique = true)]
[Index(nameof(Status))]
public sealed class XivExpansionSweepAttempt
{
    [Key]
    public int Id { get; set; }

    public string TriggerKey { get; set; } = string.Empty;

    public int TriggerRepoVersionId { get; set; }

    public XivRepoVersion TriggerRepoVersion { get; set; } = null!;

    public ExpansionSweepTrigger Trigger { get; set; }

    public ExpansionSweepStatus Status { get; set; } = ExpansionSweepStatus.Pending;

    public DateTime DetectedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int? MaxExpansion { get; set; }

    public int? DiscoveredPatchCount { get; set; }

    public string? LastError { get; set; }
}
