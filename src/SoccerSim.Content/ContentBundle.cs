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

    public IReadOnlyList<ContentNation> Nations { get; init; } = [];

    public IReadOnlyList<ContentStadium> Stadiums { get; init; } = [];

    public IReadOnlyList<ContentCompetition> Competitions { get; init; } = [];

    public IReadOnlyList<ContentTrait> Traits { get; init; } = [];

    /// <summary>Divisions. See <c>sql/0007_world_structure.sql</c> for the naming.</summary>
    public IReadOnlyList<ContentLeague> Leagues { get; init; } = [];

    public IReadOnlyList<ContentTeam> Teams { get; init; } = [];

    public IReadOnlyList<ContentPlayer> Players { get; init; } = [];

    public IReadOnlyList<ContentCoach> Coaches { get; init; } = [];

    public IReadOnlyList<ContentContract> Contracts { get; init; } = [];

    public IReadOnlyList<ContentHousingItem> HousingItems { get; init; } = [];

    public ContentWorld World { get; init; } = new();

    /// <summary>Category name → row count. Feeds the manifest and the boot log.</summary>
    public IReadOnlyDictionary<string, int> CountByCategory() => new Dictionary<string, int>
    {
        [ContentCategory.Nations] = Nations.Count,
        [ContentCategory.Stadiums] = Stadiums.Count,
        [ContentCategory.Competitions] = Competitions.Count,
        [ContentCategory.Traits] = Traits.Count,
        [ContentCategory.Leagues] = Leagues.Count,
        [ContentCategory.Teams] = Teams.Count,
        [ContentCategory.Players] = Players.Count,
        [ContentCategory.Coaches] = Coaches.Count,
        [ContentCategory.Contracts] = Contracts.Count,
        [ContentCategory.HousingItems] = HousingItems.Count,
        [ContentCategory.Seasons] = World.Seasons.Count,
        [ContentCategory.Fixtures] = World.Fixtures.Count,
    };
}

/// <summary>Category names, used as JSON filenames, API route segments and validation scopes.</summary>
public static class ContentCategory
{
    public const string Nations = "nations";
    public const string Stadiums = "stadiums";
    public const string Competitions = "competitions";
    public const string Traits = "traits";
    public const string Leagues = "leagues";
    public const string Teams = "teams";
    public const string Players = "players";
    public const string Coaches = "coaches";
    public const string Contracts = "contracts";
    public const string HousingItems = "housing_items";
    public const string Seasons = "seasons";
    public const string Fixtures = "fixtures";
    public const string World = "world";

    /// <summary>
    /// Every category that is its own JSON file, in dependency order — referenced categories
    /// come first. Import and hash both walk this list, so the order is load-bearing.
    /// </summary>
    public static readonly IReadOnlyList<string> Files =
    [
        Nations, Stadiums, Competitions, Traits, Leagues, Teams, Players,
        Coaches, Contracts, HousingItems, World,
    ];

    /// <summary>Categories a CRUD tool exposes as editable grids.</summary>
    public static readonly IReadOnlyList<string> Editable =
    [
        Nations, Stadiums, Competitions, Traits, Leagues, Teams, Players, Coaches, Contracts, HousingItems,
    ];
}
