-- ============================================================================
-- Migration 0008 — club deviation audit (1:1 with Clubs)
--
-- The provenance record: for every factual claim about a club's real-world anchor
-- there is a citation column beside it. This is the "no number without a source"
-- rule as data (DATA_CONTRACT.md §3).
--
-- Separate from Clubs on purpose: it has its own lifecycle (reviewer, review date)
-- and is read in batch audit sweeps, not on the club page.
--
-- PhoneticSimilarity is NULLABLE and deliberately UNCONSTRAINED in range. It only
-- applies when NamingRule = 'Phonetic', and the real batch contains a club outside
-- the 0.55-0.80 window that the tool must be able to store and report. See the long
-- note in 0007 and Core/World/Validation/ClubInvariants (check #2).
--
-- CrestOriginalChargeReplaced is TEXT, not a flag: the source data carries prose
-- there ("SIM — carga central original substituída"), not a boolean.
-- ============================================================================

CREATE TABLE ClubDeviationAudit (
    ClubId                     TEXT PRIMARY KEY REFERENCES Clubs (ClubId) ON DELETE CASCADE,
    AnchorClubName             TEXT    NOT NULL,
    AnchorCityName             TEXT    NOT NULL,
    AnchorFoundingYear         INTEGER NOT NULL,
    GeneratedFoundingYear      INTEGER NOT NULL,
    FoundingDecadePreserved    INTEGER NOT NULL,
    FoundingSourceCitation     TEXT    NOT NULL,
    AnchorNickname             TEXT    NOT NULL,
    NicknameCommercialLevel    INTEGER NOT NULL CHECK (NicknameCommercialLevel BETWEEN 0 AND 2),
    NicknameTrademarked        INTEGER NOT NULL CHECK (NicknameTrademarked IN (0, 1)),
    NicknameEvidence           TEXT    NOT NULL,
    NamingRule                 TEXT    NOT NULL CHECK (NamingRule IN
                                   ('Toponymic', 'RegionalCode', 'EpithetLift', 'Phonetic')),
    NamingRuleReason           TEXT    NOT NULL,
    PhoneticSimilarity         REAL    NULL,     -- range is an invariant, not a constraint (see header)
    CrestOriginalChargeReplaced TEXT   NOT NULL,
    CrestSubstituteCharge      TEXT    NOT NULL,
    CrestSourceCitation        TEXT    NOT NULL,
    DistrictSourceCitation     TEXT    NOT NULL,
    TacticalStyleProvenance    TEXT    NOT NULL CHECK (TacticalStyleProvenance IN
                                   ('Derived', 'Sampled', 'Authored')),
    TacticalStyleEvidence      TEXT    NOT NULL,
    ChromaticPolicy            TEXT    NOT NULL,
    AnchorFactsVerified        INTEGER NOT NULL CHECK (AnchorFactsVerified IN (0, 1)),
    ReviewedBy                 TEXT    NOT NULL,
    ReviewDate                 TEXT    NOT NULL,
    Note                       TEXT    NULL
);
