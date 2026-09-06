-- ============================================================================
-- Migration 0010 — calibration, position weights and sources
--
-- Every number the economy and validation formulas use lives here as DATA, never as
-- a literal in code (ROADMAP.md D-04). Re-fitting valuePivot or a position weight is
-- an UPDATE, not a recompile — valuePivot alone has already moved 62 -> 67 -> 68.
--
-- CalibrationConstants.Note is NOT NULL on purpose: DATA_CONTRACT.md §6 says "todo
-- número aqui tem origem declarada". The "no number without a source" rule holds in
-- the database too, not just in the UI.
--
-- WorldSources.Numero/Fonte/Url are NULLABLE, deviating from the DATA_CONTRACT.md §7
-- sketch: one of the 27 real rows is a prose cross-reference with all three null and
-- the whole text in Tema. Modelling them NOT NULL would make the real batch
-- unimportable, and a null Url must render as a note, not as an empty <a href>.
-- ============================================================================

CREATE TABLE CalibrationConstants (
    Key   TEXT PRIMARY KEY,
    Value REAL NOT NULL,
    Unit  TEXT NOT NULL,
    Note  TEXT NOT NULL          -- mandatory: the origin of the number (see header)
);

-- Step function: the multiplier of the last rung whose Age is <= the player's age.
CREATE TABLE CalibrationAgeMultipliers (
    Age        INTEGER PRIMARY KEY,
    Multiplier REAL NOT NULL
);

CREATE TABLE CalibrationPrestigeBands (
    Band      TEXT PRIMARY KEY CHECK (Band IN ('B1', 'B2', 'B3', 'B4', 'B5', 'B6')),
    ValueMult REAL    NOT NULL,
    CapMean   REAL    NOT NULL,
    CapSd     REAL    NOT NULL,
    N         INTEGER NOT NULL
);

-- Atmosphere -> home advantage. Writing a club's atmosphere rewrites its
-- HomeAdvantageModifier from this table (ALGORITHMS.md §4.3).
CREATE TABLE CalibrationHomeAdvantage (
    AtmosphereArchetype TEXT PRIMARY KEY CHECK (AtmosphereArchetype IN
                            ('Cauldron', 'Traditional', 'Corporate', 'Hostile', 'Apathetic')),
    Modifier            REAL NOT NULL
);

-- Per-country stadium capacity profile; drives club invariant #5.
CREATE TABLE CalibrationStadiumProfiles (
    CountryId TEXT PRIMARY KEY,
    Mean      REAL NOT NULL,
    Sd        REAL NOT NULL,
    MinValue  REAL NOT NULL,
    MaxValue  REAL NOT NULL
);

-- Position -> Attr -> weight. Weights sum to 1 per position; the sum is checked as an
-- invariant (calibration screen), not as a constraint — a row-level CHECK cannot see
-- the other 11 rows, and a half-edited matrix must be storable.
CREATE TABLE PositionWeights (
    Position TEXT NOT NULL CHECK (Position IN ('GK', 'CB', 'FB', 'DM', 'CM', 'AM', 'WG', 'ST')),
    Attr     TEXT NOT NULL CHECK (Attr IN
                 ('Finishing', 'Passing', 'Dribbling', 'Tackling', 'Pace', 'Strength',
                  'Stamina', 'Positioning', 'Vision', 'Composure', 'Reflexes', 'Handling')),
    Weight   REAL NOT NULL,
    PRIMARY KEY (Position, Attr)
);

CREATE TABLE WorldSources (
    Id     INTEGER PRIMARY KEY,
    Tema   TEXT NOT NULL,
    Numero TEXT NULL,
    Fonte  TEXT NULL,
    Url    TEXT NULL          -- null = prose cross-reference, not a broken link (see header)
);
