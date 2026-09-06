-- ============================================================================
-- Migration 0011 — competitions (world-authoring)
--
-- Authored competition data: scope, prestige, promotion/relegation, continental
-- slots, and the frozen member list of an edition. NOT the legacy Leagues/Seasons
-- pair the MatchEngine consumes (docs/adr/0002).
--
-- Nothing here implies a simulated round. There is no standings table and no result:
-- the tool sorts members by squad strength and says so on screen. Keep it that way
-- (design_handoff README, "Aviso de escopo").
--
-- LeagueTierFloat is REAL, not the legacy Tier IN (1,2,3): the authoring model uses a
-- continuous tier (0.86 for the current batch) that only collapses to an integer when
-- projected into the legacy schema in Sprint 6.
-- ============================================================================

CREATE TABLE Competitions (
    CompetitionId      TEXT PRIMARY KEY,
    Name               TEXT    NOT NULL,
    Scope              TEXT    NOT NULL CHECK (Scope IN
                           ('SubNational', 'National', 'SubContinentalZonal',
                            'Continental', 'Intercontinental')),
    AnchorGeoNodeId    TEXT    NOT NULL REFERENCES GeoNodes (GeoNodeId) ON DELETE RESTRICT,
    MemberPredicateId  TEXT    NOT NULL,
    PrestigeBand       TEXT    NOT NULL CHECK (PrestigeBand IN ('B1','B2','B3','B4','B5','B6')),
    LeagueTierFloat    REAL    NOT NULL,
    Format             TEXT    NOT NULL,
    ClubCount          INTEGER NOT NULL,
    Rounds             INTEGER NOT NULL,
    PromotedIn         INTEGER NOT NULL,
    RelegatedOut       INTEGER NOT NULL,
    ContinentalSlots   TEXT    NOT NULL,
    EditionId          TEXT    NOT NULL,
    Season             INTEGER NOT NULL
);

-- The frozen member list of an edition. Ordinal preserves the authored order, which
-- is meaningful (it is the order the source spreadsheet froze), so re-reading a
-- competition returns its members exactly as written.
CREATE TABLE CompetitionMembers (
    CompetitionId TEXT    NOT NULL REFERENCES Competitions (CompetitionId) ON DELETE CASCADE,
    ClubId        TEXT    NOT NULL REFERENCES Clubs (ClubId) ON DELETE CASCADE,
    Ordinal       INTEGER NOT NULL,
    PRIMARY KEY (CompetitionId, ClubId)
);

CREATE INDEX IX_CompetitionMembers_ClubId ON CompetitionMembers (ClubId);
