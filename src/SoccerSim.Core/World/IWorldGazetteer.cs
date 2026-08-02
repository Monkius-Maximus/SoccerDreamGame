namespace SoccerSim.Core.World;

/// <summary>
/// Read access to the world's locations.
///
/// <para>
/// Deliberately an interface with a throwaway implementation behind it
/// (<see cref="TestWorldGazetteer"/>). The real world is authored in the City Searcher tool and
/// lands in a later sprint; everything built against this port — travel, venue-gated activities, the
/// phone's map app — keeps working when the import replaces the test world, because none of it ever
/// referenced a hardcoded place.
/// </para>
/// </summary>
public interface IWorldGazetteer
{
    /// <summary>Every node, in a stable order.</summary>
    IReadOnlyList<WorldLocation> All { get; }

    /// <summary>
    /// Where the human's career is based. Travel costs are measured from here and it is the default
    /// venue when nothing else is selected.
    /// </summary>
    string HomeLocationId { get; }

    /// <summary>The node with this id, or null when unknown.</summary>
    WorldLocation? Find(string id);

    /// <summary>Direct children of a node; pass null for the roots.</summary>
    IReadOnlyList<WorldLocation> ChildrenOf(string? parentId);

    /// <summary>Every venue of a given category, in <see cref="All"/> order.</summary>
    IReadOnlyList<WorldLocation> VenuesOfCategory(VenueCategory category);
}
