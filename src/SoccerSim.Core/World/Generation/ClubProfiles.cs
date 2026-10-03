namespace SoccerSim.Core.World.Generation;

/// <summary>Which palette colour a kit piece takes.</summary>
public enum PaletteSlot { Primary, Secondary, Tertiary }

/// <summary>
/// A city a country's clubs can be born in. <see cref="GeoNodeId"/> must name a City node of the
/// world's geo tree; the tree does not carry the state (UF) or the Portuguese preposition a
/// club name needs ("do Recife", "de Santos"), so the profile does.
/// </summary>
public sealed record CityProfile(
    string GeoNodeId,
    string Uf,
    string Preposition,
    double Weight,
    int MaxClubs,
    IReadOnlyList<(DistrictArchetype Item, double Weight)> Districts);

/// <summary>
/// A named colour a palette can draw from, with a separate weight for each palette slot:
/// white is common as a secondary and rare as a primary.
/// </summary>
public sealed record ColorFamily(
    string Id,
    string Hex,
    double PrimaryWeight,
    double SecondaryWeight,
    double TertiaryWeight);

/// <summary>
/// A nickname for a palette. Two families ("red" + "black" → "Os Rubro-Negros") match the
/// primary and secondary in either order; one family matches the primary alone.
/// </summary>
public sealed record NicknameRule(IReadOnlyList<string> Families, string Text);

/// <summary>
/// Everything the club generator needs that is data rather than logic: where clubs are, what
/// they are called, which colours and shapes they wear, and how big their stadiums and squads
/// are. Imported per country, never hardcoded — the same rule <see cref="GenerationProfiles"/>
/// follows for players (ADR-0011 §3).
///
/// <para><see cref="ReservedNames"/> are real clubs' short names a generated club may not take,
/// accents and case ignored — a Regen club asserts nothing about a real one, and a shared name
/// would (the IP rule the anchored clubs' naming rules exist for).</para>
///
/// <para><see cref="SectionSources"/> maps every section to one or more keys of
/// <see cref="Sources"/>, so each number can say where it came from: measured from the pilot
/// league, or authored and still provisional. The reader refuses a section without one.</para>
/// </summary>
public sealed record ClubProfiles(
    string CountryId,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SectionSources,
    IReadOnlyList<CityProfile> Cities,
    IReadOnlyList<(string Item, double Weight)> ClubPrefixes,
    IReadOnlyDictionary<DistrictArchetype, IReadOnlyList<string>> Qualifiers,
    IReadOnlyList<string> ReservedNames,
    IReadOnlyList<ColorFamily> ColorFamilies,
    IReadOnlyList<NicknameRule> Nicknames,
    IReadOnlyList<(int Item, double Weight)> FoundingDecades,
    IReadOnlyList<(ShieldShape Item, double Weight)> ShieldShapes,
    IReadOnlyList<(TypographyStyle Item, double Weight)> TypographyStyles,
    IReadOnlyList<(CollarStyle Item, double Weight)> CollarStyles,
    IReadOnlyList<(FitStyle Item, double Weight)> FitStyles,
    IReadOnlyList<(FabricPattern Item, double Weight)> HomePatterns,
    IReadOnlyList<(FabricPattern Item, double Weight)> AwayPatterns,
    IReadOnlyList<(PaletteSlot Item, double Weight)> HomeSocks,
    IReadOnlyList<(AtmosphereArchetype Item, double Weight)> Atmospheres,
    IReadOnlyList<(PitchSurface Item, double Weight)> PitchSurfaces,
    IReadOnlyList<(TacticalStyle Item, double Weight)> TacticalStyles,
    IReadOnlyList<string> CentralCharges,
    IReadOnlyList<string> Mottos,
    IReadOnlyList<(string Item, double Weight)> StadiumPatterns,
    IReadOnlyList<string> StadiumPlaces,
    int CapacityStep,
    IReadOnlyDictionary<PrestigeBand, int> SquadSizeByBand);
