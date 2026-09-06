-- ============================================================================
-- Migration 0009 — characters (CharacterRecord: 12 attributes, scale 1..99)
--
-- The world-authoring player. NOT the legacy Players table (7 attributes, 1..20),
-- which stays exactly as it is for MatchEngine — see docs/adr/0002.
--
-- Attributes are ROWS, not columns (DATA_CONTRACT.md §7 note 1): the position-weight
-- matrix has already been re-fitted three times, and adding a 13th attribute must be
-- an INSERT, not a migration of every row.
--
-- Age is STORED, never derived from DateOfBirth: 297 of the 688 source players
-- disagree with a birthday-aware calculation at the contract's reference date
-- (2026-01-28), and Age feeds the market-value curve — deriving it would silently
-- rewrite those players' value and salary.
--
-- Overall, PotentialOverall, MarketValueEur, SalaryMonthlyBrl and ShirtName ARE
-- derived and recalculated on every write (Core/World/WorldDerivations).
-- ============================================================================

CREATE TABLE Characters (
    PlayerId           TEXT PRIMARY KEY,
    ClubId             TEXT    NULL REFERENCES Clubs (ClubId) ON DELETE SET NULL,
    ShirtNumber        INTEGER NOT NULL CHECK (ShirtNumber BETWEEN 1 AND 99),
    FirstName          TEXT    NOT NULL,
    LastName           TEXT    NOT NULL,
    ShirtName          TEXT    NOT NULL,                     -- derived (LastName uppercased)
    Nationality        TEXT    NOT NULL,
    SecondNationality  TEXT    NULL,
    DateOfBirth        TEXT    NOT NULL,                      -- ISO-8601 date, 'YYYY-MM-DD'
    Age                INTEGER NOT NULL,                      -- stored, not derived (see header)
    Phase              TEXT    NOT NULL CHECK (Phase IN
                           ('Prospect', 'Breakthrough', 'Prime', 'Veteran', 'Twilight')),
    SquadRole          TEXT    NOT NULL CHECK (SquadRole IN
                           ('Titular', 'Rotacao', 'Reserva', 'Promessa')),
    PrimaryPosition    TEXT    NOT NULL CHECK (PrimaryPosition IN
                           ('GK', 'CB', 'FB', 'DM', 'CM', 'AM', 'WG', 'ST')),
    SecondaryPositions TEXT    NULL,                          -- pipe-separated, e.g. 'AM|ST'
    PreferredFoot      TEXT    NOT NULL CHECK (PreferredFoot IN ('Right', 'Left', 'Both')),
    WeakFootRating     INTEGER NOT NULL CHECK (WeakFootRating   BETWEEN 1 AND 5),
    SkillMovesRating   INTEGER NOT NULL CHECK (SkillMovesRating BETWEEN 1 AND 5),
    Height             INTEGER NOT NULL CHECK (Height > 0),
    BuildType          TEXT    NOT NULL CHECK (BuildType IN ('Lean', 'Balanced', 'Athletic', 'Stocky')),
    PotentialGap       INTEGER NOT NULL,
    Provenance         TEXT    NOT NULL CHECK (Provenance IN ('Anchored', 'Regen')),
    Overall            INTEGER NOT NULL CHECK (Overall BETWEEN 1 AND 99),   -- derived
    PotentialOverall   INTEGER NOT NULL,                                    -- derived
    MarketValueEur     INTEGER NOT NULL,                                    -- derived
    SalaryMonthlyBrl   INTEGER NOT NULL,                                    -- derived
    UNIQUE (ClubId, ShirtNumber)
);

CREATE TABLE CharacterAttributes (       -- 12 rows per character
    PlayerId TEXT    NOT NULL REFERENCES Characters (PlayerId) ON DELETE CASCADE,
    Attr     TEXT    NOT NULL CHECK (Attr IN
                 ('Finishing', 'Passing', 'Dribbling', 'Tackling', 'Pace', 'Strength',
                  'Stamina', 'Positioning', 'Vision', 'Composure', 'Reflexes', 'Handling')),
    Value    INTEGER NOT NULL CHECK (Value BETWEEN 1 AND 99),
    PRIMARY KEY (PlayerId, Attr)
);

-- 1:1 with Characters. A Regen player has no anchor at all: AnchorPlayerName NULL and
-- AnchorFactsVerified 0 is what makes generated players safe (ALGORITHMS.md §6.10).
CREATE TABLE CharacterDeviationAudit (
    PlayerId             TEXT PRIMARY KEY REFERENCES Characters (PlayerId) ON DELETE CASCADE,
    AnchorPlayerName     TEXT NULL,
    AnchorNationality    TEXT NULL,
    DeviationFromSurname TEXT NULL,
    GeneratedSurname     TEXT NULL,
    PhoneticSimilarity   REAL NULL,       -- range is an invariant, not a constraint (see 0008)
    DeviationMethod      TEXT NULL,       -- null for Regen: no anchor, so no deviation to describe
    AnchorFactsVerified  INTEGER NOT NULL CHECK (AnchorFactsVerified IN (0, 1))
);

CREATE INDEX IX_Characters_ClubId ON Characters (ClubId);
