-- ============================================================================
-- Migration 0007 — clubs (ClubIdentity v2)
--
-- One row per club, flattening the nested authoring blocks (identity, geography,
-- world, crest, palette, kits, stadium, aiProfile) from DATA_CONTRACT.md §3. The
-- deviation audit is a separate 1:1 table (0008) because it has its own lifecycle.
--
-- WHAT IS AND ISN'T CONSTRAINED HERE — this distinction is the whole design:
--
--   * Structural facts get CHECK constraints: closed enums, code shape, positive
--     capacity, squad size range. A value outside these is corrupt data, not an
--     authoring state.
--   * Authoring invariants do NOT get CHECK constraints. The tool must be able to
--     save a club that currently violates an invariant — the user needs to store
--     work-in-progress and fix it later, and the audit sweep needs the bad row IN
--     the database to be able to report it (design_handoff README, "Portão de
--     invariantes"; ROADMAP.md Sprint 8 golden = 1 error + 4 warnings on the real batch).
--     Those live in Core/World/Validation/ClubInvariants as Findings.
--
-- Concretely: DATA_CONTRACT.md §7 proposes CHECK (PhoneticSimilarity BETWEEN 0.55
-- AND 0.80). That constraint is NOT applied, because the real batch contains
-- clb_bra_bel_001 (Clube Náutico Remolar, NamingRule = Phonetic, similarity 0.867)
-- — a genuine, known violation the tool exists to surface. A CHECK would make the
-- offending club unimportable and therefore invisible and unfixable.
--
-- DeltaE, HomeLuminance, PolarityRule and HomeAdvantageModifier are stored for query
-- convenience but are DERIVED: every write recalculates them from palette/kits/
-- atmosphere via Core/World/WorldDerivations. A derived value from a client is never
-- trusted (ROADMAP.md Sprint 2).
-- ============================================================================

CREATE TABLE Clubs (
    ClubId                TEXT PRIMARY KEY,
    DisplayCode           TEXT    NOT NULL UNIQUE CHECK (length(DisplayCode) = 3),

    -- identity
    OfficialName          TEXT    NOT NULL,
    ShortName             TEXT    NOT NULL,
    Nickname              TEXT    NOT NULL,
    FoundingYear          INTEGER NOT NULL,

    -- geography
    CityName              TEXT    NOT NULL,
    Uf                    TEXT    NOT NULL,
    CountryId             TEXT    NOT NULL,
    GeoNodeId             TEXT    NOT NULL REFERENCES GeoNodes (GeoNodeId) ON DELETE RESTRICT,
    DistrictArchetype     TEXT    NOT NULL CHECK (DistrictArchetype IN
                              ('WorkingClass', 'Docklands', 'HistoricCenter', 'Affluent',
                               'University', 'Outskirts', 'Coastal')),

    -- world
    PrestigeBand          TEXT    NOT NULL CHECK (PrestigeBand IN ('B1','B2','B3','B4','B5','B6')),
    ClubStrength          REAL    NOT NULL,
    SquadSize             INTEGER NOT NULL CHECK (SquadSize BETWEEN 28 AND 40),
    NamingRule            TEXT    NOT NULL CHECK (NamingRule IN
                              ('Toponymic', 'RegionalCode', 'EpithetLift', 'Phonetic')),

    -- crest (Colors mirrors the palette and is derived on read, so it is not stored)
    ShieldShape           TEXT    NOT NULL CHECK (ShieldShape IN
                              ('Heater', 'Round', 'Oval', 'Square', 'Ogival')),
    CentralCharge         TEXT    NOT NULL,
    Motto                 TEXT    NOT NULL,

    -- palette
    PalettePrimary        TEXT    NOT NULL CHECK (PalettePrimary   LIKE '#______'),
    PaletteSecondary      TEXT    NOT NULL CHECK (PaletteSecondary LIKE '#______'),
    PaletteTertiary       TEXT    NOT NULL CHECK (PaletteTertiary  LIKE '#______'),
    TypographyStyle       TEXT    NOT NULL CHECK (TypographyStyle IN
                              ('ModernSans', 'ClassicSerif', 'Blackletter', 'Geometric', 'Stencil')),

    -- kits
    CollarStyle           TEXT    NOT NULL CHECK (CollarStyle IN
                              ('VNeck', 'Polo', 'Crew', 'Grandad', 'ButtonedSport')),
    FitStyle              TEXT    NOT NULL CHECK (FitStyle IN ('Slim', 'Regular', 'Retro')),
    HomePattern           TEXT    NOT NULL CHECK (HomePattern IN
                              ('Solid', 'VerticalStripes', 'HorizontalStripes', 'Sash',
                               'ContrastSleeves', 'Pinstripes', 'Checks')),
    HomeShirt             TEXT    NOT NULL CHECK (HomeShirt  LIKE '#______'),
    HomeShorts            TEXT    NOT NULL CHECK (HomeShorts LIKE '#______'),
    HomeSocks             TEXT    NOT NULL CHECK (HomeSocks  LIKE '#______'),
    HomeLuminance         REAL    NOT NULL,                  -- derived; recalculated on write
    AwayPattern           TEXT    NOT NULL CHECK (AwayPattern IN
                              ('Solid', 'VerticalStripes', 'HorizontalStripes', 'Sash',
                               'ContrastSleeves', 'Pinstripes', 'Checks')),
    AwayShirt             TEXT    NOT NULL CHECK (AwayShirt  LIKE '#______'),
    AwayShorts            TEXT    NOT NULL CHECK (AwayShorts LIKE '#______'),
    AwaySocks             TEXT    NOT NULL CHECK (AwaySocks  LIKE '#______'),
    DeltaE                REAL    NOT NULL,                  -- derived; recalculated on write
    DeltaEThreshold       REAL    NOT NULL,                  -- calibrated data, not a literal (D-04)
    PolarityRule          TEXT    NOT NULL,                  -- derived; recalculated on write

    -- stadium
    StadiumName           TEXT    NOT NULL,
    StadiumCapacity       INTEGER NOT NULL CHECK (StadiumCapacity > 0),
    AtmosphereArchetype   TEXT    NOT NULL CHECK (AtmosphereArchetype IN
                              ('Cauldron', 'Traditional', 'Corporate', 'Hostile', 'Apathetic')),
    PitchSurface          TEXT    NOT NULL CHECK (PitchSurface IN
                              ('Pristine', 'Heavy', 'Synthetic', 'Worn')),

    -- aiProfile
    DefaultTacticalStyle  TEXT    NOT NULL CHECK (DefaultTacticalStyle IN
                              ('Possession', 'Counter', 'HighPress', 'LowBlock', 'Controlled', 'Direct')),
    TacticalStyleProvenance TEXT  NOT NULL CHECK (TacticalStyleProvenance IN
                              ('Derived', 'Sampled', 'Authored')),
    HomeAdvantageModifier REAL    NOT NULL,                  -- derived; recalculated on write
    DerbyRivalClubId      TEXT    NULL REFERENCES Clubs (ClubId) ON DELETE SET NULL
);

CREATE INDEX IX_Clubs_GeoNodeId    ON Clubs (GeoNodeId);
CREATE INDEX IX_Clubs_PrestigeBand ON Clubs (PrestigeBand);
