namespace SoccerSim.Core.World;

// The closed enums of the ClubIdentity v2 / CharacterRecord schema
// (design_handoff_ferramenta_de_mundo/DATA_CONTRACT.md §2). A value outside these sets is an
// ERROR, not a warning — ClubInvariants.Check enforces this at runtime via Enum.IsDefined,
// since C# does not guarantee an enum's underlying value is one of its named members.

public enum DistrictArchetype { WorkingClass, Docklands, HistoricCenter, Affluent, University, Outskirts, Coastal }

public enum ShieldShape { Heater, Round, Oval, Square, Ogival }

public enum AtmosphereArchetype { Cauldron, Traditional, Corporate, Hostile, Apathetic }

public enum PitchSurface { Pristine, Heavy, Synthetic, Worn }

public enum TacticalStyle { Possession, Counter, HighPress, LowBlock, Controlled, Direct }

public enum TypographyStyle { ModernSans, ClassicSerif, Blackletter, Geometric, Stencil }

public enum CollarStyle { VNeck, Polo, Crew, Grandad, ButtonedSport }

public enum FitStyle { Slim, Regular, Retro }

public enum FabricPattern { Solid, VerticalStripes, HorizontalStripes, Sash, ContrastSleeves, Pinstripes, Checks }

public enum NamingRule { Toponymic, RegionalCode, EpithetLift, Phonetic }

public enum CompetitionScope { SubNational, National, SubContinentalZonal, Continental, Intercontinental }

public enum GeoNodeKind { World, Confederation, SubRegion, Country, Region, City }

public enum Position { GK, CB, FB, DM, CM, AM, WG, ST }

/// <summary>The 12 canonical attributes (1..99). Rows in persistence, not fixed columns — see
/// DATA_CONTRACT.md §7 note 1: positionWeights has already changed shape three times.</summary>
public enum Attr { Finishing, Passing, Dribbling, Tackling, Pace, Strength, Stamina, Positioning, Vision, Composure, Reflexes, Handling }

public enum BuildType { Lean, Balanced, Athletic, Stocky }

/// <summary>Age-derived life phase. Source data carries this as "Prospect 15-20" etc. — the
/// age range is descriptive, not part of the enum; normalize on import.</summary>
public enum Phase { Prospect, Breakthrough, Prime, Veteran, Twilight }

public enum PrestigeBand { B1, B2, B3, B4, B5, B6 }

/// <summary>Source data spells this "Rotação" (with cedilla); normalize to <see cref="Rotacao"/> on import.</summary>
public enum SquadRole { Titular, Rotacao, Reserva, Promessa }

public enum Provenance { Anchored, Regen }

public enum TacticalStyleProvenance { Derived, Sampled, Authored }

public enum PreferredFoot { Right, Left, Both }
