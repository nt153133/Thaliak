namespace Thaliak.Service.Api.Artifacts;

public sealed record ArtifactTarget(
    string Region,
    string RepositorySlug,
    string Expansion,
    string Description,
    IReadOnlyList<string> Aliases);
