using SoccerSim.Core.World;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 9: moving to a descendant is refused, deleting a node with children or
/// clubs is refused, and creating a child inherits the next kind.
/// </summary>
public sealed class GeoTreeTests
{
    private static readonly Lazy<WorldSnapshot> Source = new(() => WorldJsonReader.Read(WorldFixture.Json));

    private static IReadOnlyList<GeoNode> Nodes => Source.Value.GeoNodes;

    private static IReadOnlyList<ClubIdentity> Clubs => Source.Value.Clubs;

    /// <summary>A city with clubs in it, and the region above it.</summary>
    private static (string City, string Region) ACityWithClubs()
    {
        ClubIdentity club = Clubs[0];
        GeoNode city = Nodes.First(node => node.GeoNodeId == club.Geography.GeoNodeId);
        return (city.GeoNodeId, city.ParentId!);
    }

    // ----------------------------------------------------------------- create

    [Theory]
    [InlineData(GeoNodeKind.World, GeoNodeKind.Confederation)]
    [InlineData(GeoNodeKind.Confederation, GeoNodeKind.Country)]
    [InlineData(GeoNodeKind.SubRegion, GeoNodeKind.Country)]
    [InlineData(GeoNodeKind.Country, GeoNodeKind.Region)]
    [InlineData(GeoNodeKind.Region, GeoNodeKind.City)]
    public void AChildTakesTheNextKindInTheHierarchy(GeoNodeKind parent, GeoNodeKind child) =>
        Assert.Equal(child, GeoTree.ChildKindOf(parent));

    [Fact]
    public void ACityIsALeaf() =>
        Assert.Throws<GeoTreeException>(() => GeoTree.ChildKindOf(GeoNodeKind.City));

    [Fact]
    public void CreatingAChild_AddsItUnderTheParentWithTheRightKind()
    {
        string countryId = Nodes.First(node => node.Kind == GeoNodeKind.Country).GeoNodeId;

        IReadOnlyList<GeoNode> after = GeoTree.AddChild(Nodes, countryId, "geo_new_region", "Região Nova");

        GeoNode created = after.Single(node => node.GeoNodeId == "geo_new_region");
        Assert.Equal(GeoNodeKind.Region, created.Kind);
        Assert.Equal(countryId, created.ParentId);
        Assert.Equal("Região Nova", created.DisplayName);
        Assert.Equal(Nodes.Count + 1, after.Count);
    }

    [Fact]
    public void CreatingAChildWithAnIdThatExists_IsRefused() =>
        Assert.Throws<GeoTreeException>(() => GeoTree.AddChild(Nodes, "geo_bra", Nodes[0].GeoNodeId, "Duplicado"));

    [Fact]
    public void AChildNeedsAName() =>
        Assert.Throws<GeoTreeException>(() => GeoTree.AddChild(Nodes, "geo_bra", "geo_x", "   "));

    // ------------------------------------------------------------------- move

    /// <summary>
    /// The rule that matters: a node moved inside its own subtree becomes its own ancestor, and
    /// every walk upward — the club page's breadcrumb, for one — stops terminating.
    /// </summary>
    [Fact]
    public void MovingANodeIntoItsOwnSubtree_IsRefused()
    {
        (string city, string region) = ACityWithClubs();

        var exception = Assert.Throws<GeoTreeException>(() => GeoTree.Move(Nodes, region, city));

        Assert.Contains("ancestral de si mesmo", exception.Message);
    }

    [Fact]
    public void MovingANodeOntoItself_IsRefused()
    {
        (string city, _) = ACityWithClubs();

        Assert.Throws<GeoTreeException>(() => GeoTree.Move(Nodes, city, city));
    }

    [Fact]
    public void MovingANodeUnderTheWrongKind_IsRefused()
    {
        (string city, _) = ACityWithClubs();

        // A city belongs under a region, not under the world.
        var exception = Assert.Throws<GeoTreeException>(() => GeoTree.Move(Nodes, city, "geo_world"));

        Assert.Contains("City", exception.Message);
        Assert.Contains("World", exception.Message);
    }

    [Fact]
    public void AValidMove_Reparents()
    {
        (string city, string region) = ACityWithClubs();
        string otherRegion = Nodes.First(node => node.Kind == GeoNodeKind.Region && node.GeoNodeId != region).GeoNodeId;

        IReadOnlyList<GeoNode> after = GeoTree.Move(Nodes, city, otherRegion);

        Assert.Equal(otherRegion, after.Single(node => node.GeoNodeId == city).ParentId);
        Assert.Equal(Nodes.Count, after.Count);
    }

    /// <summary>The select the screen builds omits the impossible targets, so the rule and the
    /// control cannot disagree.</summary>
    [Fact]
    public void TheValidParents_ExcludeTheSubtreeAndTheWrongKinds()
    {
        (string city, string region) = ACityWithClubs();

        var parents = GeoTree.ValidParentsFor(Nodes, city);

        Assert.All(parents, parent => Assert.Equal(GeoNodeKind.Region, parent.Kind));
        Assert.DoesNotContain(parents, parent => parent.GeoNodeId == city);
        Assert.Contains(parents, parent => parent.GeoNodeId == region);
    }

    // ----------------------------------------------------------------- delete

    [Fact]
    public void DeletingANodeWithChildren_IsRefusedWithTheCount()
    {
        var exception = Assert.Throws<GeoTreeException>(() => GeoTree.Delete(Nodes, Clubs, "geo_bra"));

        Assert.Contains("abaixo", exception.Message);
        Assert.Contains("filhos", exception.Message);
    }

    [Fact]
    public void DeletingANodeWithClubs_IsRefusedAndNamesThem()
    {
        (string city, _) = ACityWithClubs();

        var exception = Assert.Throws<GeoTreeException>(() => GeoTree.Delete(Nodes, Clubs, city));

        // The alternative is a silent delete that turns into one GEO_DANGLING finding per club.
        Assert.Contains("clube(s)", exception.Message);
        Assert.Contains(Clubs[0].Identity.ShortName, exception.Message);
    }

    [Fact]
    public void DeletingAnEmptyLeaf_Works()
    {
        string countryId = Nodes.First(node => node.Kind == GeoNodeKind.Country).GeoNodeId;
        IReadOnlyList<GeoNode> with = GeoTree.AddChild(Nodes, countryId, "geo_spare", "Região Vazia");

        IReadOnlyList<GeoNode> after = GeoTree.Delete(with, Clubs, "geo_spare");

        Assert.Equal(Nodes.Count, after.Count);
        Assert.DoesNotContain(after, node => node.GeoNodeId == "geo_spare");
    }

    [Fact]
    public void EveryOperationRefusesANodeThatIsNotThere()
    {
        Assert.Throws<GeoTreeException>(() => GeoTree.Rename(Nodes, "geo_nowhere", "x"));
        Assert.Throws<GeoTreeException>(() => GeoTree.Move(Nodes, "geo_nowhere", "geo_bra"));
        Assert.Throws<GeoTreeException>(() => GeoTree.Delete(Nodes, Clubs, "geo_nowhere"));
        Assert.Throws<GeoTreeException>(() => GeoTree.AddChild(Nodes, "geo_nowhere", "geo_x", "x"));
    }

    [Fact]
    public void RenamingKeepsEverythingElse()
    {
        (string city, _) = ACityWithClubs();
        GeoNode before = Nodes.Single(node => node.GeoNodeId == city);

        GeoNode after = GeoTree.Rename(Nodes, city, "  Cidade Renomeada  ").Single(node => node.GeoNodeId == city);

        Assert.Equal("Cidade Renomeada", after.DisplayName);
        Assert.Equal(before with { DisplayName = after.DisplayName }, after);
    }
}
