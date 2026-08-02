using SoccerSim.Core.Localization;

namespace SoccerSim.Core.World;

/// <summary>
/// Where a location sits in the world hierarchy. Mirrors the levels used by the City Searcher
/// world-editor tool (<c>region → state → subregion → city → district → venue</c>) so the real
/// gazetteer can be imported later without reshaping anything that depends on this.
/// </summary>
public enum LocationKind
{
    Region,
    State,
    Subregion,
    City,
    District,
    Venue,
}

/// <summary>
/// What a venue is <i>for</i>, in gameplay terms. Distinct from <see cref="LocationKind"/>, which is
/// only about hierarchy depth.
///
/// <para>
/// This is the join between the world and the life simulation: a <see cref="LifeSim.LifeActivity"/>
/// declares the category it needs, and any venue of that category can host it. That is what lets an
/// imported world work immediately — mapping a gazetteer entry's free-text type onto one of these
/// is the whole integration, rather than re-authoring activities per location.
/// </para>
/// </summary>
public enum VenueCategory
{
    /// <summary>Not a venue, or a venue with no life-sim function (hierarchy nodes use this).</summary>
    None,

    Home,
    TrainingGround,
    Stadium,
    Gym,
    Medical,
    Dining,
    Social,
    Commerce,
    Transit,
    Media,
}

/// <summary>
/// A single node of the world.
///
/// <para>
/// <see cref="Id"/> follows the City Searcher scheme exactly: a dotted path down to the city, then
/// <c>#</c> and a flat slug for anything inside it — <c>br.sudeste.rj.rio-de-janeiro#lapa</c>. Ids
/// are stable and are what the localisation keys are built from, so importing a real world does not
/// invalidate a save or a translation.
/// </para>
///
/// <para>
/// Note what is NOT here: the flavourful, location-specific actions the gazetteer attaches to a
/// venue ("Tuesday event night", "wish-ribbon quest"). Those are events and quests, not need
/// activities, and they arrive with the world import on their own seam. This type carries only what
/// the life simulation needs to answer "can I do that here, and how long does getting there cost".
/// </para>
/// </summary>
public sealed record WorldLocation(string Id, LocationKind Kind)
{
    /// <summary>Parent node id, or null for a root.</summary>
    public string? ParentId { get; init; }

    /// <summary>What the venue is for. <see cref="VenueCategory.None"/> for hierarchy nodes.</summary>
    public VenueCategory Category { get; init; } = VenueCategory.None;

    /// <summary>
    /// In-game minutes to reach this venue from the human's home. A flat cost for now; the real
    /// gazetteer carries geometry that will replace it with a routed distance.
    /// </summary>
    public double TravelMinutes { get; init; }

    /// <summary>Localisation key for the display name; derived from <see cref="Id"/>.</summary>
    public string NameKey => LocKeys.LocationName(Id);

    /// <summary>Localisation key for the flavour text.</summary>
    public string DescriptionKey => LocKeys.LocationDescription(Id);

    /// <summary>True when this node can host life activities.</summary>
    public bool IsVenue => Kind == LocationKind.Venue && Category != VenueCategory.None;
}
