-- ============================================================================
-- Migration 0005 — career / save-state
-- Records who the human player is for this save file. Singleton row (Id = 1).
-- The controlled team is derived from the player's TeamId, so "who the player is"
-- lives in one place instead of being hardcoded at the composition root.
-- ============================================================================

CREATE TABLE Career (
    Id            INTEGER PRIMARY KEY CHECK (Id = 1),  -- one active career per save file
    HumanPlayerId INTEGER NOT NULL,
    FOREIGN KEY (HumanPlayerId) REFERENCES Players (Id)
);
