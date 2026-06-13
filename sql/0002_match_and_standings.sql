-- ============================================================================
-- Migration 0002 — matches, scorers, standings
-- Drives the LOD background simulation. Tier 3 minor leagues resolve to simple
-- inserts here at week's end (GDD §6).
-- ============================================================================

CREATE TABLE Matches (
    Id         INTEGER PRIMARY KEY,
    SeasonId   INTEGER NOT NULL,
    LeagueId   INTEGER NOT NULL,
    HomeTeamId INTEGER NOT NULL,
    AwayTeamId INTEGER NOT NULL,
    KickoffDate TEXT   NOT NULL,
    Played     INTEGER NOT NULL DEFAULT 0 CHECK (Played IN (0, 1)),
    HomeGoals  INTEGER NULL,
    AwayGoals  INTEGER NULL,
    FOREIGN KEY (SeasonId)   REFERENCES Seasons (Id) ON DELETE CASCADE,
    FOREIGN KEY (LeagueId)   REFERENCES Leagues (Id) ON DELETE CASCADE,
    FOREIGN KEY (HomeTeamId) REFERENCES Teams (Id),
    FOREIGN KEY (AwayTeamId) REFERENCES Teams (Id)
);

-- Scorers; populated even by Tier 3's simple math resolution.
CREATE TABLE Goals (
    Id       INTEGER PRIMARY KEY,
    MatchId  INTEGER NOT NULL,
    PlayerId INTEGER NOT NULL,
    Minute   INTEGER NOT NULL CHECK (Minute BETWEEN 1 AND 120),
    FOREIGN KEY (MatchId)  REFERENCES Matches (Id) ON DELETE CASCADE,
    FOREIGN KEY (PlayerId) REFERENCES Players (Id)
);

CREATE TABLE Standings (
    SeasonId     INTEGER NOT NULL,
    TeamId       INTEGER NOT NULL,
    Played       INTEGER NOT NULL DEFAULT 0,
    Won          INTEGER NOT NULL DEFAULT 0,
    Drawn        INTEGER NOT NULL DEFAULT 0,
    Lost         INTEGER NOT NULL DEFAULT 0,
    GoalsFor     INTEGER NOT NULL DEFAULT 0,
    GoalsAgainst INTEGER NOT NULL DEFAULT 0,
    Points       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (SeasonId, TeamId),
    FOREIGN KEY (SeasonId) REFERENCES Seasons (Id) ON DELETE CASCADE,
    FOREIGN KEY (TeamId)   REFERENCES Teams (Id)   ON DELETE CASCADE
);

CREATE INDEX IX_Matches_League_Kickoff ON Matches (LeagueId, KickoffDate);
CREATE INDEX IX_Goals_MatchId          ON Goals (MatchId);
CREATE INDEX IX_Goals_PlayerId         ON Goals (PlayerId);
