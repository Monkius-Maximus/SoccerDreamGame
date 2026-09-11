using SoccerSim.Core.Domain;

namespace SoccerSim.Core.World.Projection;

/// <summary>
/// How the world's twelve attributes (1–99) become the legacy seven (1–20).
///
/// <para>ADR-0002 commits to this being an <b>explicit, documented weight table</b> and not a
/// blind rescale of a subset, for one reason: a collapse from twelve dimensions to seven throws
/// information away, and the only honest version of that is one where you can read exactly what
/// was thrown away and what absorbed it.</para>
///
/// <para>Every one of the twelve appears in at least one column. Five map essentially straight
/// through (Pace, Stamina, Strength, Passing, Tackling); Finishing becomes Shooting tempered by
/// Composure; Positioning has no column of its own and is split between Tackling and Vision,
/// which is where reading the game actually shows up in the legacy model.</para>
/// </summary>
public static class AttributeMapping
{
    /// <summary>
    /// The outfield table. Weights within each column sum to 1, so a player with 99 everywhere
    /// projects to 20 everywhere and the scale is never quietly inflated by a heavy mix.
    /// </summary>
    public static readonly IReadOnlyDictionary<LegacyAttribute, IReadOnlyDictionary<Attr, double>> Outfield =
        new Dictionary<LegacyAttribute, IReadOnlyDictionary<Attr, double>>
        {
            [LegacyAttribute.Pace] = new Dictionary<Attr, double>
            {
                // Not pure Pace: the legacy engine has no dribbling, and carrying the ball at
                // speed is the part of Dribbling that has anywhere else to go.
                [Attr.Pace] = 0.85, [Attr.Dribbling] = 0.15,
            },
            [LegacyAttribute.Stamina] = new Dictionary<Attr, double> { [Attr.Stamina] = 1.00 },
            [LegacyAttribute.Strength] = new Dictionary<Attr, double> { [Attr.Strength] = 1.00 },
            [LegacyAttribute.Passing] = new Dictionary<Attr, double>
            {
                [Attr.Passing] = 0.75, [Attr.Vision] = 0.25,
            },
            [LegacyAttribute.Shooting] = new Dictionary<Attr, double>
            {
                [Attr.Finishing] = 0.70, [Attr.Composure] = 0.30,
            },
            [LegacyAttribute.Tackling] = new Dictionary<Attr, double>
            {
                [Attr.Tackling] = 0.70, [Attr.Positioning] = 0.30,
            },
            [LegacyAttribute.Vision] = new Dictionary<Attr, double>
            {
                [Attr.Vision] = 0.60, [Attr.Positioning] = 0.25, [Attr.Composure] = 0.15,
            },
        };

    /// <summary>
    /// The columns a goalkeeper overrides. Without these, <see cref="Attr.Reflexes"/> and
    /// <see cref="Attr.Handling"/> — the two attributes that are the entire point of a keeper —
    /// would be dropped on the floor, and a world-class keeper would project identically to a
    /// hopeless one. Everything not listed here comes from <see cref="Outfield"/>, including
    /// Shooting: a keeper's Finishing is genuinely low, and that is what keeps the engine from
    /// treating him as a scoring threat.
    /// </summary>
    public static readonly IReadOnlyDictionary<LegacyAttribute, IReadOnlyDictionary<Attr, double>> Goalkeeper =
        new Dictionary<LegacyAttribute, IReadOnlyDictionary<Attr, double>>
        {
            [LegacyAttribute.Passing] = new Dictionary<Attr, double>
            {
                [Attr.Passing] = 0.80, [Attr.Composure] = 0.20,
            },
            [LegacyAttribute.Tackling] = new Dictionary<Attr, double>
            {
                // A keeper's stopping ability, in the only column the legacy model has for it.
                [Attr.Reflexes] = 0.60, [Attr.Positioning] = 0.40,
            },
            [LegacyAttribute.Vision] = new Dictionary<Attr, double>
            {
                [Attr.Handling] = 0.40, [Attr.Vision] = 0.40, [Attr.Composure] = 0.20,
            },
        };

    /// <summary>The table that applies to a player in this position.</summary>
    public static IReadOnlyDictionary<Attr, double> WeightsFor(LegacyAttribute column, Position position) =>
        position == Position.GK && Goalkeeper.TryGetValue(column, out IReadOnlyDictionary<Attr, double>? keeper)
            ? keeper
            : Outfield[column];

    /// <summary>
    /// 1–99 down to 1–20, linear and endpoint-exact: 1 maps to 1 and 99 maps to 20, so neither
    /// end of the legacy range is unreachable. The legacy columns carry a
    /// <c>CHECK (… BETWEEN 1 AND 20)</c>, so anything else is a failed insert, not a rounding
    /// quirk.
    /// </summary>
    public static int ToLegacyScale(double value) =>
        Math.Clamp((int)Math.Round(1 + ((value - 1) * 19 / 98), MidpointRounding.AwayFromZero), 1, 20);

    /// <summary>The legacy seven, projected from one character's twelve.</summary>
    public static PlayerAttributes Project(CharacterRecord character)
    {
        int Column(LegacyAttribute column)
        {
            double blended = 0;
            foreach ((Attr source, double weight) in WeightsFor(column, character.PrimaryPosition))
                blended += character.Attrs[source] * weight;

            return ToLegacyScale(blended);
        }

        return new PlayerAttributes(
            Pace: Column(LegacyAttribute.Pace),
            Stamina: Column(LegacyAttribute.Stamina),
            Strength: Column(LegacyAttribute.Strength),
            Passing: Column(LegacyAttribute.Passing),
            Shooting: Column(LegacyAttribute.Shooting),
            Tackling: Column(LegacyAttribute.Tackling),
            Vision: Column(LegacyAttribute.Vision));
    }
}

/// <summary>The seven columns of <c>Players</c> in <c>sql/0001_initial_schema.sql</c>, named so
/// the mapping table can be keyed on something closed rather than on strings.</summary>
public enum LegacyAttribute
{
    Pace,
    Stamina,
    Strength,
    Passing,
    Shooting,
    Tackling,
    Vision,
}
