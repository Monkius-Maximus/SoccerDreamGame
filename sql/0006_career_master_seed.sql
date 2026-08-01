-- ============================================================================
-- Migration 0006 — master seed per career
-- The world seed belongs to the save file, not to the composition root. Until now
-- it was a constant in GameBootstrap, which meant every save replayed the same world
-- and no save could be replayed once that constant changed.
--
-- Stored as INTEGER: SQLite integers are signed 64-bit, so the unsigned seed travels
-- as its two's-complement bit pattern and comes back unchanged. That is the whole
-- serialization — the generator state is a single ulong and needs nothing more.
--
-- Deliberately NULL-able rather than NOT NULL DEFAULT <something>: a default would be an
-- implicit seed, which is exactly what the determinism rules forbid. A career row with
-- no seed is an error the read path reports, not a hole to paper over.
-- ============================================================================

ALTER TABLE Career ADD COLUMN MasterSeed INTEGER NULL;

-- Backfill: this is not a default, it is the seed those worlds were actually generated
-- with (the old GameBootstrap constant 0xD1CED00D2026 = 230686183989286). Any save written
-- before this migration replays correctly only with this exact value, so it is recorded
-- as history rather than reset.
UPDATE Career SET MasterSeed = 230686183989286 WHERE MasterSeed IS NULL;
