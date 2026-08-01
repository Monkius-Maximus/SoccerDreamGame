-- ============================================================================
-- Migration 9999 — development seed data (OPTIONAL)
-- Applied only when the migration runner is invoked with includeSeeds = true.
-- Gives a minimal three-tier world so the LOD simulation has something to chew on.
-- ============================================================================

INSERT INTO Leagues (Id, Name, Country, Tier) VALUES
    (1, 'Premier Division',  'Homeland', 1),   -- Tier 1: active human league
    (2, 'La Liga Mayor',     'Iberia',   2),   -- Tier 2: major foreign league
    (3, 'Regional North',    'Homeland', 3);   -- Tier 3: minor league

INSERT INTO Seasons (Id, LeagueId, StartDate, EndDate) VALUES
    (1, 1, '2026-08-01 00:00:00', '2027-05-31 00:00:00'),
    (2, 2, '2026-08-15 00:00:00', '2027-05-24 00:00:00'),
    (3, 3, '2026-09-01 00:00:00', '2027-04-30 00:00:00');

UPDATE Leagues SET CurrentSeasonId = 1 WHERE Id = 1;
UPDATE Leagues SET CurrentSeasonId = 2 WHERE Id = 2;
UPDATE Leagues SET CurrentSeasonId = 3 WHERE Id = 3;

INSERT INTO Teams (Id, Name, LeagueId, Budget, EloRating) VALUES
    (1, 'Riverside FC',   1, 5000000, 1620),
    (2, 'Hilltop United', 1, 4200000, 1555),
    (3, 'Costa Real',     2, 9000000, 1710),
    (4, 'Atletico Sur',   2, 7500000, 1668),
    (5, 'North Rovers',   3,  150000, 1420),
    (6, 'Lakeside Town',  3,  120000, 1390);

INSERT INTO PlayerTraits (Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias) VALUES
    (1, 'hot_headed',  'Hot-Headed',  85, 40, 10),
    (2, 'team_player', 'Team Player', 30, 10, -5),
    (3, 'showboat',    'Showboat',    50, 80, 15);

INSERT INTO Players (Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision) VALUES
    (1, 'Alex',  'Mercer',  1, 16, 15, 13, 14, 17, 8,  15),
    (2, 'Diego', 'Santos',  1, 14, 16, 15, 13, 11, 16, 12),
    (3, 'Ravi',  'Kapoor',  3, 17, 14, 12, 16, 15, 9,  17),
    (4, 'Tom',   'Fielder', 5, 11, 13, 14, 10, 9,  13, 9);

-- Extra Tier 1 squad members so both active-league teams can field a side and the
-- rendered match has named scorers (Riverside = team 1, Hilltop = team 2).
INSERT INTO Players (Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision) VALUES
    (5,  'Mateo',   'Bianchi',   1, 13, 14, 12, 15, 14, 11, 14),
    (6,  'Kwame',   'Osei',      1, 15, 13, 16, 11, 10, 15, 10),
    (7,  'Lucas',   'Berg',      1, 12, 15, 13, 14, 13, 12, 13),
    (8,  'Hiroshi', 'Tanaka',    2, 14, 14, 12, 15, 13, 12, 15),
    (9,  'Owen',    'Pryce',     2, 16, 13, 14, 12, 15, 10, 12),
    (10, 'Bruno',   'Ramirez',   2, 13, 15, 15, 13, 12, 14, 11),
    (11, 'Sami',    'Haidar',    2, 15, 14, 13, 14, 14, 11, 13),
    (12, 'Noah',    'Whitfield', 2, 12, 16, 14, 13, 11, 15, 12);

INSERT INTO PlayerTraitAssignments (PlayerId, TraitId) VALUES
    (1, 1), (1, 3),
    (2, 2),
    (3, 3),
    (4, 2);

-- The active career for this dev save: the human controls player 1 (Alex Mercer),
-- who plays for Riverside FC (team 1). Sourced at startup instead of hardcoded.
-- MasterSeed (0xD1CED00D2026) is stated explicitly, never defaulted: it is what makes this
-- dev world reproducible, and every simulation stream derives from it.
INSERT INTO Career (Id, HumanPlayerId, MasterSeed) VALUES (1, 1, 230686183989286);

INSERT INTO PlayerFinances (PlayerId, Balance, BaseSalaryWeekly) VALUES
    (1, 250000, 35000),
    (2, 180000, 28000),
    (3, 600000, 60000),
    (4,  12000,  3500);

INSERT INTO HousingItems (Id, Key, Name, Cost, StatKey, YieldMultiplier) VALUES
    (1, 'basic_bed',       'Basic Bed',           0,    'stamina_recovery', 1.0),
    (2, 'orthopedic_bed',  'Orthopedic Bed',      40000, 'stamina_recovery', 1.4),
    (3, 'home_gym',        'Home Gym',            75000, 'strength_training', 1.5);

-- Two unplayed Tier 1 fixtures in season 1 so "Play Next Fixture" has a match to render
-- (and a second one to watch on a repeat run). KickoffDate >= the 2026-08-01 clock start.
INSERT INTO Matches (Id, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played) VALUES
    (1, 1, 1, 1, 2, '2026-08-08 15:00:00', 0),
    (2, 1, 1, 2, 1, '2026-08-15 15:00:00', 0);
