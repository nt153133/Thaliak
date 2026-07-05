using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Thaliak.Common.Database.Models;

[Index(nameof(Status))]
public sealed class XivInstallationState
{
    [Key]
    public int RepositoryId { get; set; }

    public XivRepository Repository { get; set; } = null!;

    public int? LastAppliedPatchId { get; set; }

    public XivPatch? LastAppliedPatch { get; set; }

    public string? InstalledVersion { get; set; }

    public InstallationStatus Status { get; set; } = InstallationStatus.Pending;

    public DateTime? LastAttemptedAtUtc { get; set; }

    public DateTime? LastCompletedAtUtc { get; set; }

    public string? LastError { get; set; }
}
