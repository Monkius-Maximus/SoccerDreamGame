namespace SoccerSim.Core.World;

/// <summary>
/// A player in the world-authoring schema (12 attributes, 1..99 scale) — distinct from the
/// legacy <see cref="SoccerSim.Core.Domain.Player"/> (7 attributes, 1..20) that
/// <c>MatchEngine</c> consumes today. The two coexist by design (ROADMAP.md D-02); a derived
/// projection bridges them in Sprint 6.
///
/// <para>
/// <see cref="Age"/> is stored, never derived from <see cref="DateOfBirth"/>: 297 of the 688
/// source players disagree with a birthday-aware calculation at the contract's reference date
/// (2026-01-28), and age feeds the market-value curve — deriving it would silently rewrite
/// those players' economy.
/// </para>
/// </summary>
public sealed record CharacterRecord(
    string PlayerId,
    string ClubId,
    int ShirtNumber,
    string FirstName,
    string LastName,
    string ShirtName,
    string Nationality,
    string? SecondNationality,
    DateOnly DateOfBirth,
    int Age,
    Phase Phase,
    SquadRole SquadRole,
    Position PrimaryPosition,
    IReadOnlyList<Position> SecondaryPositions,
    PreferredFoot PreferredFoot,
    int WeakFootRating,
    int SkillMovesRating,
    int Height,
    BuildType BuildType,
    IReadOnlyDictionary<Attr, int> Attrs,
    int PotentialGap,
    Provenance Provenance,
    int Overall,
    int PotentialOverall,
    int MarketValueEur,
    int SalaryMonthlyBrl,
    CharacterDeviationAudit Audit);

/// <summary>
/// Deviation-from-reality trail for a generated/anchored player. A <c>Regen</c> player (no
/// anchor) always carries <c>AnchorPlayerName = null</c> and <c>AnchorFactsVerified = false</c>
/// — this is what makes squad generation safe to invent without asserting facts about a real
/// person (ALGORITHMS.md §6.10).
/// </summary>
public sealed record CharacterDeviationAudit(
    string? AnchorPlayerName,
    string? AnchorNationality,
    string? DeviationFromSurname,
    string? GeneratedSurname,
    double? PhoneticSimilarity,
    /// <summary>Null for a Regen player: with no anchor, there is no deviation to describe.
    /// All 400 generated players in the source batch carry null here.</summary>
    string? DeviationMethod,
    bool AnchorFactsVerified);
