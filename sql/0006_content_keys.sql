-- ============================================================================
-- Migration 0006 — stable content keys + the content-build stamp
-- Authored rows gain a human-readable key. The INTEGER id stays the runtime and
-- foreign-key currency; the key is the identity that survives a re-export, a
-- branch merge, or a rebuild of the content bundle. PlayerTraits and HousingItems
-- already had one (0001/0004), so only the four remaining authored tables change.
--
-- Portability notes (for the future Postgres/Turso backend):
--   * SQLite's ALTER TABLE ADD COLUMN cannot carry UNIQUE, hence the separate
--     partial unique index. Partial indexes exist in both SQLite 3.8+ and
--     Postgres, so `WHERE Key IS NOT NULL` carries over unchanged.
--   * Key is nullable because rows created by the running game (future seasons,
--     generated players) have no authored identity. Only authored rows carry one.
-- ============================================================================

ALTER TABLE Leagues ADD COLUMN Key TEXT NULL;
ALTER TABLE Teams   ADD COLUMN Key TEXT NULL;
ALTER TABLE Players ADD COLUMN Key TEXT NULL;
ALTER TABLE Seasons ADD COLUMN Key TEXT NULL;
ALTER TABLE Matches ADD COLUMN Key TEXT NULL;

CREATE UNIQUE INDEX UX_Leagues_Key ON Leagues (Key) WHERE Key IS NOT NULL;
CREATE UNIQUE INDEX UX_Teams_Key   ON Teams   (Key) WHERE Key IS NOT NULL;
CREATE UNIQUE INDEX UX_Players_Key ON Players (Key) WHERE Key IS NOT NULL;
CREATE UNIQUE INDEX UX_Seasons_Key ON Seasons (Key) WHERE Key IS NOT NULL;
CREATE UNIQUE INDEX UX_Matches_Key ON Matches (Key) WHERE Key IS NOT NULL;

-- Which content build populated this database. Singleton row, like Career: a save
-- is built from exactly one bundle, and the hash lets a re-import be a no-op.
CREATE TABLE ContentBuilds (
    Id             INTEGER PRIMARY KEY CHECK (Id = 1),
    BuildId        TEXT    NOT NULL,
    ContentHash    TEXT    NOT NULL,
    FormatVersion  INTEGER NOT NULL,
    ContentVersion INTEGER NOT NULL,
    Generator      TEXT    NOT NULL,
    ImportedAt     TEXT    NOT NULL
);
