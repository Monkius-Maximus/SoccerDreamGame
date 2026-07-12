using SoccerSim.Core.Pitch;

namespace SoccerSim.Core.Ai;

/// <summary>
/// Zonal influence map: a coarse grid over the pitch where each cell holds the Home team's
/// control share in [0, 1], derived from every player's proximity falloff. Both AI layers
/// consult it (finding space to carry/pass into, weighing pressure).
/// TODO: a continuous fine-grid version (per-cell gradients, decayed writes) is a later
/// upgrade — keep this zonal version as the only implementation until then.
/// </summary>
public sealed class InfluenceMap : IInfluenceMap
{
    /// <summary>Squared distance (m²) at which a player's influence halves.</summary>
    private const double FalloffScale = 60.0;

    private readonly PitchDimensions _pitch;
    private readonly int _columns;
    private readonly int _rows;
    private readonly double[,] _homeShare;

    public InfluenceMap(int columns, int rows, PitchDimensions pitch)
    {
        if (columns < 2 || rows < 2)
            throw new ArgumentOutOfRangeException(nameof(columns), $"The influence grid needs at least 2x2 cells; got {columns}x{rows}.");
        ArgumentNullException.ThrowIfNull(pitch);

        _columns = columns;
        _rows = rows;
        _pitch = pitch;
        _homeShare = new double[columns, rows];
    }

    /// <summary>Coarse default grid (~9 x 8.5 m cells on a standard pitch).</summary>
    public static InfluenceMap CreateDefault(PitchDimensions pitch) => new(12, 8, pitch);

    /// <summary>Recompute every cell from the current player positions. Call once per tick.</summary>
    public void Rebuild(IReadOnlyList<PlayerState> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        for (int col = 0; col < _columns; col++)
        {
            for (int row = 0; row < _rows; row++)
            {
                Vec2 centre = CellCentre(col, row);
                double home = 0.0;
                double away = 0.0;
                foreach (PlayerState player in players)
                {
                    double distanceSquared = (centre - player.Position).LengthSquared;
                    double influence = 1.0 / (1.0 + (distanceSquared / FalloffScale));
                    if (player.Side == TeamSide.Home)
                        home += influence;
                    else
                        away += influence;
                }

                double total = home + away;
                _homeShare[col, row] = total < 1e-9 ? 0.5 : home / total;
            }
        }
    }

    public double ControlAt(Vec2 point, TeamSide side)
    {
        Vec2 clamped = _pitch.Clamp(point);
        int col = Math.Min((int)(clamped.X / _pitch.Length * _columns), _columns - 1);
        int row = Math.Min((int)(clamped.Y / _pitch.Width * _rows), _rows - 1);
        double homeShare = _homeShare[col, row];
        return side == TeamSide.Home ? homeShare : 1.0 - homeShare;
    }

    private Vec2 CellCentre(int col, int row)
        => new((col + 0.5) * _pitch.Length / _columns, (row + 0.5) * _pitch.Width / _rows);
}
