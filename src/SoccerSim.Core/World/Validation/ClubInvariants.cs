using System.Globalization;
using SoccerSim.Core.World.Color;

namespace SoccerSim.Core.World.Validation;

/// <summary>Severity, in ascending order. <see cref="WorstLevel"/> compares these by ordinal, so
/// the order is part of the contract — do not reorder the members.</summary>
public enum FindingLevel { Ok, Warning, Error }

/// <summary>One invariant result. <see cref="Code"/> is the stable identifier findings are
/// grouped/filtered by (batch audit, Sprint 8); <see cref="Label"/>/<see cref="Detail"/> are the
/// human-readable line shown on the club page.</summary>
public sealed record Finding(FindingLevel Level, string Code, string Label, string Detail);

/// <summary>
/// The six club-page invariants (ALGORITHMS.md §5, checks 1-6). An error means the club should
/// not enter the batch (enforced by the wizard, Sprint 3+); it never blocks editing an existing
/// club — the tool has to let a user save an invalid state while they work
/// (design_handoff_ferramenta_de_mundo/README.md, "Portão de invariantes").
/// </summary>
public static class ClubInvariants
{
    /// <summary>
    /// Findings are read in the tool's own language, and a sentence that says "0.867 fora de
    /// 0,55–0,80" reads as two different numbering systems in one breath. Every number in a
    /// detail goes through here.
    /// </summary>
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");
    public static IReadOnlyList<Finding> Check(ClubIdentity club, WorldCalibration calibration) =>
    [
        CheckAnchorFactsVerified(club),
        CheckPhoneticWindow(club),
        CheckKitDeltaE(club),
        CheckAwayKitNoNewHue(club),
        CheckStadiumCapacity(club, calibration),
        CheckEnumsClosed(club),
    ];

    /// <summary>
    /// A club's health is the worst of its checks — one error makes the club an error however
    /// many checks passed. This is the level the rail badge, the competition table and the grid
    /// all colour by, so it is computed here rather than in each of them.
    /// </summary>
    public static FindingLevel WorstLevel(IEnumerable<Finding> findings)
    {
        FindingLevel worst = FindingLevel.Ok;
        foreach (Finding finding in findings)
        {
            if (finding.Level > worst)
                worst = finding.Level;
        }
        return worst;
    }

    /// <summary>#1 — audit.anchorFactsVerified = 1. Error if not.</summary>
    private static Finding CheckAnchorFactsVerified(ClubIdentity club) =>
        club.Audit.AnchorFactsVerified
            ? new Finding(FindingLevel.Ok, "ANCHOR_VERIFIED", "Fatos da âncora verificados", "audit.anchorFactsVerified = 1")
            : new Finding(FindingLevel.Error, "ANCHOR_VERIFIED", "Fatos da âncora verificados", "audit.anchorFactsVerified = 0");

    /// <summary>#2 — when phoneticSimilarity is set (NamingRule = Phonetic), it must fall in
    /// [0.55, 0.80]. Null means the rule isn't Phonetic and the check does not apply.</summary>
    private static Finding CheckPhoneticWindow(ClubIdentity club)
    {
        double? similarity = club.Audit.PhoneticSimilarity;
        if (similarity is null)
            return new Finding(FindingLevel.Ok, "PHONETIC_WINDOW", "Janela fonética", "não se aplica (regra ≠ Phonetic)");

        bool inWindow = similarity.Value is >= 0.55 and <= 0.80;
        return inWindow
            ? new Finding(FindingLevel.Ok, "PHONETIC_WINDOW", "Janela fonética", similarity.Value.ToString("0.000", Ptbr))
            : new Finding(FindingLevel.Error, "PHONETIC_WINDOW", "Janela fonética", $"{similarity.Value.ToString("0.000", Ptbr)} fora de 0,55–0,80");
    }

    /// <summary>#3 — ΔE(home.shirt, away.shirt) must be at or above kits.deltaEThreshold.</summary>
    private static Finding CheckKitDeltaE(ClubIdentity club)
    {
        double deltaE = ColorMath.DeltaE76(club.Kits.Home.Shirt, club.Kits.Away.Shirt);
        return deltaE >= club.Kits.DeltaEThreshold
            ? new Finding(FindingLevel.Ok, "KIT_DELTA_E", "ΔE titular × reserva", deltaE.ToString("0.0", Ptbr))
            : new Finding(FindingLevel.Error, "KIT_DELTA_E", "ΔE titular × reserva",
                $"{deltaE.ToString("0.0", Ptbr)} abaixo do limite {club.Kits.DeltaEThreshold.ToString("0.#", Ptbr)}");
    }

    /// <summary>#4 (warning) — none of the away kit's 3 colors may sit more than 18° (hue) from
    /// every palette color; the reserve kit is meant to reorder the palette, not add a new hue.</summary>
    private static Finding CheckAwayKitNoNewHue(ClubIdentity club)
    {
        double[] paletteHues =
        [
            ColorMath.Hue(club.Palette.Primary),
            ColorMath.Hue(club.Palette.Secondary),
            ColorMath.Hue(club.Palette.Tertiary),
        ];
        string[] awayColors = [club.Kits.Away.Shirt, club.Kits.Away.Shorts, club.Kits.Away.Socks];

        bool introducesNewHue = awayColors.Any(color => IsNewHue(ColorMath.Hue(color), paletteHues));

        return introducesNewHue
            ? new Finding(FindingLevel.Warning, "KIT_NEW_HUE", "Reserva sem matiz novo", "uma cor do reserva foge à paleta em mais de 18°")
            : new Finding(FindingLevel.Ok, "KIT_NEW_HUE", "Reserva sem matiz novo", "dentro de 18° da paleta");
    }

    private static bool IsNewHue(double hue, double[] paletteHues)
    {
        if (hue < 0)
            return false; // achromatic never counts as a new hue

        return paletteHues
            .Where(paletteHue => paletteHue >= 0)
            .All(paletteHue => ColorMath.HueDistance(hue, paletteHue) > 18);
    }

    /// <summary>#5 — stadium capacity must fall inside the country's stadium profile. No profile
    /// for the country is a warning (can't validate), not an error.</summary>
    private static Finding CheckStadiumCapacity(ClubIdentity club, WorldCalibration calibration)
    {
        if (!calibration.StadiumProfile.TryGetValue(club.Geography.CountryId, out var profile))
            return new Finding(FindingLevel.Warning, "STADIUM_PROFILE_MISSING", "Capacidade no perfil do país",
                $"sem perfil de estádio para {club.Geography.CountryId}");

        int capacity = club.Stadium.Capacity;
        return capacity >= profile.Min && capacity <= profile.Max
            ? new Finding(FindingLevel.Ok, "STADIUM_CAPACITY", "Capacidade no perfil do país",
                $"{capacity.ToString("#,0", Ptbr)} dentro de {profile.Min.ToString("#,0", Ptbr)}–{profile.Max.ToString("#,0", Ptbr)}")
            : new Finding(FindingLevel.Error, "STADIUM_CAPACITY", "Capacidade no perfil do país",
                $"{capacity.ToString("#,0", Ptbr)} fora de {profile.Min.ToString("#,0", Ptbr)}–{profile.Max.ToString("#,0", Ptbr)}");
    }

    /// <summary>#6 — every enum-typed field must hold one of its schema's named values.
    /// C#'s enum type system does not itself guarantee this (an out-of-range underlying value is
    /// legal), so this checks explicitly with <see cref="Enum.IsDefined{TEnum}"/> — the guard
    /// that matters once values arrive from JSON import (Sprint 2), not from code that only ever
    /// constructs valid members.</summary>
    private static Finding CheckEnumsClosed(ClubIdentity club)
    {
        var invalidFields = new List<string>();
        void Check<TEnum>(TEnum value, string field) where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(value))
                invalidFields.Add(field);
        }

        Check(club.Geography.DistrictArchetype, "geography.districtArchetype");
        Check(club.World.PrestigeBand, "world.prestigeBand");
        Check(club.World.NamingRule, "world.namingRule");
        Check(club.Crest.ShieldShape, "crest.shieldShape");
        Check(club.Palette.TypographyStyle, "palette.typographyStyle");
        Check(club.Kits.CollarStyle, "kits.collarStyle");
        Check(club.Kits.FitStyle, "kits.fitStyle");
        Check(club.Kits.Home.FabricPattern, "kits.home.fabricPattern");
        Check(club.Kits.Away.FabricPattern, "kits.away.fabricPattern");
        Check(club.Stadium.AtmosphereArchetype, "stadium.atmosphereArchetype");
        Check(club.Stadium.PitchSurface, "stadium.pitchSurface");
        Check(club.AiProfile.DefaultTacticalStyle, "aiProfile.defaultTacticalStyle");
        Check(club.AiProfile.TacticalStyleProvenance, "aiProfile.tacticalStyleProvenance");

        return invalidFields.Count == 0
            ? new Finding(FindingLevel.Ok, "ENUM_CLOSED", "Enums fechados", "todos os campos dentro do schema")
            : new Finding(FindingLevel.Error, "ENUM_CLOSED", "Enums fechados", string.Join(", ", invalidFields));
    }
}
