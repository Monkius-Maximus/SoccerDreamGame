-- ============================================================================
-- Migration 0012 — editing: concurrency tokens and the edit log
--
-- Sprint 4 turns the tool into the source of truth, which needs two things the
-- read-only sprints did not.
--
-- 1. RowVersion. Every club and character carries a version that the writer
--    increments. A caller patches with the version it read; if the row moved on
--    since, the write is refused with 409 rather than quietly overwriting work
--    someone (or another tab) did in between. This is a token, not a timestamp:
--    two edits inside the same clock tick still conflict.
--
-- 2. WorldEdits. An append-only record of every field change: what changed, from
--    what, to what, and whether it has been exported yet. The pending counter in
--    the top bar is COUNT(*) WHERE ExportedAt IS NULL, and Sprint 7's export is
--    what stamps it. It is also, deliberately, a provenance trail: this project's
--    whole premise is that a number without a source does not enter, and an edit
--    without a record is the same problem one step later.
--
-- Note: the ALTERs use a literal default so existing rows get version 1 rather
-- than NULL. SQLite rewrites nothing; both are O(1) schema changes.
-- ============================================================================

ALTER TABLE Clubs      ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Characters ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 1;

CREATE TABLE WorldEdits (
    Id         INTEGER PRIMARY KEY,
    EntityType TEXT NOT NULL CHECK (EntityType IN ('Club', 'Character')),
    EntityId   TEXT NOT NULL,
    FieldPath  TEXT NOT NULL,          -- e.g. 'kits.home.shirt'
    OldValue   TEXT NULL,              -- null is a real value here, not "unknown"
    NewValue   TEXT NULL,
    EditedAt   TEXT NOT NULL,          -- ISO-8601, as everywhere else in the schema
    ExportedAt TEXT NULL               -- null = still pending export
);

CREATE INDEX IX_WorldEdits_Pending ON WorldEdits (ExportedAt);
CREATE INDEX IX_WorldEdits_Entity  ON WorldEdits (EntityType, EntityId);
