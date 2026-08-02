using SoccerSim.Core.Simulation;
using SoccerSim.Core.Tactics;

namespace SoccerSim.Content.Model;

/// <summary>
/// Every authored entity carries BOTH a stable numeric <see cref="Id"/> (what the game and
/// its foreign keys run on) and a stable textual <see cref="Key"/> (what the JSON bundle
/// cross-references and what a human reads in a diff).
///
/// Why both: ids must never shift, because a save file and its FKs are pinned to them; keys
/// must be the reference in JSON, because numbers make merges unreadable and unmergeable.
/// Deriving one from the other (e.g. ids from sorted key position) was considered and
/// rejected — inserting a single entity would renumber everything after it.
/// </summary>
public interface IContentEntity
{
    int Id { get; }

    string Key { get; }
}

/// <summary>A competition/division. Maps to the <c>Leagues</c> table.</summary>
public sealed record ContentLeague : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Country { get; init; }

    /// <summary>Level-of-detail tier; controls how this league's matches are resolved.</summary>
    public SimulationTier Tier { get; init; } = SimulationTier.Minor;

    /// <summary>The tournament this division belongs to. Null while unassigned.</summary>
    public string? CompetitionKey { get; init; }

    public string? NationKey { get; init; }

    /// <summary>1 = top flight. Drives promotion/relegation between divisions.</summary>
    public int PyramidLevel { get; init; } = 1;

    public int PromotionSlots { get; init; }

    public int RelegationSlots { get; init; }
}

/// <summary>A club. Maps to the <c>Teams</c> table.</summary>
public sealed record ContentTeam : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary><see cref="ContentLeague.Key"/> of the league this club plays in.</summary>
    public required string LeagueKey { get; init; }

    public long Budget { get; init; }

    public int EloRating { get; init; } = 1500;

    public string? ShortName { get; init; }

    public string? NationKey { get; init; }

    public string? StadiumKey { get; init; }

    public int? FoundedYear { get; init; }

    public int Reputation { get; init; } = 50;
}

/// <summary>
/// The seven static base attributes, 1–20. Mirrors
/// <see cref="SoccerSim.Core.Domain.PlayerAttributes"/> but stays a separate authoring type:
/// the content schema is versioned independently of the runtime domain model.
/// </summary>
public sealed record ContentAttributes
{
    public int Pace { get; init; }

    public int Stamina { get; init; }

    public int Strength { get; init; }

    public int Passing { get; init; }

    public int Shooting { get; init; }

    public int Tackling { get; init; }

    public int Vision { get; init; }

    public IEnumerable<(string Name, int Value)> Enumerate()
    {
        yield return (nameof(Pace), Pace);
        yield return (nameof(Stamina), Stamina);
        yield return (nameof(Strength), Strength);
        yield return (nameof(Passing), Passing);
        yield return (nameof(Shooting), Shooting);
        yield return (nameof(Tackling), Tackling);
        yield return (nameof(Vision), Vision);
    }
}

/// <summary>A player. Maps to <c>Players</c> + <c>PlayerTraitAssignments</c>.</summary>
public sealed record ContentPlayer : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary><see cref="ContentTeam.Key"/>, or null for a free agent.</summary>
    public string? TeamKey { get; init; }

    public required ContentAttributes Attributes { get; init; }

    /// <summary>
    /// <see cref="ContentTrait.Key"/> values assigned at generation.
    ///
    /// The initializer alone is not enough: an explicit <c>"traitKeys": null</c> — which is what
    /// a blank spreadsheet cell produces — overwrites it, and every consumer would then have to
    /// null-check a non-nullable property. Absorb it here instead.
    /// </summary>
    public IReadOnlyList<string> TraitKeys
    {
        get => _traitKeys;
        init => _traitKeys = value ?? [];
    }

    private readonly IReadOnlyList<string> _traitKeys = [];

    public DateTime? DateOfBirth { get; init; }

    public string? NationKey { get; init; }

    public PreferredFoot PreferredFoot { get; init; } = PreferredFoot.Right;

    /// <summary>
    /// Deliberately the lean <see cref="SoccerSim.Core.Tactics.PlayerRole"/> vocabulary rather
    /// than a 14-position taxonomy: the on-pitch AI reasons in roles plus formation slots, so a
    /// finer position list would be authored data nothing could act on. Side preference lives in
    /// <see cref="Flank"/>, which together with the role is what a formation slot needs.
    /// </summary>
    public PlayerRole PrimaryRole { get; init; } = PlayerRole.Midfielder;

    public Flank Flank { get; init; } = Flank.Centre;

    public int? SquadNumber { get; init; }

    public int? HeightCm { get; init; }
}

/// <summary>A personality trait in the catalogue. Maps to <c>PlayerTraits</c>.</summary>
public sealed record ContentTrait : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public int Aggression { get; init; }

    public int Selfishness { get; init; }

    public int EventWeightBias { get; init; }
}

/// <summary>A purchasable housing item. Maps to <c>HousingItems</c>.</summary>
public sealed record ContentHousingItem : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public long Cost { get; init; }

    /// <summary>Which daily-task yield this item multiplies, e.g. <c>stamina_recovery</c>.</summary>
    public required string StatKey { get; init; }

    public double YieldMultiplier { get; init; } = 1.0;
}
