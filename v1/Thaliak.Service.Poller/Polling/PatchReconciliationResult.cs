using Thaliak.Common.Database.Models;

namespace Thaliak.Service.Poller.Polling;

public sealed record PatchReconciliationResult(IReadOnlyCollection<XivPatch> NewlyOfferedPatches)
{
    public static PatchReconciliationResult Empty { get; } = new([]);
}
