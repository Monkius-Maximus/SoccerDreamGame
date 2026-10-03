-- ============================================================================
-- Migration 0018 — club provenance, and the club-generation profiles
--
-- A club is Anchored (derived from a real one, with a ClubDeviationAudit row) or
-- Regen (generated from nothing, with no audit row) — ADR-0011 §2. Core computes
-- the provenance from the audit; the database stores it explicitly, because a
-- missing audit row alone cannot tell "Regen" from "an anchored club whose audit
-- was lost". The repository reads both and throws when they disagree.
--
-- The DEFAULT exists only because SQLite needs one to add a NOT NULL column: it
-- marks every club written before this migration, all of which have an audit row,
-- as Anchored. The repository always writes the column explicitly.
--
-- ClubProfileDocuments holds one validated club_profiles document per country
-- (`worldbuilder import-club-profiles`). It is kept as the document rather than
-- normalised: it has some two dozen sections, each carrying its own source, and
-- the generator only ever reads it whole, through the same reader that validated
-- it on import.
-- ============================================================================

ALTER TABLE Clubs ADD COLUMN Provenance TEXT NOT NULL DEFAULT 'Anchored'
    CHECK (Provenance IN ('Anchored', 'Regen'));

CREATE TABLE ClubProfileDocuments (
    CountryId TEXT PRIMARY KEY,
    Document  TEXT NOT NULL
);
