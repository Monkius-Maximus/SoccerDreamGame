-- ============================================================================
-- Migration 0013 — squad generation inputs and world settings
--
-- The generator's statistical profile is DATA, not code (ROADMAP.md Sprint 5).
-- Every offset and deviation here was measured from the 688 real players: it is
-- what makes a generated goalkeeper come out with Finishing ~54 points below his
-- own overall and Reflexes above it, instead of a flat noise cloud. Re-measuring
-- from a larger batch has to be an import, not a recompile.
--
-- WorldSettings holds the document's meta block. masterSeed matters: it is the
-- root of every deterministic stream, so it belongs with the world it seeds
-- rather than in a config file that can drift away from the data. SchemaVersion
-- was until now a literal in the API, which is exactly the kind of fact that
-- should come from the document that declares it.
-- ============================================================================

-- Position -> Attr -> (mean offset from the player's own overall, standard deviation).
CREATE TABLE GenerationAttributeProfiles (
    Position   TEXT NOT NULL CHECK (Position IN ('GK', 'CB', 'FB', 'DM', 'CM', 'AM', 'WG', 'ST')),
    Attr       TEXT NOT NULL CHECK (Attr IN
                   ('Finishing', 'Passing', 'Dribbling', 'Tackling', 'Pace', 'Strength',
                    'Stamina', 'Positioning', 'Vision', 'Composure', 'Reflexes', 'Handling')),
    OffsetMean REAL NOT NULL,
    StdDev     REAL NOT NULL CHECK (StdDev >= 0),
    PRIMARY KEY (Position, Attr)
);

-- The name pools, drawn from the existing batch. A generated player's name is invented
-- from these, never taken from a real person: that is part of what makes generation safe
-- (ALGORITHMS.md §6.10).
CREATE TABLE GenerationNames (
    Kind TEXT NOT NULL CHECK (Kind IN ('First', 'Last')),
    Name TEXT NOT NULL,
    PRIMARY KEY (Kind, Name)
);

CREATE TABLE WorldSettings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
