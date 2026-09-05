namespace SoccerSim.Core.World.Color;

/// <summary>
/// A color in the CIELAB (D65) space. Only ever produced by <see cref="FromSrgb"/> — construct
/// from a hex string, never by hand.
/// </summary>
public readonly record struct Lab(double L, double A, double B)
{
    /// <summary>sRGB (D65) → CIELAB, per ALGORITHMS.md §1.1.</summary>
    public static Lab FromSrgb(string hex)
    {
        var (r8, g8, b8) = ColorMath.ParseHex(hex);
        double r = Decode(r8), g = Decode(g8), b = Decode(b8);

        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        double y = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 1.00000;
        double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;

        double fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double Decode(int channel8) => ColorMath.GammaExpand(channel8, threshold: 0.04045);

    private static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
}

/// <summary>
/// Color math for club identity — kit contrast, text-on-swatch legibility, and "does the away
/// kit introduce a new hue" — per ALGORITHMS.md §1. CIE76, not CIEDE2000: that is what produced
/// the ΔE values recorded in the source data (e.g. 104.6 for the first club), and switching
/// formulas would invalidate every recorded ΔE (see the warning in ALGORITHMS.md §1.2).
/// </summary>
public static class ColorMath
{
    /// <summary>ΔE (CIE76) between two hex colors: the Euclidean distance in CIELAB space.</summary>
    public static double DeltaE76(string hex1, string hex2)
    {
        var a = Lab.FromSrgb(hex1);
        var b = Lab.FromSrgb(hex2);
        double dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>WCAG relative luminance. Used both to pick legible text color over a club color
    /// (Y &gt; 0.55 → dark ink, else white — 0.5 in the palette swatches) and to decide kit
    /// polarity (ALGORITHMS.md §1.3, §4.3).</summary>
    public static double RelativeLuminance(string hex)
    {
        var (r8, g8, b8) = ParseHex(hex);
        double r = GammaExpand(r8, threshold: 0.03928);
        double g = GammaExpand(g8, threshold: 0.03928);
        double b = GammaExpand(b8, threshold: 0.03928);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>HSL hue in degrees [0, 360), or -1 for an achromatic color (grays never count as
    /// introducing a new hue). ALGORITHMS.md §1.4.</summary>
    public static double Hue(string hex)
    {
        var (r8, g8, b8) = ParseHex(hex);
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        if (d < 0.04)
            return -1;

        double h;
        if (max == r)
            h = Mod((g - b) / d, 6);
        else if (max == g)
            h = (b - r) / d + 2;
        else
            h = (r - g) / d + 4;

        return Mod(h * 60 + 360, 360);
    }

    /// <summary>Shortest circular distance between two hues in degrees.</summary>
    public static double HueDistance(double h1, double h2)
    {
        double diff = Math.Abs(h1 - h2);
        return Math.Min(diff, 360 - diff);
    }

    /// <summary>sRGB gamma expansion shared by luminance (0.03928 threshold) and Lab (0.04045
    /// threshold) — ALGORITHMS.md §1.1 and §1.3 use the same curve with different breakpoints.</summary>
    internal static double GammaExpand(int channel8, double threshold)
    {
        double c = channel8 / 255.0;
        return c <= threshold ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Positive floating-point modulo (JS-style <c>%</c>, never negative), needed because
    /// C#'s <c>%</c> keeps the dividend's sign.</summary>
    private static double Mod(double x, double m) => ((x % m) + m) % m;

    internal static (int R, int G, int B) ParseHex(string hex)
    {
        string h = hex.TrimStart('#');
        int r = Convert.ToInt32(h.Substring(0, 2), 16);
        int g = Convert.ToInt32(h.Substring(2, 2), 16);
        int b = Convert.ToInt32(h.Substring(4, 2), 16);
        return (r, g, b);
    }
}
