namespace SoccerSim.Content.Model;

public enum CompetitionFormat
{
    League,
    Cup,
}

public enum CompetitionScope
{
    Domestic,
    Continental,
    International,
}

public enum PitchSurface
{
    Grass,
    Artificial,
    Hybrid,
}

public enum PreferredFoot
{
    Left,
    Right,
    Both,
}

/// <summary>Which side of the pitch a player occupies within their role.</summary>
public enum Flank
{
    Left,
    Centre,
    Right,
}

public enum CoachRole
{
    HeadCoach,
    AssistantCoach,
    GoalkeepingCoach,
    FitnessCoach,
    Physio,
    Scout,
    Director,
}

public enum SquadStatus
{
    Key,
    FirstTeam,
    Squad,
    Rotation,
    Prospect,
    Youth,
}

public sealed record ContentNation : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>Three-letter code, e.g. <c>BRA</c>.</summary>
    public required string Code { get; init; }

    public string? Adjective { get; init; }

    public string? Confederation { get; init; }

    public int Reputation { get; init; } = 50;
}

public sealed record ContentStadium : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? NationKey { get; init; }

    public string? City { get; init; }

    public int Capacity { get; init; }

    /// <summary>Feeds <c>PitchGeometry</c>; bounded by the laws of the game.</summary>
    public int PitchLengthM { get; init; } = 105;

    public int PitchWidthM { get; init; } = 68;

    public PitchSurface Surface { get; init; } = PitchSurface.Grass;

    public int? YearBuilt { get; init; }
}

/// <summary>
/// The tournament. Divisions (<see cref="ContentLeague"/>) sit underneath it — see the header
/// comment in <c>sql/0007_world_structure.sql</c> for why the division table kept its name.
/// </summary>
public sealed record ContentCompetition : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? ShortName { get; init; }

    /// <summary>Null for a continental or international competition.</summary>
    public string? NationKey { get; init; }

    public CompetitionFormat Format { get; init; } = CompetitionFormat.League;

    public CompetitionScope Scope { get; init; } = CompetitionScope.Domestic;

    public int Reputation { get; init; } = 50;

    public int PointsWin { get; init; } = 3;

    public int PointsDraw { get; init; } = 1;
}

public sealed record ContentCoach : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>Null for an unemployed coach.</summary>
    public string? TeamKey { get; init; }

    public string? NationKey { get; init; }

    public CoachRole Role { get; init; } = CoachRole.HeadCoach;

    public DateTime? DateOfBirth { get; init; }

    /// <summary>Same 1–20 scale as player attributes, for one mental model across the game.</summary>
    public int Coaching { get; init; } = 10;

    public int TacticalKnowledge { get; init; } = 10;

    public int ManManagement { get; init; } = 10;

    public int Fitness { get; init; } = 10;

    public int Scouting { get; init; } = 10;

    /// <summary>Feeds <c>TeamTactics.Mentality</c> when this coach picks the side.</summary>
    public string PreferredMentality { get; init; } = "Balanced";
}

/// <summary>A player's or a coach's terms at a club. Exactly one of the two subject keys is set.</summary>
public sealed record ContentContract : IContentEntity
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public string? PlayerKey { get; init; }

    public string? CoachKey { get; init; }

    public required string TeamKey { get; init; }

    public DateTime StartDate { get; init; }

    public DateTime EndDate { get; init; }

    public long WeeklyWage { get; init; }

    public long SigningBonus { get; init; }

    public long? ReleaseClause { get; init; }

    public SquadStatus SquadStatus { get; init; } = SquadStatus.Squad;
}
