-- ============================================================================
-- Migration 0015 — the undo stack
--
-- The tool has no Save button: every edit writes immediately, which is the right
-- shape for authoring and the wrong shape for a mistake. Three paths are openly
-- destructive — regenerating a squad, applying a CSV import that removes rows,
-- recalculating the batch — and until now their only protection was a confirm()
-- dialog, which asks a question nobody can answer without seeing the result.
--
-- A snapshot is the WORLD DOCUMENT, not a diff. Two reasons:
--
--   * It is the same format WorldJsonWriter produces and WorldJsonReader reads,
--     and a test already pins that the two are inverses. Undo is therefore
--     correct by construction rather than by a second mechanism nobody exercises.
--   * A diff-based undo would need an inverse for every operation, including the
--     coarse ones (replace the whole world from CSV). Those inverses are exactly
--     where an undo stack goes wrong quietly.
--
-- The cost is honest: roughly 1.5 MB per snapshot for the pilot batch, capped at
-- 25 entries (Core/World/WorldHistory). That is a bounded few tens of megabytes
-- in a local authoring database, and it buys back every mistake.
-- ============================================================================

CREATE TABLE WorldHistory (
    Id       INTEGER PRIMARY KEY,
    -- What the user did, in their language: "Regerar elenco de Carioca Sul".
    -- The button reads "Desfazer" and the label says what would come back.
    Label    TEXT NOT NULL,
    TakenAt  TEXT NOT NULL,          -- ISO-8601, as everywhere else in the schema
    Document TEXT NOT NULL           -- the whole world, as the export writes it
);

CREATE INDEX IX_WorldHistory_TakenAt ON WorldHistory (Id DESC);
