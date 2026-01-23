namespace WorldApi.World.Dem;

/// <summary>
/// Orchestrates lazy fetching of DEM tiles, ensuring tiles are available locally.
/// Combines index lookup, public fetch, local persistence, and index mutation.
/// Prevents duplicate concurrent fetches for the same tile.
/// </summary>
public sealed class DemTileResolver
{
    private readonly DemTileIndex _index;
    private readonly PublicSrtmClient _publicClient;
    private readonly DemTileWriter _writer;
    private readonly object _fetchLock = new();
    private readonly HashSet<string> _inProgressFetches = new();

    public DemTileResolver(
        DemTileIndex index,
        PublicSrtmClient publicClient,
        DemTileWriter writer)
    {
        _index = index;
        _publicClient = publicClient;
        _writer = writer;
    }

    /// <summary>
    /// Resolves a DEM tile for the given coordinates, fetching if necessary.
    /// Guarantees the tile exists locally after this call (or throws if unavailable).
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees</param>
    /// <param name="longitude">Longitude in decimal degrees</param>
    /// <returns>DemTile metadata for the resolved tile</returns>
    /// <exception cref="TileNotFoundException">Thrown if tile does not exist in public SRTM dataset</exception>
    public async Task<DemTile> ResolveTileAsync(double latitude, double longitude)
    {
        // 1. Check if tile already in index
        var existingTile = _index.FindTileContaining(latitude, longitude);
        if (existingTile != null)
        {
            return existingTile;
        }

        // 2. Calculate tile name
        string tileName = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // 3. Prevent duplicate concurrent fetches
        await EnsureTileFetchedAsync(tileName);

        // 4. Tile should now be in index, retrieve it
        var resolvedTile = _index.FindTileContaining(latitude, longitude);
        if (resolvedTile == null)
        {
            throw new InvalidOperationException(
                $"Tile {tileName} was fetched but not found in index for coordinates ({latitude}, {longitude})");
        }

        return resolvedTile;
    }

    /// <summary>
    /// Ensures a tile is fetched, stored, and indexed.
    /// Prevents duplicate concurrent fetches using lock + in-progress tracking.
    /// </summary>
    private async Task EnsureTileFetchedAsync(string tileName)
    {
        // Check if we should fetch or wait
        bool shouldFetch;
        lock (_fetchLock)
        {
            shouldFetch = !_inProgressFetches.Contains(tileName);
            if (shouldFetch)
            {
                _inProgressFetches.Add(tileName);
            }
        }

        // If another thread is fetching, wait for it
        if (!shouldFetch)
        {
            while (true)
            {
                await Task.Delay(50);
                lock (_fetchLock)
                {
                    if (!_inProgressFetches.Contains(tileName))
                    {
                        // Fetch completed
                        return;
                    }
                }
            }
        }

        // We're the fetcher
        try
        {
            // Check if tile exists in local S3 (but not yet in index)
            if (await _writer.TileExistsAsync(tileName))
            {
                // File exists, just add to index
                var tile = SrtmFilenameParser.Parse($"{tileName}.hgt") with { S3Key = $"dem/srtm/{tileName}.hgt" };
                _index.Add(tile);
                return;
            }

            // Fetch from public SRTM
            byte[] tileData = await _publicClient.FetchAndDecompressTileAsync(tileName);

            // Save to local S3
            string s3Key = await _writer.WriteTileAsync(tileName, tileData);

            // Parse metadata and add to index
            var fetchedTile = SrtmFilenameParser.Parse($"{tileName}.hgt") with { S3Key = s3Key };
            _index.Add(fetchedTile);
        }
        finally
        {
            // Remove from in-progress tracking
            lock (_fetchLock)
            {
                _inProgressFetches.Remove(tileName);
            }
        }
    }
}
