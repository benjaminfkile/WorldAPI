namespace WorldApi.World.Dem;

/// <summary>
/// Thread-safe in-memory index of available DEM tiles.
/// Supports runtime mutation for lazy-fetched tiles without requiring restart.
/// </summary>
public sealed class DemTileIndex
{
    private readonly Dictionary<string, DemTile> _tiles = new();
    private readonly object _lock = new();

    /// <summary>
    /// Adds a tile to the index. Thread-safe and idempotent.
    /// If a tile with the same S3Key already exists, it will be replaced.
    /// </summary>
    public void Add(DemTile tile)
    {
        lock (_lock)
        {
            _tiles[tile.S3Key] = tile;
        }
    }

    public IReadOnlyCollection<DemTile> GetAllTiles()
    {
        lock (_lock)
        {
            return _tiles.Values.ToList();
        }
    }

    public DemTile? FindTileContaining(double latitude, double longitude)
    {
        lock (_lock)
        {
            foreach (var tile in _tiles.Values)
            {
                if (latitude >= tile.MinLatitude && latitude < tile.MaxLatitude &&
                    longitude >= tile.MinLongitude && longitude < tile.MaxLongitude)
                {
                    return tile;
                }
            }
            return null;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _tiles.Count;
            }
        }
    }
}
