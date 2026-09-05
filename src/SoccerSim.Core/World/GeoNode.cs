namespace SoccerSim.Core.World;

/// <summary>
/// One node in the geographic hierarchy (World → Confederation → ... → City). Clubs and
/// competitions anchor to a node via <see cref="GeoNodeId"/>; the tree is walked upward through
/// <see cref="ParentId"/> to build a breadcrumb (e.g. "Mundo › CONMEBOL › Brasil › ... › Cidade").
/// </summary>
public sealed record GeoNode(string GeoNodeId, GeoNodeKind Kind, string? ParentId, string DisplayName);
