-- ============================================================================
-- Migration 0019 — competitions as composition (docs/adr/0012)
--
-- One competition model replaces two. The flat 16-field Competitions row
-- (0011) and the Divisions of a country's pyramid (0014) both held members;
-- now a Competition is the DEFINITION (it outlives seasons), with its stages
-- and its transition rules, and a CompetitionSeason is one EDITION with its
-- participants. The pyramid is a view over competitions with a Level.
--
-- Converts what is unambiguous and refuses the rest (ADR-0012 §11). It runs in
-- the runner's transaction, so an abort writes nothing.
--
-- How the foreign keys are handled: the runner's connection has
-- foreign_keys = ON, which cannot be switched off inside a transaction. So no
-- table is renamed (SQLite would rewrite the child tables' references, and
-- RENAME is not portable). The old rows are COPIED into plain staging tables,
-- the old tables are dropped CHILDREN FIRST (CompetitionMembers before
-- Competitions, DivisionClubs before Divisions) so the implicit delete of a
-- DROP never meets a row that still points at it, and the new tables are built
-- from the copies. Nothing else in the schema references the four old tables.
--
-- How it aborts: Migration0019Guard has one NAMED CHECK per refusal. Inserting
-- a reason fails the statement with "CHECK constraint failed: <name>", which
-- is the message the owner reads, and the transaction rolls back.
--
-- PromotedIn/RelegatedOut keep the meaning the code always gave them
-- (ADR-0007 §3, the PYRAMID_FLOW sum): a level's PromotedIn is how many come UP
-- INTO it from the level below, its RelegatedOut how many go DOWN out of it.
-- ADR-0012 §11 step 6 stated the first one the other way round; see its
-- clarifications.
-- ============================================================================

CREATE TABLE Migration0019Guard (
    Reason TEXT NOT NULL,
    CONSTRAINT "0019 aborted: a competition that is not National cannot be represented before stages beyond the league exist (ADR-0012 §11)"
        CHECK (Reason <> 'not_national'),
    CONSTRAINT "0019 aborted: a national competition has no members to take its country from"
        CHECK (Reason <> 'country_unknown'),
    CONSTRAINT "0019 aborted: a national competition has members in more than one country"
        CHECK (Reason <> 'members_in_two_countries'),
    CONSTRAINT "0019 aborted: two national competitions in one country would both be level 1"
        CHECK (Reason <> 'two_level_one'),
    CONSTRAINT "0019 aborted: a competition's typed rounds are neither a single nor a double round robin of its clubs"
        CHECK (Reason <> 'rounds_ambiguous'),
    CONSTRAINT "0019 aborted: a division's format is not a league (LeagueSingle or LeagueDouble)"
        CHECK (Reason <> 'format_not_league'),
    CONSTRAINT "0019 aborted: a tier-1 division has clubs in a country whose imported league is level 1 — withdraw them in Ligas first"
        CHECK (Reason <> 'tier_one_division_with_clubs'),
    CONSTRAINT "0019 aborted: a division is in a country with no national competition to take its geo anchor from"
        CHECK (Reason <> 'division_without_anchor'),
    CONSTRAINT "0019 aborted: the database holds clubs or divisions but no competition to take the current season from"
        CHECK (Reason <> 'no_current_season')
);

-- ---------------------------------------------------------------- staging

CREATE TABLE OldCompetitions (
    CompetitionId   TEXT    PRIMARY KEY,
    Name            TEXT    NOT NULL,
    Scope           TEXT    NOT NULL,
    AnchorGeoNodeId TEXT    NOT NULL,
    ClubCount       INTEGER NOT NULL,
    Rounds          INTEGER NOT NULL,
    PromotedIn      INTEGER NOT NULL,
    RelegatedOut    INTEGER NOT NULL,
    EditionId       TEXT    NOT NULL,
    Season          INTEGER NOT NULL
);

INSERT INTO OldCompetitions
    (CompetitionId, Name, Scope, AnchorGeoNodeId, ClubCount, Rounds, PromotedIn, RelegatedOut, EditionId, Season)
SELECT CompetitionId, Name, Scope, AnchorGeoNodeId, ClubCount, Rounds, PromotedIn, RelegatedOut, EditionId, Season
FROM Competitions;

CREATE TABLE OldCompetitionMembers (
    CompetitionId TEXT    NOT NULL,
    ClubId        TEXT    NOT NULL,
    Ordinal       INTEGER NOT NULL
);

INSERT INTO OldCompetitionMembers (CompetitionId, ClubId, Ordinal)
SELECT CompetitionId, ClubId, Ordinal FROM CompetitionMembers;

-- The country of an old competition is the single country of its members
-- (the link ADR-0007 §4 already uses).
CREATE TABLE OldCompetitionCountry (
    CompetitionId TEXT PRIMARY KEY,
    CountryId     TEXT NOT NULL
);

INSERT INTO OldCompetitionCountry (CompetitionId, CountryId)
SELECT m.CompetitionId, MIN(c.CountryId)
FROM OldCompetitionMembers m
JOIN Clubs c ON c.ClubId = m.ClubId
GROUP BY m.CompetitionId;

-- ---------------------------------------------------------------- refusals

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'not_national' FROM OldCompetitions WHERE Scope <> 'National';

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'country_unknown' FROM OldCompetitions o
WHERE o.CompetitionId NOT IN (SELECT CompetitionId FROM OldCompetitionCountry);

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'members_in_two_countries'
FROM OldCompetitionMembers m
JOIN Clubs c ON c.ClubId = m.ClubId
GROUP BY m.CompetitionId
HAVING COUNT(DISTINCT c.CountryId) > 1;

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'two_level_one' FROM OldCompetitionCountry
GROUP BY CountryId
HAVING COUNT(*) > 1;

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'rounds_ambiguous' FROM OldCompetitions
WHERE Rounds <> (CASE WHEN ClubCount % 2 = 0 THEN ClubCount - 1 ELSE ClubCount END)
  AND Rounds <> 2 * (CASE WHEN ClubCount % 2 = 0 THEN ClubCount - 1 ELSE ClubCount END);

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'format_not_league' FROM Divisions
WHERE Format NOT IN ('LeagueSingle', 'LeagueDouble');

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'tier_one_division_with_clubs' FROM Divisions d
WHERE d.Tier = 1
  AND d.CountryId IN (SELECT CountryId FROM OldCompetitionCountry)
  AND d.DivisionId IN (SELECT DivisionId FROM DivisionClubs);

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'division_without_anchor' FROM Divisions d
WHERE d.CountryId NOT IN (SELECT CountryId FROM OldCompetitionCountry);

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'no_current_season' FROM Clubs
WHERE NOT EXISTS (SELECT 1 FROM OldCompetitions);

INSERT INTO Migration0019Guard (Reason)
SELECT DISTINCT 'no_current_season' FROM Divisions
WHERE NOT EXISTS (SELECT 1 FROM OldCompetitions);

-- ---------------------------------------------------------------- the current season

-- The largest season of the old competitions (2026 for the pilot). An empty
-- database gets it on import.
INSERT INTO WorldSettings (Key, Value)
SELECT DISTINCT 'currentSeason', CAST(Season AS TEXT) FROM OldCompetitions
WHERE Season = (SELECT MAX(Season) FROM OldCompetitions);

-- ---------------------------------------------------------------- what each level counts

-- The promotion and relegation counts by level, before the old tables go: the
-- old national competition at level 1, and every kept division at its tier.
-- A tier-1 division in a country that has an imported league is the one that
-- gets dropped (it is empty — the refusal above guarantees it), so the level-1
-- counts are the imported league's.
CREATE TABLE LevelCounts (
    CompetitionId TEXT    PRIMARY KEY,
    CountryId     TEXT    NOT NULL,
    Level         INTEGER NOT NULL,
    ClubCount     INTEGER NOT NULL,
    PromotedIn    INTEGER NOT NULL,
    RelegatedOut  INTEGER NOT NULL
);

INSERT INTO LevelCounts (CompetitionId, CountryId, Level, ClubCount, PromotedIn, RelegatedOut)
SELECT o.CompetitionId, cc.CountryId, 1, o.ClubCount, o.PromotedIn, o.RelegatedOut
FROM OldCompetitions o
JOIN OldCompetitionCountry cc ON cc.CompetitionId = o.CompetitionId;

INSERT INTO LevelCounts (CompetitionId, CountryId, Level, ClubCount, PromotedIn, RelegatedOut)
SELECT d.DivisionId, d.CountryId, d.Tier, d.ClubCount, d.PromotedIn, d.RelegatedOut
FROM Divisions d
WHERE NOT (d.Tier = 1 AND d.CountryId IN (SELECT CountryId FROM OldCompetitionCountry));

CREATE TABLE KeptDivisions (
    DivisionId TEXT    PRIMARY KEY,
    CountryId  TEXT    NOT NULL,
    Tier       INTEGER NOT NULL,
    Name       TEXT    NOT NULL,
    ClubCount  INTEGER NOT NULL,
    Legs       INTEGER NOT NULL,
    SeasonId   TEXT    NOT NULL
);

INSERT INTO KeptDivisions (DivisionId, CountryId, Tier, Name, ClubCount, Legs, SeasonId)
SELECT d.DivisionId, d.CountryId, d.Tier, d.Name, d.ClubCount,
       CASE d.Format WHEN 'LeagueSingle' THEN 1 ELSE 2 END,
       'edt_' || d.DivisionId || '_' || CAST((SELECT MAX(Season) FROM OldCompetitions) AS TEXT)
FROM Divisions d
WHERE d.DivisionId IN (SELECT CompetitionId FROM LevelCounts);

CREATE TABLE KeptDivisionClubs (
    DivisionId TEXT    NOT NULL,
    ClubId     TEXT    NOT NULL,
    Ordinal    INTEGER NOT NULL
);

INSERT INTO KeptDivisionClubs (DivisionId, ClubId, Ordinal)
SELECT dc.DivisionId, dc.ClubId, dc.Ordinal
FROM DivisionClubs dc
WHERE dc.DivisionId IN (SELECT DivisionId FROM KeptDivisions);

-- ---------------------------------------------------------------- drop the old model, children first

DROP TABLE CompetitionMembers;
DROP TABLE Competitions;
DROP TABLE DivisionClubs;
DROP TABLE Divisions;

-- ---------------------------------------------------------------- the new model

-- The definition. CountryId is the ISO code, required for a national or
-- sub-national scope and absent otherwise; Level is set only on a national
-- league in its country's pyramid. Deliberately no foreign key to Countries,
-- like Clubs.CountryId: undo rewrites the countries, and a cascade from there
-- would take the competitions with it.
CREATE TABLE Competitions (
    CompetitionId   TEXT    PRIMARY KEY,
    Name            TEXT    NOT NULL,
    Scope           TEXT    NOT NULL CHECK (Scope IN
                        ('SubNational', 'National', 'SubContinentalZonal',
                         'Continental', 'Intercontinental')),
    AnchorGeoNodeId TEXT    NOT NULL REFERENCES GeoNodes (GeoNodeId) ON DELETE RESTRICT,
    CountryId       TEXT    NULL,
    Level           INTEGER NULL CHECK (Level IS NULL OR Level >= 1),
    ClubCount       INTEGER NOT NULL CHECK (ClubCount >= 2),
    CHECK ((Scope IN ('SubNational', 'National') AND CountryId IS NOT NULL)
        OR (Scope NOT IN ('SubNational', 'National') AND CountryId IS NULL)),
    CHECK (Level IS NULL OR Scope = 'National'),
    -- One league per level per country. A duplicate level is not a bigger
    -- league, it is two leagues that both claim to be the second division.
    UNIQUE (CountryId, Level)
);

-- How a competition is played. Only the league stage exists (ADR-0012 §4).
CREATE TABLE CompetitionStages (
    CompetitionId TEXT    NOT NULL REFERENCES Competitions (CompetitionId) ON DELETE CASCADE,
    Ordinal       INTEGER NOT NULL CHECK (Ordinal >= 1),
    Kind          TEXT    NOT NULL CHECK (Kind IN ('League')),
    Legs          INTEGER NOT NULL CHECK (Legs IN (1, 2)),
    PRIMARY KEY (CompetitionId, Ordinal)
);

-- One edition. The tool writes only the world's current season (ADR-0012 §3).
CREATE TABLE CompetitionSeasons (
    SeasonId      TEXT    PRIMARY KEY,
    CompetitionId TEXT    NOT NULL REFERENCES Competitions (CompetitionId) ON DELETE CASCADE,
    Year          INTEGER NOT NULL,
    UNIQUE (CompetitionId, Year)
);

-- The participants, in their authored order.
CREATE TABLE SeasonParticipants (
    SeasonId TEXT    NOT NULL REFERENCES CompetitionSeasons (SeasonId) ON DELETE CASCADE,
    ClubId   TEXT    NOT NULL REFERENCES Clubs (ClubId) ON DELETE CASCADE,
    Ordinal  INTEGER NOT NULL,
    PRIMARY KEY (SeasonId, ClubId)
);

-- Where the clubs of a finished season go. The target reference has no action
-- on purpose: inside the tool's write transactions foreign keys are deferred to
-- COMMIT, so a level and the rules pointing at it can change together.
CREATE TABLE TransitionRules (
    CompetitionId       TEXT    NOT NULL REFERENCES Competitions (CompetitionId) ON DELETE CASCADE,
    RankFrom            INTEGER NOT NULL,
    RankTo              INTEGER NOT NULL,
    TargetCompetitionId TEXT    NOT NULL REFERENCES Competitions (CompetitionId),
    PRIMARY KEY (CompetitionId, RankFrom),
    CHECK (RankFrom >= 1 AND RankFrom <= RankTo),
    CHECK (TargetCompetitionId <> CompetitionId)
);

CREATE INDEX IX_Competitions_Country       ON Competitions (CountryId);
CREATE INDEX IX_SeasonParticipants_Club    ON SeasonParticipants (ClubId);
CREATE INDEX IX_TransitionRules_Target     ON TransitionRules (TargetCompetitionId);

-- ---------------------------------------------------------------- conversion

-- Each old national competition: level 1, one league stage, its edition as a season.
INSERT INTO Competitions (CompetitionId, Name, Scope, AnchorGeoNodeId, CountryId, Level, ClubCount)
SELECT o.CompetitionId, o.Name, o.Scope, o.AnchorGeoNodeId, cc.CountryId, 1, o.ClubCount
FROM OldCompetitions o
JOIN OldCompetitionCountry cc ON cc.CompetitionId = o.CompetitionId;

INSERT INTO CompetitionStages (CompetitionId, Ordinal, Kind, Legs)
SELECT o.CompetitionId, 1, 'League',
       CASE WHEN o.Rounds = 2 * (CASE WHEN o.ClubCount % 2 = 0 THEN o.ClubCount - 1 ELSE o.ClubCount END)
            THEN 2 ELSE 1 END
FROM OldCompetitions o;

INSERT INTO CompetitionSeasons (SeasonId, CompetitionId, Year)
SELECT o.EditionId, o.CompetitionId, o.Season FROM OldCompetitions o;

INSERT INTO SeasonParticipants (SeasonId, ClubId, Ordinal)
SELECT o.EditionId, m.ClubId, m.Ordinal
FROM OldCompetitionMembers m
JOIN OldCompetitions o ON o.CompetitionId = m.CompetitionId;

-- Each kept division: same id, Level = Tier, its anchor from its country's
-- national competition, one league stage from its format, its clubs as the
-- participants of the current season.
INSERT INTO Competitions (CompetitionId, Name, Scope, AnchorGeoNodeId, CountryId, Level, ClubCount)
SELECT d.DivisionId, d.Name, 'National',
       (SELECT o.AnchorGeoNodeId
        FROM OldCompetitions o
        JOIN OldCompetitionCountry cc ON cc.CompetitionId = o.CompetitionId
        WHERE cc.CountryId = d.CountryId),
       d.CountryId, d.Tier, d.ClubCount
FROM KeptDivisions d;

INSERT INTO CompetitionStages (CompetitionId, Ordinal, Kind, Legs)
SELECT k.DivisionId, 1, 'League', k.Legs FROM KeptDivisions k;

INSERT INTO CompetitionSeasons (SeasonId, CompetitionId, Year)
SELECT k.SeasonId, k.DivisionId, (SELECT MAX(Season) FROM OldCompetitions) FROM KeptDivisions k;

INSERT INTO SeasonParticipants (SeasonId, ClubId, Ordinal)
SELECT k.SeasonId, c.ClubId, c.Ordinal
FROM KeptDivisionClubs c
JOIN KeptDivisions k ON k.DivisionId = c.DivisionId;

-- Rules between adjacent levels of a country. Counts with no level below to
-- point at (the bottom level's, and the imported league's when there is no
-- level 2) are dropped. The two directions stay separate, as they were, and
-- PYRAMID_FLOW reports any imbalance they carry.

-- RelegatedOut of t: the bottom ranks of t go down to t+1.
INSERT INTO TransitionRules (CompetitionId, RankFrom, RankTo, TargetCompetitionId)
SELECT upper.CompetitionId, upper.ClubCount - upper.RelegatedOut + 1, upper.ClubCount, lower.CompetitionId
FROM LevelCounts upper
JOIN LevelCounts lower ON lower.CountryId = upper.CountryId AND lower.Level = upper.Level + 1
WHERE upper.RelegatedOut > 0;

-- PromotedIn of t: the top ranks of t+1 come up into t.
INSERT INTO TransitionRules (CompetitionId, RankFrom, RankTo, TargetCompetitionId)
SELECT lower.CompetitionId, 1, upper.PromotedIn, upper.CompetitionId
FROM LevelCounts upper
JOIN LevelCounts lower ON lower.CountryId = upper.CountryId AND lower.Level = upper.Level + 1
WHERE upper.PromotedIn > 0;

-- ---------------------------------------------------------------- undo history

-- Snapshots taken before this migration are in a shape the reader no longer
-- reads (ADR-0012 §10). The edit trail stays; its link to an act becomes
-- "not known to belong to an act", which 0017 already defines as null.
DELETE FROM WorldHistory;
UPDATE WorldEdits SET HistoryId = NULL;

-- ---------------------------------------------------------------- clean up

DROP TABLE KeptDivisionClubs;
DROP TABLE KeptDivisions;
DROP TABLE LevelCounts;
DROP TABLE OldCompetitionCountry;
DROP TABLE OldCompetitionMembers;
DROP TABLE OldCompetitions;
DROP TABLE Migration0019Guard;
