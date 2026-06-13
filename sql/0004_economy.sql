-- ============================================================================
-- Migration 0004 — economy & progression (GDD §5)
-- Income (salary, bonuses, sponsorships), expenditure, and the housing/staff
-- loop that multiplies daily task stat-yields.
-- ============================================================================

CREATE TABLE Sponsorships (
    Id           INTEGER PRIMARY KEY,
    PlayerId     INTEGER NOT NULL,
    Sponsor      TEXT    NOT NULL,
    WeeklyAmount INTEGER NOT NULL,
    StartDate    TEXT    NOT NULL,
    EndDate      TEXT    NULL,
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE
);

-- Catalogue of purchasable items: a better item raises the yield of its task.
CREATE TABLE HousingItems (
    Id              INTEGER PRIMARY KEY,
    Key             TEXT    NOT NULL UNIQUE,        -- e.g. 'orthopedic_bed'
    Name            TEXT    NOT NULL,
    Cost            INTEGER NOT NULL,
    StatKey         TEXT    NOT NULL,               -- e.g. 'stamina_recovery'
    YieldMultiplier REAL    NOT NULL DEFAULT 1.0
);

-- Items a player owns -> their multipliers apply to matching daily tasks.
CREATE TABLE PlayerInventory (
    PlayerId INTEGER NOT NULL,
    ItemId   INTEGER NOT NULL,
    PRIMARY KEY (PlayerId, ItemId),
    FOREIGN KEY (PlayerId) REFERENCES Players (Id)      ON DELETE CASCADE,
    FOREIGN KEY (ItemId)   REFERENCES HousingItems (Id) ON DELETE CASCADE
);

CREATE TABLE PlayerFinances (
    PlayerId         INTEGER PRIMARY KEY,
    Balance          INTEGER NOT NULL DEFAULT 0,
    BaseSalaryWeekly INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE
);

-- Income/expenditure ledger.
CREATE TABLE Transactions (
    Id       INTEGER PRIMARY KEY,
    PlayerId INTEGER NOT NULL,
    Date     TEXT    NOT NULL,
    Amount   INTEGER NOT NULL,    -- +income / -expense
    Category TEXT    NOT NULL,    -- 'salary','bonus','sponsorship','housing','diet','recovery','leisure'
    FOREIGN KEY (PlayerId) REFERENCES Players (Id) ON DELETE CASCADE
);

CREATE INDEX IX_Sponsorships_PlayerId ON Sponsorships (PlayerId);
CREATE INDEX IX_Transactions_PlayerId ON Transactions (PlayerId);
