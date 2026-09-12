-- ============================================================================
-- Migration 0016 — the undo stack also carries the scale data
--
-- Countries and divisions (migration 0014) live outside the world DOCUMENT: they
-- are not in the JSON the exporter writes, because they are not part of the
-- authored batch the pilot shipped. The undo snapshot is that document — which
-- means, until this column existed, undoing a change to a country or a division
-- would have quietly restored nothing while the button said it worked. A tool
-- that lies about having undone something is worse than one with no undo.
--
-- A second column rather than a wrapper object around both: the Document column
-- stays exactly the export format, whose round trip is already pinned by a test,
-- and correctness by construction survives. The scale payload is its own small
-- shape with its own reader.
--
-- '{}' as the default so rows written before this migration read back as "no
-- countries, no divisions", which is what the world looked like then.
-- ============================================================================

ALTER TABLE WorldHistory ADD COLUMN Scale TEXT NOT NULL DEFAULT '{}';
