using SoccerSim.Core.World.Color;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// Builds a club's away kit from its palette and home shirt by polarity inversion
/// (ALGORITHMS.md §7.2): a dark home shirt sends the away kit to the palette's light pole, a
/// light one to its dark pole. The away shirt and shorts take that pole; the socks carry the
/// home shirt, so the away kit reorders the palette instead of adding a colour to it.
///
/// <para>Measured against the twenty pilot clubs (<c>TestData/world.json</c>): the away shirt and
/// shorts sit on the pole in 20 of 20, the socks equal the home shirt in 18 of 20. Until this
/// sprint the C# side only labelled the polarity (<see cref="WorldDerivations.PolarityDarkHome"/>);
/// generating a club is the first thing that has to build the kit.</para>
/// </summary>
public static class KitDerivation
{
    /// <summary>The palette's light and dark poles, by relative luminance. Ties keep palette
    /// order (primary, secondary, tertiary), so the result is the same on every run.</summary>
    public static (string Light, string Dark) Poles(ClubPalette palette)
    {
        string[] colours = [palette.Primary, palette.Secondary, palette.Tertiary];
        string light = colours[0];
        string dark = colours[0];
        foreach (string colour in colours)
        {
            if (ColorMath.RelativeLuminance(colour) > ColorMath.RelativeLuminance(light))
                light = colour;
            if (ColorMath.RelativeLuminance(colour) < ColorMath.RelativeLuminance(dark))
                dark = colour;
        }

        return (light, dark);
    }

    /// <summary>The away kit's three colours for a home shirt on this palette.</summary>
    public static (string Shirt, string Shorts, string Socks) AwayColours(ClubPalette palette, string homeShirt)
    {
        (string light, string dark) = Poles(palette);
        string pole = ColorMath.RelativeLuminance(homeShirt) < WorldDerivations.DarkHomeLuminanceThreshold
            ? light
            : dark;
        return (pole, pole, homeShirt);
    }
}
