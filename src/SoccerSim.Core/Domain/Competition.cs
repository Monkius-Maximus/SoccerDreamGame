namespace SoccerSim.Core.Domain;

public sealed class Season
{
    public int Id { get; init; }
    public int LeagueId { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
}

public sealed class Match
{
    public int Id { get; init; }
    public int SeasonId { get; init; }
    public int LeagueId { get; init; }
    public int HomeTeamId { get; init; }
    public int AwayTeamId { get; init; }
    public DateTime KickoffDate { get; init; }
    public bool Played { get; set; }
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
}

public sealed class Standing
{
    public int SeasonId { get; init; }
    public int TeamId { get; init; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int Points { get; set; }
}
