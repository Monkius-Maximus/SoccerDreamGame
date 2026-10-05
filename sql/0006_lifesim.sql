-- ============================================================================
-- Migration 0006 — off-pitch life simulation (GDD §1 life-sim, §5 economy loop)
--
-- Wellbeing is stored ONCE for the active career, whatever role that career is
-- living. A player and a manager drain the same six needs, so they share this
-- table rather than getting two parallel schemas that would drift apart; the
-- Career.Role column is what selects the decay/weight profile applied to them.
--
-- Portability: TEXT role/need keys and a REAL gauge map 1:1 to PostgreSQL, and
-- the tables are keyed by CareerId so a future multi-career (or multiplayer)
-- save only relaxes the Career singleton, not this schema.
-- ============================================================================

-- 'Player' | 'Manager'. Defaulted so existing saves migrate as player careers.
-- NOTE: a manager career still points HumanPlayerId at a person row; a dedicated
-- staff/manager entity is future work and does not change this table's shape.
ALTER TABLE Career ADD COLUMN Role TEXT NOT NULL DEFAULT 'Player';

-- One row per need per career: the 0-100 gauges the life-sim decays each day.
-- Stored long-form (rather than six columns) so adding a seventh need is a data
-- change, not a schema migration.
CREATE TABLE CareerWellbeing (
    CareerId INTEGER NOT NULL,
    NeedKey  TEXT    NOT NULL,      -- 'Energy','Nutrition','Fitness','Morale','Social','Focus'
    Value    REAL    NOT NULL CHECK (Value BETWEEN 0 AND 100),
    PRIMARY KEY (CareerId, NeedKey),
    FOREIGN KEY (CareerId) REFERENCES Career (Id) ON DELETE CASCADE
);

-- What the human actually did with their days. Feeds the season review screen and
-- the economy's spending breakdown (Cost mirrors the Transactions ledger entry).
CREATE TABLE LifeActivityLog (
    Id          INTEGER PRIMARY KEY,
    CareerId    INTEGER NOT NULL,
    Date        TEXT    NOT NULL,
    ActivityKey TEXT    NOT NULL,   -- LifeActivityCatalogue key, e.g. 'film_study'
    Cost        INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CareerId) REFERENCES Career (Id) ON DELETE CASCADE
);

CREATE INDEX IX_LifeActivityLog_Career_Date ON LifeActivityLog (CareerId, Date);
