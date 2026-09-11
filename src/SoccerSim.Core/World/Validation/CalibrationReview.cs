namespace SoccerSim.Core.World.Validation;

/// <summary>
/// How well sourced a calibrated number is, read off its own note. The project's rule is that no
/// number enters without a declared origin; this turns that from prose into a seal the screen can
/// colour.
/// </summary>
public enum SourceSeal
{
    /// <summary>Fitted or measured against something citable.</summary>
    Anchored,

    /// <summary>The note says so itself: "NÃO ANCORADO".</summary>
    Unsourced,

    /// <summary>The note says the number is standing in until something is resolved.</summary>
    Provisional,
}

/// <summary>One constant, with what its note admits about it.</summary>
public sealed record ConstantReview(string Key, CalibrationConstant Constant, SourceSeal Seal);

/// <summary>One row of the position-weight matrix and whether it closes.</summary>
public sealed record WeightRowReview(Position Position, double Sum, bool Balanced);

/// <summary>One rung of the age ladder with how many players stand on it.</summary>
public sealed record AgeRungReview(int Age, double Multiplier, int Players);

/// <summary>One country's stadium profile and how many of its clubs fall outside it.</summary>
public sealed record StadiumProfileReview(string CountryId, StadiumProfileEntry Profile, int ClubsOutside);

/// <summary>Everything the calibration screen shows, computed in Core so the browser adds nothing
/// up itself.</summary>
public sealed record CalibrationReview(
    IReadOnlyList<ConstantReview> Constants,
    IReadOnlyList<WeightRowReview> Weights,
    IReadOnlyList<AgeRungReview> AgeLadder,
    IReadOnlyList<StadiumProfileReview> StadiumProfiles,
    int StalePlayers)
{
    /// <summary>Rows whose weights do not sum to 1. A row that sums to 0.98 quietly deflates
    /// every overall computed from it — by about two points, across the whole batch.</summary>
    public IReadOnlyList<WeightRowReview> UnbalancedWeights =>
        Weights.Where(row => !row.Balanced).ToList();

    public int Unsourced => Constants.Count(review => review.Seal == SourceSeal.Unsourced);

    public int Provisional => Constants.Count(review => review.Seal == SourceSeal.Provisional);
}

/// <summary>
/// Reads the calibration back as something reviewable (ROADMAP.md Sprint 9). Nothing here
/// changes a number — it reports what the numbers currently say about themselves.
/// </summary>
public static class CalibrationReviewer
{
    /// <summary>The most a hand-typed row of twelve weights can reasonably be expected to close
    /// to. Tighter than this and a legitimate matrix fails on rounding.</summary>
    public const double WeightTolerance = 0.0005;

    public static CalibrationReview Review(WorldSnapshot world)
    {
        WorldCalibration calibration = world.Calibration;

        Dictionary<string, PrestigeBand> bands = world.Clubs
            .ToDictionary(club => club.ClubId, club => club.World.PrestigeBand);

        ILookup<string, ClubIdentity> byCountry = world.Clubs.ToLookup(club => club.Geography.CountryId);

        return new CalibrationReview(
            calibration.Constants
                .Select(entry => new ConstantReview(entry.Key, entry.Value, SealOf(entry.Value.Note)))
                .ToList(),
            calibration.PositionWeights.Select(entry =>
            {
                double sum = entry.Value.Values.Sum();
                return new WeightRowReview(entry.Key, sum, Math.Abs(sum - 1.0) <= WeightTolerance);
            }).ToList(),
            calibration.AgeMult
                .Select((step, index) => new AgeRungReview(
                    step.Age,
                    step.Multiplier,
                    // Players standing on this rung: from this age up to the next rung's age.
                    world.Characters.Count(player =>
                        player.Age >= step.Age
                        && (index + 1 >= calibration.AgeMult.Count || player.Age < calibration.AgeMult[index + 1].Age))))
                .ToList(),
            calibration.StadiumProfile.Select(entry => new StadiumProfileReview(
                entry.Key,
                entry.Value,
                byCountry[entry.Key].Count(club =>
                    club.Stadium.Capacity < entry.Value.Min || club.Stadium.Capacity > entry.Value.Max))).ToList(),
            world.Characters.Count(player =>
                bands.TryGetValue(player.ClubId, out PrestigeBand band)
                && IsStale(player, band, calibration)));
    }

    /// <summary>
    /// The seal comes from the note, because the note is where the honesty already lives: the
    /// authored calibration writes "NÃO ANCORADO" and "PROVISÓRIO" in plain sight, and reading
    /// them is cheaper and truer than keeping a second flag beside them that can disagree.
    /// </summary>
    public static SourceSeal SealOf(string note)
    {
        if (note.Contains("NÃO ANCORADO", StringComparison.OrdinalIgnoreCase))
            return SourceSeal.Unsourced;

        if (note.Contains("PROVISÓRIO", StringComparison.OrdinalIgnoreCase))
            return SourceSeal.Provisional;

        return SourceSeal.Anchored;
    }

    /// <summary>
    /// The batch with every economy recomputed. This is what the calibration screen's "recalculate"
    /// writes: a re-fit constant ages every player at once, and correcting them one edit at a time
    /// is not a thing a person can do to 688 rows.
    /// </summary>
    public static IReadOnlyList<CharacterRecord> Recalculate(WorldSnapshot world)
    {
        Dictionary<string, PrestigeBand> bands = world.Clubs
            .ToDictionary(club => club.ClubId, club => club.World.PrestigeBand);

        return world.Characters
            .Where(player => bands.ContainsKey(player.ClubId))
            .Select(player => WorldDerivations.Recalculate(player, bands[player.ClubId], world.Calibration))
            .ToList();
    }

    private static bool IsStale(CharacterRecord player, PrestigeBand band, WorldCalibration calibration)
    {
        CharacterRecord fresh = WorldDerivations.Recalculate(player, band, calibration);

        return fresh.Overall != player.Overall
            || fresh.PotentialOverall != player.PotentialOverall
            || fresh.MarketValueEur != player.MarketValueEur
            || fresh.SalaryMonthlyBrl != player.SalaryMonthlyBrl;
    }
}
