-- ============================================================================
-- Migration 0017 — an edit knows which act it was part of
--
-- WorldEdits (migration 0012) is the provenance trail: one row per field
-- change. WorldHistory (0015) is the undo stack: one labelled snapshot per act.
-- They were written in the same instant by the same endpoint and had no link
-- between them, which was invisible while every act produced one edit — and
-- stops being invisible at the CSV import, which records ONE edit per changed
-- row under ONE snapshot. A screen without this link shows a single import as
-- several hundred unexplained rows.
--
-- Deliberately NOT a foreign key. The undo stack is capped at 25 and trims its
-- oldest entries, so an edit reliably outlives the snapshot it belongs to: the
-- id here is a correlation, and "that act is too old to return to" is a normal
-- state of this table, not a broken reference.
--
-- NULL for every row written before this migration, and for anything that ever
-- records an edit without taking a snapshot first. Null means "not known to
-- belong to an act", which is the truth about those rows.
-- ============================================================================

ALTER TABLE WorldEdits ADD COLUMN HistoryId INTEGER NULL;

CREATE INDEX IX_WorldEdits_History ON WorldEdits (HistoryId);
