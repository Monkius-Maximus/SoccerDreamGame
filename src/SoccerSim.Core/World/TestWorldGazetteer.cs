namespace SoccerSim.Core.World;

/// <summary>
/// A deliberately small, fictional world used to build and test the systems that depend on having
/// somewhere to be — venue-gated activities, travel time, the phone's map — before the real world
/// exists.
///
/// <para>
/// <b>This is scaffolding and is meant to be deleted.</b> The real world is authored in the City
/// Searcher tool and imported in a later sprint. What makes that merge orderly rather than a rewrite
/// is that this class is the ONLY place a location is named: it implements
/// <see cref="IWorldGazetteer"/>, uses the tool's id scheme and hierarchy levels, and everything
/// else in the codebase reaches the world through the port. Swapping the implementation is the
/// entire integration.
/// </para>
///
/// <para>
/// The city is fictional (Verith) on purpose — the same reason the club list is fictional. A test
/// fixture that names a real city invites real landmarks, and those come with rights.
/// </para>
/// </summary>
public sealed class TestWorldGazetteer : IWorldGazetteer
{
    /// <summary>Id of the test city. Follows the gazetteer's dotted-path scheme.</summary>
    public const string CityId = "test.verith";

    /// <summary>Where a test career lives; travel costs are measured from here.</summary>
    public const string ApartmentId = "test.verith#apartamento";

    private readonly Dictionary<string, WorldLocation> _byId;

    public TestWorldGazetteer()
    {
        All =
        [
            new WorldLocation(CityId, LocationKind.City),

            // ── Districts ───────────────────────────────────────────────────────────────
            new WorldLocation("test.verith#centro", LocationKind.District) { ParentId = CityId },
            new WorldLocation("test.verith#orla", LocationKind.District) { ParentId = CityId },
            new WorldLocation("test.verith#distrito-esportivo", LocationKind.District) { ParentId = CityId },

            // ── Venues: where the human lives ───────────────────────────────────────────
            new WorldLocation(ApartmentId, LocationKind.Venue)
            {
                ParentId = "test.verith#centro",
                Category = VenueCategory.Home,
                TravelMinutes = 0,
            },

            // ── Venues: the job ─────────────────────────────────────────────────────────
            new WorldLocation("test.verith#centro-de-treinamento", LocationKind.Venue)
            {
                ParentId = "test.verith#distrito-esportivo",
                Category = VenueCategory.TrainingGround,
                TravelMinutes = 25,
            },
            new WorldLocation("test.verith#estadio", LocationKind.Venue)
            {
                ParentId = "test.verith#distrito-esportivo",
                Category = VenueCategory.Stadium,
                TravelMinutes = 30,
            },
            new WorldLocation("test.verith#academia", LocationKind.Venue)
            {
                ParentId = "test.verith#distrito-esportivo",
                Category = VenueCategory.Gym,
                TravelMinutes = 20,
            },
            new WorldLocation("test.verith#departamento-medico", LocationKind.Venue)
            {
                ParentId = "test.verith#distrito-esportivo",
                Category = VenueCategory.Medical,
                TravelMinutes = 25,
            },

            // ── Venues: the rest of a life ──────────────────────────────────────────────
            new WorldLocation("test.verith#restaurante", LocationKind.Venue)
            {
                ParentId = "test.verith#centro",
                Category = VenueCategory.Dining,
                TravelMinutes = 10,
            },
            new WorldLocation("test.verith#bar-da-orla", LocationKind.Venue)
            {
                ParentId = "test.verith#orla",
                Category = VenueCategory.Social,
                TravelMinutes = 18,
            },
            new WorldLocation("test.verith#galeria", LocationKind.Venue)
            {
                ParentId = "test.verith#centro",
                Category = VenueCategory.Commerce,
                TravelMinutes = 12,
            },
            new WorldLocation("test.verith#centro-de-midia", LocationKind.Venue)
            {
                ParentId = "test.verith#centro",
                Category = VenueCategory.Media,
                TravelMinutes = 15,
            },
        ];

        _byId = All.ToDictionary(location => location.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<WorldLocation> All { get; }

    public string HomeLocationId => ApartmentId;

    public WorldLocation? Find(string id) =>
        _byId.TryGetValue(id, out WorldLocation? location) ? location : null;

    public IReadOnlyList<WorldLocation> ChildrenOf(string? parentId) =>
        All.Where(location => location.ParentId == parentId).ToArray();

    public IReadOnlyList<WorldLocation> VenuesOfCategory(VenueCategory category) =>
        All.Where(location => location.IsVenue && location.Category == category).ToArray();
}
