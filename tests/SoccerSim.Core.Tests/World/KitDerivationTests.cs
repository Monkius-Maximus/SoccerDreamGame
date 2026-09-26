using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The polarity inversion the club generator builds away kits with (ALGORITHMS.md §7.2), checked
/// against the twenty kits the pilot league was authored with.
/// </summary>
public sealed class KitDerivationTests
{
    private static readonly Lazy<WorldSnapshot> Pilot = new(() => WorldJsonReader.Read(WorldFixture.Json));

    [Fact]
    public void ThePilotAwayShirtsAndShorts_AllSitOnThePolarityPole()
    {
        Assert.All(Pilot.Value.Clubs, club =>
        {
            (string shirt, string shorts, _) = KitDerivation.AwayColours(club.Palette, club.Kits.Home.Shirt);
            Assert.Equal(shirt, club.Kits.Away.Shirt, ignoreCase: true);
            Assert.Equal(shorts, club.Kits.Away.Shorts, ignoreCase: true);
        });
    }

    [Fact]
    public void ThePilotAwaySocks_CarryTheHomeShirt_InEighteenOfTwenty()
    {
        // The two exceptions (Paulista Sul, Baiana Central) put an accent on the socks. Authored
        // kits may; the derivation keeps to the rule the other eighteen follow.
        int matching = Pilot.Value.Clubs.Count(club =>
            string.Equals(KitDerivation.AwayColours(club.Palette, club.Kits.Home.Shirt).Socks, club.Kits.Away.Socks, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(18, matching);
    }

    [Fact]
    public void ADarkHomeShirt_GoesAwayOnTheLightPole()
    {
        var palette = new ClubPalette("#0B2A6B", "#FFFFFF", "#000000", TypographyStyle.ModernSans);

        Assert.Equal(("#FFFFFF", "#FFFFFF", "#0B2A6B"), KitDerivation.AwayColours(palette, "#0B2A6B"));
    }

    [Fact]
    public void ALightHomeShirt_GoesAwayOnTheDarkPole()
    {
        var palette = new ClubPalette("#F5C518", "#0E6B3D", "#FFFFFF", TypographyStyle.ModernSans);

        Assert.Equal(("#0E6B3D", "#0E6B3D", "#F5C518"), KitDerivation.AwayColours(palette, "#F5C518"));
    }
}
