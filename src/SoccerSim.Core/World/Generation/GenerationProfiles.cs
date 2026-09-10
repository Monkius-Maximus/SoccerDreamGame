namespace SoccerSim.Core.World.Generation;

/// <summary>
/// How one attribute is distributed for one position, measured as a distance from the player's
/// own overall: a goalkeeper's Finishing sits about 54 points below his overall, his Reflexes
/// above it. Sampling around the overall rather than around a fixed value is what keeps a weak
/// keeper recognisably a keeper.
/// </summary>
public sealed record AttributeProfile(double OffsetMean, double StdDev);

/// <summary>
/// Everything the squad generator needs that was measured rather than decided: the per-position
/// attribute shapes and the name pools. Imported as data (migration 0013), never hardcoded —
/// re-measuring from a larger batch must be an import, not a recompile.
/// </summary>
public sealed record GenerationProfiles(
    IReadOnlyDictionary<Position, IReadOnlyDictionary<Attr, AttributeProfile>> Attributes,
    IReadOnlyList<string> FirstNames,
    IReadOnlyList<string> LastNames)
{
    public bool IsEmpty => Attributes.Count == 0 || FirstNames.Count == 0 || LastNames.Count == 0;
}
