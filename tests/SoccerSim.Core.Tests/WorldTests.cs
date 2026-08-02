using SoccerSim.Core.LifeSim;
using SoccerSim.Core.World;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Covers the test world and the venue gating that binds it to the life simulation.
///
/// <para>
/// These tests deliberately assert on the <i>shape</i> of the world rather than on its contents:
/// the test world is scaffolding to be replaced by the City Searcher import, so pinning "Verith has
/// a gym" would just be a test to delete later. What must survive the swap is the id scheme, the
/// hierarchy, and the fact that activities are gated by venue category — so that is what is pinned.
/// </para>
/// </summary>
public sealed class WorldTests
{
    private static readonly IWorldGazetteer World = new TestWorldGazetteer();

    // ── Shape that must survive the real-world import ───────────────────────────────────

    [Fact]
    public void LocationIds_FollowTheGazetteerScheme()
    {
        // Dotted path down to the city, then '#' and a flat slug for anything inside it — the exact
        // convention the City Searcher tool emits, so ids (and the loc keys built from them) stay
        // stable when the real world lands.
        foreach (WorldLocation location in World.All)
        {
            Assert.DoesNotContain(' ', location.Id);
            Assert.Equal(location.Id.ToLowerInvariant(), location.Id);
            if (location.Kind != LocationKind.City)
                Assert.Contains('#', location.Id);
        }
    }

    [Fact]
    public void EveryNonRootNode_HasAParentThatExists()
    {
        foreach (WorldLocation location in World.All.Where(l => l.ParentId is not null))
            Assert.NotNull(World.Find(location.ParentId!));
    }

    [Fact]
    public void HomeLocation_ExistsAndIsAHome()
    {
        WorldLocation? home = World.Find(World.HomeLocationId);

        Assert.NotNull(home);
        Assert.Equal(VenueCategory.Home, home!.Category);
        Assert.Equal(0, home.TravelMinutes); // travel is measured FROM here
    }

    [Fact]
    public void HierarchyNodes_AreNotVenues()
    {
        foreach (WorldLocation location in World.All.Where(l => l.Kind != LocationKind.Venue))
            Assert.False(location.IsVenue);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull() => Assert.Null(World.Find("br.sudeste.rj.rio-de-janeiro"));

    [Fact]
    public void ChildrenOf_ReturnsDirectDescendantsOnly()
    {
        IReadOnlyList<WorldLocation> districts = World.ChildrenOf(TestWorldGazetteer.CityId);

        Assert.NotEmpty(districts);
        Assert.All(districts, district => Assert.Equal(LocationKind.District, district.Kind));
    }

    // ── The join with the life simulation ───────────────────────────────────────────────

    [Fact]
    public void EveryVenueCategoryUsedByAnActivity_ExistsSomewhereInTheWorld()
    {
        // An activity gated on a category no venue provides is unreachable — a content bug that is
        // invisible until a player wonders why they can never eat.
        IEnumerable<VenueCategory> required = LifeActivityCatalogue.All
            .Select(activity => activity.RequiredVenue)
            .Where(category => category != VenueCategory.None)
            .Distinct();

        foreach (VenueCategory category in required)
            Assert.NotEmpty(World.VenuesOfCategory(category));
    }

    [Fact]
    public void EveryNeed_CanBeRaisedSomewhere()
    {
        // The strongest content check available: if a need has no activity that increases it, the
        // player is being asked to manage a gauge they cannot service. Hygiene was exactly that gap
        // when it was first added.
        foreach (NeedKind need in Needs.All)
        {
            bool raisable = LifeActivityCatalogue.All
                .Any(activity => activity.NeedDeltas.Any(delta => delta.Need == need && delta.Delta > 0));

            Assert.True(raisable, $"No activity in the catalogue raises '{need}'.");
        }
    }

    [Fact]
    public void AvailableAt_FiltersByVenueAndRole()
    {
        WorldLocation home = World.Find(TestWorldGazetteer.ApartmentId)!;
        WorldLocation gym = World.VenuesOfCategory(VenueCategory.Gym).Single();

        IReadOnlyList<string> atHome = LifeActivityCatalogue
            .AvailableAt(CareerRole.Player, home).Select(a => a.Key).ToArray();
        IReadOnlyList<string> atGym = LifeActivityCatalogue
            .AvailableAt(CareerRole.Player, gym).Select(a => a.Key).ToArray();

        Assert.Contains("sleep", atHome);
        Assert.Contains("shower", atHome);
        Assert.DoesNotContain("gym_session", atHome);
        Assert.Contains("gym_session", atGym);
        Assert.DoesNotContain("sleep", atGym);
    }

    [Fact]
    public void AvailableAt_StillHonoursRoleExclusivity()
    {
        WorldLocation gym = World.VenuesOfCategory(VenueCategory.Gym).Single();

        // A manager standing in the gym gets nothing: gym_session is player-only.
        Assert.Empty(LifeActivityCatalogue.AvailableAt(CareerRole.Manager, gym));
        Assert.NotEmpty(LifeActivityCatalogue.AvailableAt(CareerRole.Player, gym));
    }

    [Fact]
    public void BothRoles_CanServiceEveryNeedSomewhere()
    {
        // The shared spine has to be genuinely shared: neither career may be left with a need it
        // has no way to raise.
        foreach (CareerRole role in Enum.GetValues<CareerRole>())
        {
            foreach (NeedKind need in Needs.All)
            {
                bool raisable = LifeActivityCatalogue.For(role)
                    .Any(activity => activity.NeedDeltas.Any(d => d.Need == need && d.Delta > 0));

                Assert.True(raisable, $"A {role} career cannot raise '{need}'.");
            }
        }
    }

    [Fact]
    public void UngatedActivity_CanBePerformedAnywhere()
    {
        var anywhere = new LifeActivity("stretch", 0.5, [new NeedDelta(NeedKind.MuscleCondition, +5)]);

        Assert.All(World.All, location => Assert.True(anywhere.CanBePerformedAt(location)));
    }
}
