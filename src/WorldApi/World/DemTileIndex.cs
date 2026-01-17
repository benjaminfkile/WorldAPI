namespace WorldApi.World;

public sealed class DemTileIndex
{
    private readonly Dictionary<string, DemTile> _tiles = new();

    public void Add(DemTile tile)
    {
        _tiles[tile.S3Key] = tile;
    }

    public IReadOnlyCollection<DemTile> GetAllTiles() => _tiles.Values;

    public DemTile? FindTileContaining(double latitude, double longitude)
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

    public int Count => _tiles.Count;
}
