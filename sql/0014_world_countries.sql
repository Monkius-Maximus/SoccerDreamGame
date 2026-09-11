-- ============================================================================
-- Migration 0014 — countries and the league pyramid (world-authoring)
--
-- None of this existed while the world had exactly one country and one
-- competition. It is what starts hurting the moment a second one is possible
-- (ROADMAP.md Sprint 9).
--
-- Two things worth reading before changing this file:
--
--   * Divisions are NOT Competitions. A Competition row is the FROZEN EDITION of
--     a competition — its member list, its season, its prestige band. A Division
--     is the standing structure editions hang off: it outlives a season, and it
--     is what promotion and relegation connect. Collapsing the two makes
--     "who came up last year" unanswerable.
--
--   * Rounds and match counts are NOT stored. They derive from the format and
--     the field size (Core/World/Competitions/CompetitionFormats), because a
--     typed round count is a number with no source.
--
-- NationalityMix has no DEFAULT and no null state: a country row without shares
-- is a country that cannot be populated, and the audit reports it as an error
-- rather than the generator inventing a distribution.
-- ============================================================================

-- CountryId is the ISO code the batch already uses ("BRA"), NOT a GeoNodes id
-- ("geo_bra"). Clubs carry the code in geography.countryId and the calibration keys
-- its stadium profiles by the same code, so this table joins to those. There is no
-- foreign key because there is no table of ISO codes to point at; the geo tree holds
-- the country's NAME, reached by walking up from a club's node.
CREATE TABLE Countries (
    CountryId           TEXT    PRIMARY KEY,
    Currency            TEXT    NOT NULL,
    EurToLocal          REAL    NOT NULL CHECK (EurToLocal > 0),
    WageFloorMonthly    INTEGER NOT NULL CHECK (WageFloorMonthly >= 0),
    -- Null means "measured from the batch itself", which is true of Brazil and is
    -- deliberately not the same thing as having a source.
    NationalityMixSource TEXT   NULL
);

CREATE TABLE CountryNationalities (
    CountryId   TEXT NOT NULL REFERENCES Countries (CountryId) ON DELETE CASCADE,
    Nationality TEXT NOT NULL,
    Share       REAL NOT NULL CHECK (Share > 0 AND Share <= 1),
    PRIMARY KEY (CountryId, Nationality)
);

CREATE TABLE Divisions (
    DivisionId   TEXT    PRIMARY KEY,
    CountryId    TEXT    NOT NULL REFERENCES Countries (CountryId) ON DELETE CASCADE,
    Tier         INTEGER NOT NULL CHECK (Tier >= 1),
    Name         TEXT    NOT NULL,
    Format       TEXT    NOT NULL CHECK (Format IN
                     ('LeagueSingle', 'LeagueDouble', 'GroupsKnockout', 'KnockoutOnly', 'NationalCup')),
    ClubCount    INTEGER NOT NULL CHECK (ClubCount >= 2),
    PromotedIn   INTEGER NOT NULL CHECK (PromotedIn >= 0),
    RelegatedOut INTEGER NOT NULL CHECK (RelegatedOut >= 0),
    -- One division per tier per country. A duplicate tier is not a bigger league,
    -- it is two leagues that both claim to be the second division.
    UNIQUE (CountryId, Tier)
);

-- A club plays one division per country. The primary key enforces "once per
-- division"; "once per country" needs the whole set and lives in PyramidRules,
-- for the same reason the club invariants are not CHECK constraints (ADR-0003).
CREATE TABLE DivisionClubs (
    DivisionId TEXT    NOT NULL REFERENCES Divisions (DivisionId) ON DELETE CASCADE,
    ClubId     TEXT    NOT NULL REFERENCES Clubs (ClubId) ON DELETE CASCADE,
    Ordinal    INTEGER NOT NULL,
    PRIMARY KEY (DivisionId, ClubId)
);

CREATE INDEX IX_Divisions_Country   ON Divisions (CountryId);
CREATE INDEX IX_DivisionClubs_Club  ON DivisionClubs (ClubId);
