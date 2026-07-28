using Microsoft.Extensions.Options;

namespace Thaliak.Service.Api.Artifacts;

public sealed class ArtifactPathService(IOptions<ArtifactOptions> options)
{
    private readonly ArtifactOptions _options = options.Value;

    public string Root => Path.GetFullPath(_options.Root);

    public string GetRelativePath(string kind, string repositorySlug, string versionString) =>
        Path.Combine($"{kind}s", repositorySlug, $"{versionString}.{kind}").Replace('\\', '/');

    public string GetAbsolutePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public string GetAbsolutePath(string kind, string repositorySlug, string versionString) =>
        GetAbsolutePath(GetRelativePath(kind, repositorySlug, versionString));

    public bool IsUnderRoot(string absolutePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
