namespace SoccerSim.Core.World.Competitions;

/// <summary>One nationality's share of a country's player pool, as a fraction of 1.</summary>
public sealed record NationalityShare(string Nationality, double Share);

/// <summary>
/// What the tool has to know about a country before it can populate one (ROADMAP.md Sprint 9).
/// The pilot world has exactly one, which is why none of this existed until a second becomes
/// possible.
///
/// <para><see cref="NationalityMix"/> is required, not optional: a country without one cannot be
/// populated, because the generator would have to guess where its players come from — and
/// guessing quietly makes everyone Brazilian. The batch sweep reports a country without a mix as
/// an error rather than letting generation invent one.</para>
/// </summary>
public sealed record CountryProfile(
    /// <summary>The geo node of kind Country this describes.</summary>
    string CountryId,
    string Currency,
    /// <summary>How many units of <see cref="Currency"/> one euro buys. The economy is calibrated
    /// in EUR and paid in local money, so this is the hinge between the two.</summary>
    double EurToLocal,
    int WageFloorMonthly,
    IReadOnlyList<NationalityShare> NationalityMix,
    /// <summary>Where the mix came from. Null means measured from the batch itself — true of
    /// Brazil, and flagged as such, because a distribution derived from the data it will generate
    /// is not evidence about the world.</summary>
    string? NationalityMixSource)
{
    /// <summary>How far the shares are from summing to 1. Shown rather than corrected: a mix that
    /// sums to 0.97 is an unfinished edit, and rounding it silently hides the missing 3%.</summary>
    public double ShareSum => NationalityMix.Sum(share => share.Share);

    /// <summary>Within half a percentage point, which is the most a hand-typed table of shares
    /// can reasonably be expected to close to.</summary>
    public bool SharesBalance => Math.Abs(ShareSum - 1.0) <= 0.005;
}
