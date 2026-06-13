-- ============================================================================
-- Migration 0001 — initial schema
-- The four GDD-named tables (Leagues, Teams, Players, PlayerTraits) plus Seasons
-- and the player<->trait link table.
--
-- Portability notes (for the future Postgres/Turso multiplayer backend):
--   * INTEGER PRIMARY KEY is SQLite's rowid alias (auto-increments). Under
--     Postgres this becomes `GENERATED ALWAYS AS IDENTITY` — handled by the
--     migration runner / connection layer, never in app SQL.
--   * Timestamps are TEXT in ISO-8601 ("YYYY-MM-DD HH:MM:SS"); booleans are
--     INTEGER 0/1. Both map 1:1 to Postgres TIMESTAMP / BOOLEAN.
--   * FKs and CHECK constraints are standard SQL and carry over unchanged.
--     Remember: SQLite needs `PRAGMA foreign_keys = ON;` per connection.
-- ============================================================================

CREATE TABLE Leagues (
    Id              INTEGER PRIMARY KEY,
    Name            TEXT    NOT NULL,
    Country         TEXT    NOT NULL,
    Tier            INTEGER NOT NULL CHECK (Tier IN (1, 2, 3)),  -- SimulationTier (LOD)
    CurrentSeasonId INTEGER NULL                                  -- soft pointer; no FK (circular)
);

CREATE TABLE Seasons (
    Id        INTEGER PRIMARY KEY,
    LeagueId  INTEGER NOT NULL,
    StartDate TEXT    NOT NULL,
    EndDate   TEXT    NOT NULL,
    FOREIGN KEY (LeagueId) REFERENCES Leagues (Id) ON DELETE CASCADE
);

CREATE TABLE Teams (
    Id        INTEGER PRIMARY KEY,
    Name      TEXT    NOT NULL,
    LeagueId  INTEGER NOT NULL,
    Budget    INTEGER NOT NULL DEFAULT 0,
    EloRating INTEGER NOT NULL DEFAULT 1500,    -- Tier 2 resolver input
    FOREIGN KEY (LeagueId) REFERENCES Leagues (Id) ON DELETE CASCADE
);

CREATE TABLE Players (
    Id        INTEGER PRIMARY KEY,
    FirstName TEXT    NOT NULL,
    LastName  TEXT    NOT NULL,
    TeamId    INTEGER NULL,
    -- STATIC base attributes (1..20); read-only during a season.
    Pace      INTEGER NOT NULL CHECK (Pace     BETWEEN 1 AND 20),
    Stamina   INTEGER NOT NULL CHECK (Stamina  BETWEEN 1 AND 20),
    Strength  INTEGER NOT NULL CHECK (Strength BETWEEN 1 AND 20),
    Passing   INTEGER NOT NULL CHECK (Passing  BETWEEN 1 AND 20),
    Shooting  INTEGER NOT NULL CHECK (Shooting BETWEEN 1 AND 20),
    Tackling  INTEGER NOT NULL CHECK (Tackling BETWEEN 1 AND 20),
    Vision    INTEGER NOT NULL CHECK (Vision   BETWEEN 1 AND 20),
    FOREIGN KEY (TeamId) REFERENCES Teams (Id) ON DELETE SET NULL
);

-- Catalogue of STATIC personality traits (read-only during the season).
CREATE TABLE PlayerTraits (
    Id              INTEGER PRIMARY KEY,
    Key             TEXT    NOT NULL UNIQUE,                 -- e.g. 'hot_headed'
    DisplayName     TEXT    NOT NULL,
    Aggression      INTEGER NOT NULL CHECK (Aggression  BETWEEN 0 AND 100),
    Selfishness     INTEGER NOT NULL CHECK (Selfishness BETWEEN 0 AND 100),
    EventWeightBias INTEGER NOT NULL DEFAULT 0              -- nudges off-pitch event probability
);

-- Many-to-many: which static traits a player was generated with.
CREATE TABLE PlayerTraitAssignments (
    PlayerId INTEGER NOT NULL,
    TraitId  INTEGER NOT NULL,
    PRIMARY KEY (PlayerId, TraitId),
    FOREIGN KEY (PlayerId) REFERENCES Players (Id)      ON DELETE CASCADE,
    FOREIGN KEY (TraitId)  REFERENCES PlayerTraits (Id) ON DELETE CASCADE
);

CREATE INDEX IX_Seasons_LeagueId ON Seasons (LeagueId);
CREATE INDEX IX_Teams_LeagueId   ON Teams (LeagueId);
CREATE INDEX IX_Players_TeamId   ON Players (TeamId);
