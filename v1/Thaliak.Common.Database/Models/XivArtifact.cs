using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thaliak.Common.Database.Models;

[Index(nameof(Kind), nameof(RepositorySlug), nameof(VersionString), IsUnique = true)]
[Index(nameof(Kind), nameof(Region), nameof(VersionString))]
public sealed class XivArtifact
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string RepositorySlug { get; set; } = string.Empty;

    public string VersionString { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? Sha256 { get; set; }

    public string Status { get; set; } = "pending";

    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReadyAtUtc { get; set; }

    public DateTime? NotifiedAtUtc { get; set; }
}
