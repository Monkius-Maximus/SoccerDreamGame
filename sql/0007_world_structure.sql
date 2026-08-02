-- ============================================================================
-- Migration 0007 — nations, stadiums, competitions, coaching staff, contracts
--
-- On naming: `Leagues` is NOT renamed. It is the fixture-routing unit — Matches
-- point at it and Leagues.Tier drives the whole LOD path (SqliteFixtureGateway,
-- SimulationLODManager) — which makes it a DIVISION, not a tournament. The
-- tournament above it is the new `Competitions` table, and a division now points
-- up at one. Renaming Leagues to Competitions would have put the wrong name on
-- the wrong concept; the table keeps its name and gains a parent.
--
-- Portability notes (unchanged house rules):
--   * SQLite's ALTER TABLE ADD COLUMN cannot carry UNIQUE, hence the separate
--     indexes; partial unique indexes exist in Postgres too.
--   * A column added with a REFERENCES clause must be nullable under SQLite when
--     foreign keys are on — every FK column added below is.
--   * Enum-ish columns are TEXT + CHECK rather than INTEGER, so a schema dump is
--     readable and the constraint survives a Postgres port verbatim.
-- ============================================================================

CREATE TABLE Nations (
    Id            INTEGER PRIMARY KEY,
    Key           TEXT    NOT NULL UNIQUE,
    Name          TEXT    NOT NULL,
    Code          TEXT    NOT NULL UNIQUE CHECK (length(Code) = 3),   -- 'BRA', 'ESP'
    Adjective     TEXT    NULL,                                       -- 'Brazilian'
    Confederation TEXT    NULL,                                       -- 'CONMEBOL'
    Reputation    INTEGER NOT NULL DEFAULT 50 CHECK (Reputation BETWEEN 0 AND 100)
);

CREATE TABLE Stadiums (
    Id           INTEGER PRIMARY KEY,
    Key          TEXT    NOT NULL UNIQUE,
    Name         TEXT    NOT NULL,
    NationId     INTEGER NULL,
    City         TEXT    NULL,
    Capacity     INTEGER NOT NULL DEFAULT 0   CHECK (Capacity >= 0),
    -- Pitch dimensions feed PitchGeometry; the laws of the game bound them.
    PitchLengthM INTEGER NOT NULL DEFAULT 105 CHECK (PitchLengthM BETWEEN 90 AND 120),
    PitchWidthM  INTEGER NOT NULL DEFAULT 68  CHECK (PitchWidthM  BETWEEN 45 AND 90),
    Surface      TEXT    NOT NULL DEFAULT 'grass'
                         CHECK (Surface IN ('grass', 'artificial', 'hybrid')),
    YearBuilt    INTEGER NULL,
    FOREIGN KEY (NationId) REFERENCES Nations (Id) ON DELETE SET NULL
);

-- The tournament. A 'league' competition owns one or more divisions (rows in
-- Leagues); a 'cup' competition owns exactly one, so cup fixtures still have a
-- LeagueId to route on. That invariant is enforced by the content validator
-- rather than by SQL, because it spans two tables.
CREATE TABLE Competitions (
    Id         INTEGER PRIMARY KEY,
    Key        TEXT    NOT NULL UNIQUE,
    Name       TEXT    NOT NULL,
    ShortName  TEXT    NULL,
    NationId   INTEGER NULL,                    -- NULL = continental / international
    Format     TEXT    NOT NULL DEFAULT 'league' CHECK (Format IN ('league', 'cup')),
    Scope      TEXT    NOT NULL DEFAULT 'domestic'
                       CHECK (Scope IN ('domestic', 'continental', 'international')),
    Reputation INTEGER NOT NULL DEFAULT 50 CHECK (Reputation BETWEEN 0 AND 100),
    PointsWin  INTEGER NOT NULL DEFAULT 3 CHECK (PointsWin  >= 0),
    PointsDraw INTEGER NOT NULL DEFAULT 1 CHECK (PointsDraw >= 0),
    FOREIGN KEY (NationId) REFERENCES Nations (Id) ON DELETE SET NULL
);

-- Leagues become divisions within a competition.
ALTER TABLE Leagues ADD COLUMN CompetitionId   INTEGER NULL REFERENCES Competitions (Id);
ALTER TABLE Leagues ADD COLUMN NationId        INTEGER NULL REFERENCES Nations (Id);
ALTER TABLE Leagues ADD COLUMN PyramidLevel    INTEGER NOT NULL DEFAULT 1 CHECK (PyramidLevel BETWEEN 1 AND 10);
ALTER TABLE Leagues ADD COLUMN PromotionSlots  INTEGER NOT NULL DEFAULT 0 CHECK (PromotionSlots  >= 0);
ALTER TABLE Leagues ADD COLUMN RelegationSlots INTEGER NOT NULL DEFAULT 0 CHECK (RelegationSlots >= 0);

ALTER TABLE Teams ADD COLUMN ShortName   TEXT    NULL;
ALTER TABLE Teams ADD COLUMN NationId    INTEGER NULL REFERENCES Nations (Id);
ALTER TABLE Teams ADD COLUMN StadiumId   INTEGER NULL REFERENCES Stadiums (Id);
ALTER TABLE Teams ADD COLUMN FoundedYear INTEGER NULL;
ALTER TABLE Teams ADD COLUMN Reputation  INTEGER NOT NULL DEFAULT 50 CHECK (Reputation BETWEEN 0 AND 100);

-- Player identity beyond a name. Role/Flank deliberately mirror the lean
-- PlayerRole + FormationSlot vocabulary in SoccerSim.Core.Tactics instead of
-- importing a 14-position taxonomy the on-pitch AI has no way to interpret.
ALTER TABLE Players ADD COLUMN DateOfBirth   TEXT    NULL;              -- ISO 'YYYY-MM-DD'
ALTER TABLE Players ADD COLUMN NationId      INTEGER NULL REFERENCES Nations (Id);
ALTER TABLE Players ADD COLUMN PreferredFoot TEXT    NOT NULL DEFAULT 'Right'
                                             CHECK (PreferredFoot IN ('Left', 'Right', 'Both'));
ALTER TABLE Players ADD COLUMN PrimaryRole   TEXT    NOT NULL DEFAULT 'Midfielder'
                                             CHECK (PrimaryRole IN ('Goalkeeper', 'Defender', 'Midfielder', 'Forward'));
ALTER TABLE Players ADD COLUMN Flank         TEXT    NOT NULL DEFAULT 'Centre'
                                             CHECK (Flank IN ('Left', 'Centre', 'Right'));
ALTER TABLE Players ADD COLUMN SquadNumber   INTEGER NULL CHECK (SquadNumber IS NULL OR SquadNumber BETWEEN 1 AND 99);
ALTER TABLE Players ADD COLUMN HeightCm      INTEGER NULL CHECK (HeightCm IS NULL OR HeightCm BETWEEN 140 AND 220);

CREATE TABLE Coaches (
    Id                INTEGER PRIMARY KEY,
    Key               TEXT    NOT NULL UNIQUE,
    FirstName         TEXT    NOT NULL,
    LastName          TEXT    NOT NULL,
    TeamId            INTEGER NULL,
    NationId          INTEGER NULL,
    Role              TEXT    NOT NULL CHECK (Role IN
                        ('head_coach', 'assistant_coach', 'goalkeeping_coach',
                         'fitness_coach', 'physio', 'scout', 'director')),
    DateOfBirth       TEXT    NULL,
    -- Same 1..20 scale as player attributes, for one mental model across the game.
    Coaching          INTEGER NOT NULL DEFAULT 10 CHECK (Coaching          BETWEEN 1 AND 20),
    TacticalKnowledge INTEGER NOT NULL DEFAULT 10 CHECK (TacticalKnowledge BETWEEN 1 AND 20),
    ManManagement     INTEGER NOT NULL DEFAULT 10 CHECK (ManManagement     BETWEEN 1 AND 20),
    Fitness           INTEGER NOT NULL DEFAULT 10 CHECK (Fitness           BETWEEN 1 AND 20),
    Scouting          INTEGER NOT NULL DEFAULT 10 CHECK (Scouting          BETWEEN 1 AND 20),
    -- Feeds TeamTactics when this coach picks the side.
    PreferredMentality TEXT   NOT NULL DEFAULT 'Balanced' CHECK (PreferredMentality IN
                        ('VeryDefensive', 'Defensive', 'Balanced', 'Attacking', 'VeryAttacking')),
    FOREIGN KEY (TeamId)   REFERENCES Teams (Id)   ON DELETE SET NULL,
    FOREIGN KEY (NationId) REFERENCES Nations (Id) ON DELETE SET NULL
);

CREATE TABLE Contracts (
    Id            INTEGER PRIMARY KEY,
    Key           TEXT    NOT NULL UNIQUE,
    PlayerId      INTEGER NULL,
    CoachId       INTEGER NULL,
    TeamId        INTEGER NOT NULL,
    StartDate     TEXT    NOT NULL,
    EndDate       TEXT    NOT NULL,
    WeeklyWage    INTEGER NOT NULL DEFAULT 0 CHECK (WeeklyWage   >= 0),
    SigningBonus  INTEGER NOT NULL DEFAULT 0 CHECK (SigningBonus >= 0),
    ReleaseClause INTEGER NULL CHECK (ReleaseClause IS NULL OR ReleaseClause >= 0),
    SquadStatus   TEXT    NOT NULL DEFAULT 'squad' CHECK (SquadStatus IN
                    ('key', 'first_team', 'squad', 'rotation', 'prospect', 'youth')),
    -- Exactly one subject. `<>` on two booleans is standard SQL XOR and ports as-is.
    CHECK ((PlayerId IS NULL) <> (CoachId IS NULL)),
    CHECK (EndDate > StartDate),
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE,
    FOREIGN KEY (CoachId)  REFERENCES Coaches (Id) ON DELETE CASCADE,
    FOREIGN KEY (TeamId)   REFERENCES Teams (Id)   ON DELETE CASCADE
);

CREATE INDEX IX_Stadiums_NationId    ON Stadiums (NationId);
CREATE INDEX IX_Competitions_Nation  ON Competitions (NationId);
CREATE INDEX IX_Leagues_Competition  ON Leagues (CompetitionId);
CREATE INDEX IX_Teams_StadiumId      ON Teams (StadiumId);
CREATE INDEX IX_Coaches_TeamId       ON Coaches (TeamId);
CREATE INDEX IX_Contracts_TeamId     ON Contracts (TeamId);
CREATE INDEX IX_Contracts_PlayerId   ON Contracts (PlayerId);
CREATE INDEX IX_Contracts_CoachId    ON Contracts (CoachId);
