-- ============================================================================
-- Migration 0006 — world geography (ClubIdentity v2 schema)
--
-- First of the world-authoring migrations (0006-0011). These tables COEXIST with
-- the legacy Leagues/Teams/Players schema rather than replacing it; a derived
-- projection bridges the two in Sprint 6. See docs/adr/0002-clubidentity-v2-coexistence.md.
--
-- Geography is a hierarchy here, not the legacy `Leagues.Country TEXT` field:
-- World -> Confederation -> SubRegion -> Country -> Region -> City. Clubs and
-- competitions anchor to a node and the tree is walked upward for breadcrumbs.
--
-- The Kind CHECK is deliberate and load-bearing: the spreadsheet extraction once
-- produced a legend row as a GeoNode with no Kind and no DisplayName. Both this
-- constraint and the importer reject that row with a message instead of storing it
-- (DATA_CONTRACT.md §5).
-- ============================================================================

CREATE TABLE GeoNodes (
    GeoNodeId   TEXT PRIMARY KEY,
    Kind        TEXT NOT NULL CHECK (Kind IN
                    ('World', 'Confederation', 'SubRegion', 'Country', 'Region', 'City')),
    ParentId    TEXT NULL REFERENCES GeoNodes (GeoNodeId) ON DELETE RESTRICT,
    DisplayName TEXT NOT NULL CHECK (length(DisplayName) > 0)
);

CREATE INDEX IX_GeoNodes_ParentId ON GeoNodes (ParentId);
