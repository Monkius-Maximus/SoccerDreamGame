-- ============================================================================
-- Migration 0003 — dynamic form / mood
-- The static/dynamic split made physical: everything that changes during a season
-- is keyed by (PlayerId, SeasonId) so a seasonal reset just clears these rows and
-- never touches the immutable Players record.
-- ============================================================================

CREATE TABLE FormMood (
    PlayerId       INTEGER NOT NULL,
    SeasonId       INTEGER NOT NULL,
    FormValue      INTEGER NOT NULL DEFAULT 0 CHECK (FormValue BETWEEN -5 AND 5),
    MoodValue      INTEGER NOT NULL DEFAULT 0 CHECK (MoodValue BETWEEN -5 AND 5),
    LastRecalcDate TEXT    NULL,
    PRIMARY KEY (PlayerId, SeasonId),
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE,
    FOREIGN KEY (SeasonId) REFERENCES Seasons (Id) ON DELETE CASCADE
);

-- Per-match player ratings; the input that drives form recalculation
-- (Tier 1 daily, Tier 2 weekly).
CREATE TABLE PlayerMatchRatings (
    MatchId  INTEGER NOT NULL,
    PlayerId INTEGER NOT NULL,
    Rating   REAL    NOT NULL CHECK (Rating BETWEEN 0 AND 10),
    PRIMARY KEY (MatchId, PlayerId),
    FOREIGN KEY (MatchId)  REFERENCES Matches (Id) ON DELETE CASCADE,
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE
);
