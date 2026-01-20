using System.Collections.Concurrent;

namespace WorldApi.World.Dem;

public sealed class HgtTileCache
{
    private readonly ConcurrentDictionary<string, SrtmTileData> _cache = new();

    public bool TryGet(string s3Key, out SrtmTileData? tile)
    {
        return _cache.TryGetValue(s3Key, out tile);
    }

    public void Add(string s3Key, SrtmTileData tile)
    {
        _cache[s3Key] = tile;
    }

    public bool Contains(string s3Key)
    {
        return _cache.ContainsKey(s3Key);
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public int Count => _cache.Count;
}
