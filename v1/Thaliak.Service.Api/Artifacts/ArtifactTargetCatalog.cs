namespace Thaliak.Service.Api.Artifacts;

public static class ArtifactTargetCatalog
{
    private static readonly ArtifactTarget[] Targets =
    [
        new("global", "4e9a232b", "game", "FFXIV Global base game", []),
        new("global", "6b936f08", "ex1", "FFXIV Global Heavensward", []),
        new("global", "f29a3eb2", "ex2", "FFXIV Global Stormblood", []),
        new("global", "859d0e24", "ex3", "FFXIV Global Shadowbringers", []),
        new("global", "1bf99b87", "ex4", "FFXIV Global Endwalker", []),
        new("global", "6cfeab11", "ex5", "FFXIV Global Dawntrail", []),

        new("china", "c38effbc", "game", "FFXIV China base game", []),
        new("china", "77420d17", "ex1", "FFXIV China Heavensward", []),
        new("china", "ee4b5cad", "ex2", "FFXIV China Stormblood", []),
        new("china", "994c6c3b", "ex3", "FFXIV China Shadowbringers", []),
        new("china", "0728f998", "ex4", "FFXIV China Endwalker", []),
        new("china", "702fc90e", "ex5", "FFXIV China Dawntrail", []),

        new("tc", "961a4536", "game", "FFXIV Traditional Chinese base game", ["TC"]),
        new("tc", "e6dea8a0", "ex1", "FFXIV Traditional Chinese Heavensward", []),
        new("tc", "7fd7f91a", "ex2", "FFXIV Traditional Chinese Stormblood", []),
        new("tc", "08d0c98c", "ex3", "FFXIV Traditional Chinese Shadowbringers", []),
        new("tc", "96b45c2f", "ex4", "FFXIV Traditional Chinese Endwalker", []),
        new("tc", "e1b36cb9", "ex5", "FFXIV Traditional Chinese Dawntrail", [])
    ];

    public static IReadOnlyList<ArtifactTarget> All => Targets;

    public static IReadOnlyList<string> Regions { get; } = Targets
        .Select(target => target.Region)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<ArtifactTarget> ForRegion(string region) =>
        Targets
            .Where(target => string.Equals(target.Region, region, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static ArtifactTarget? FindBySlugOrAlias(string slugOrAlias) =>
        Targets.FirstOrDefault(target =>
            string.Equals(target.RepositorySlug, slugOrAlias, StringComparison.OrdinalIgnoreCase)
            || target.Aliases.Any(alias => string.Equals(alias, slugOrAlias, StringComparison.OrdinalIgnoreCase)));

    public static string ResolveSlugOrAlias(string slugOrAlias) =>
        FindBySlugOrAlias(slugOrAlias)?.RepositorySlug ?? slugOrAlias;
}
