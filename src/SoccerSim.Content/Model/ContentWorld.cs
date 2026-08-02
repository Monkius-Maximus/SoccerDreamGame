namespace SoccerSim.Content.Model;

/// <summary>
/// The initial save-state a new career starts from: seasons, the opening fixture list, who the
/// human is, and starting balances.
///
/// This is NOT content in the same sense as clubs and players — it is the world's starting
/// position, and once a career is running the game owns these tables outright. It lives in the
/// bundle because a fresh save has to come from somewhere, and hand-written SQL was the thing
/// this pipeline exists to replace. When a real fixture generator lands, most of this becomes
/// derived rather than authored.
/// </summary>
public sealed record ContentWorld
{
    /// <summary>The date the game clock starts at.</summary>
    public DateTime StartDate { get; init; } = new(2026, 8, 1);

    public IReadOnlyList<ContentSeason> Seasons { get; init; } = [];

    public IReadOnlyList<ContentFixture> Fixtures { get; init; } = [];

    public IReadOnlyList<ContentPlayerFinance> Finances { get; init; } = [];

    /// <summary>Who the human controls. Null means "no career yet" — a valid, unseeded world.</summary>
    public string? HumanPlayerKey { get; init; }
}

public sealed record ContentSeason : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string LeagueKey { get; init; }

    public DateTime StartDate { get; init; }

    public DateTime EndDate { get; init; }

    /// <summary>Whether this is the league's <c>CurrentSeasonId</c> at world start.</summary>
    public bool IsCurrent { get; init; }
}

public sealed record ContentFixture : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string SeasonKey { get; init; }

    public required string HomeTeamKey { get; init; }

    public required string AwayTeamKey { get; init; }

    public DateTime KickoffDate { get; init; }
}

public sealed record ContentPlayerFinance
{
    public required string PlayerKey { get; init; }

    public long Balance { get; init; }

    public long BaseSalaryWeekly { get; init; }
}
