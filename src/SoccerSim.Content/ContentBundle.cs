using SoccerSim.Content.Model;

namespace SoccerSim.Content;

/// <summary>
/// A complete authored world in memory: the catalogues, the clubs and people, and the initial
/// world state a new career starts from.
///
/// This is the single unit the whole pipeline moves around — the authoring tool produces one,
/// the validator checks one, the exporter writes one to JSON, and the importer writes one into
/// SQLite.
/// </summary>
public sealed record ContentBundle
{
    public ContentManifest Manifest { get; init; } = new();

    public IReadOnlyList<ContentTrait> Traits { get; init; } = [];

    public IReadOnlyList<ContentLeague> Leagues { get; init; } = [];

    public IReadOnlyList<ContentTeam> Teams { get; init; } = [];

    public IReadOnlyList<ContentPlayer> Players { get; init; } = [];

    public IReadOnlyList<ContentHousingItem> HousingItems { get; init; } = [];

    public ContentWorld World { get; init; } = new();

    /// <summary>Category name → row count. Feeds the manifest and the boot log.</summary>
    public IReadOnlyDictionary<string, int> CountByCategory() => new Dictionary<string, int>
    {
        [ContentCategory.Traits] = Traits.Count,
        [ContentCategory.Leagues] = Leagues.Count,
        [ContentCategory.Teams] = Teams.Count,
        [ContentCategory.Players] = Players.Count,
        [ContentCategory.HousingItems] = HousingItems.Count,
        [ContentCategory.Seasons] = World.Seasons.Count,
        [ContentCategory.Fixtures] = World.Fixtures.Count,
    };
}

/// <summary>Category names, used as JSON filenames, API route segments and validation scopes.</summary>
public static class ContentCategory
{
    public const string Traits = "traits";
    public const string Leagues = "leagues";
    public const string Teams = "teams";
    public const string Players = "players";
    public const string HousingItems = "housing_items";
    public const string Seasons = "seasons";
    public const string Fixtures = "fixtures";
    public const string World = "world";

    public static readonly IReadOnlyList<string> All =
    [
        Traits, Leagues, Teams, Players, HousingItems, Seasons, Fixtures,
    ];
}
