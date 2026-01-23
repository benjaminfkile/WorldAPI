using System.Collections.Concurrent;

namespace WorldApi.World.Dem;

/// <summary>
/// Guarantees a DEM tile exists locally by orchestrating:
/// 1. Check in-memory index (fast path for cached tiles)
/// 2. Fetch from public SRTM if missing
/// 3. Persist to local S3
/// 4. Add to runtime index
/// 5. Return resolved tile
/// 
/// Handles concurrent requests efficiently:
/// - Only fetches a tile once, even with 100 concurrent requests
/// - Uses SemaphoreSlim to serialize fetch per tile
/// - Other tiles can be fetched concurrently
/// </summary>
public sealed class DemTileResolver
{
    private readonly DemTileIndex _index;
    private readonly IPublicSrtmClient _publicClient;
    private readonly ILocalSrtmPersistence _persistence;
    private readonly RuntimeDemIndexMutator _mutator;
    private readonly ILogger<DemTileResolver> _logger;

    // Per-tile semaphore to prevent concurrent fetches for the same tile
    // Key: S3 key (e.g., "dem/srtm/N46W113.hgt")
    // Value: Semaphore to serialize fetch requests
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perTileSemaphores =
        new(StringComparer.Ordinal);

    public DemTileResolver(
        DemTileIndex index,
        IPublicSrtmClient publicClient,
        ILocalSrtmPersistence persistence,
        RuntimeDemIndexMutator mutator,
        ILogger<DemTileResolver> logger)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _publicClient = publicClient ?? throw new ArgumentNullException(nameof(publicClient));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolve a DEM tile, ensuring it exists locally.
    /// 
    /// Guarantees:
    /// - Returns a valid DemTile for any valid coordinate
    /// - Only fetches from public SRTM once per tile (even with concurrent requests)
    /// - Tiles immediately indexed and discoverable
    /// 
    /// Concurrency: Safe for 100+ concurrent requests on the same or different tiles.
    /// Per-tile serialization prevents duplicate fetches while allowing parallel fetch of other tiles.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (-90 to 90)</param>
    /// <param name="longitude">Longitude in decimal degrees (-180 to 180)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolved DemTile</returns>
    /// <exception cref="ArgumentOutOfRangeException">If coordinates are out of range</exception>
    /// <exception cref="InvalidOperationException">If tile cannot be fetched or indexed</exception>
    public async Task<DemTile> ResolveTileAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90");

        if (longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180");

        cancellationToken.ThrowIfCancellationRequested();

        // Compute tile name and S3 key
        string tileName = SrtmTileNamer.ComputeTileName(latitude, longitude);
        string s3Key = $"dem/srtm/{tileName}";

        try
        {
            // Fast path: Check if tile already in index
            var existingTile = _index.FindTileContaining(latitude, longitude);
            if (existingTile != null)
            {
                _logger.LogInformation("✓ Cache hit for tile {TileName} at coordinates [{Lat:F2}, {Lon:F2}]",
                    tileName, latitude, longitude);
                return existingTile;
            }

            _logger.LogInformation("⟳ Cache miss for tile {TileName}. Fetching from public SRTM...", tileName);

            // Get or create a semaphore for this specific tile
            // This ensures only one fetch happens per tile, even with concurrent requests
            var semaphore = _perTileSemaphores.GetOrAdd(s3Key, _ => new SemaphoreSlim(1, 1));

            // Acquire semaphore for this tile
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Double-check after acquiring lock (another request may have already fetched it)
                var tile = _index.FindTileContaining(latitude, longitude);
                if (tile != null)
                {
                    _logger.LogInformation(
                        "✓ Another request already indexed tile {TileName}. Skipping fetch.",
                        tileName);
                    return tile;
                }

                // Fetch from public SRTM
                _logger.LogInformation("🌐 Fetching tile {TileName} from public SRTM bucket...", tileName);
                byte[] tileData = await _publicClient.FetchTileAsync(tileName);

                // Save to local S3
                _logger.LogInformation("💾 Saving tile {TileName} to local S3 cache...", tileName);
                await _persistence.SaveTileAsync(tileName, tileData, cancellationToken);

                // Add to runtime index
                _logger.LogInformation("📍 Adding tile {TileName} to runtime index...", tileName);
                await _mutator.AddTileToIndexAsync(tileName, (int)Math.Floor(latitude), (int)Math.Floor(longitude), cancellationToken);

                // Return the newly indexed tile
                var resolvedTile = _index.FindTileContaining(latitude, longitude);
                if (resolvedTile == null)
                {
                    throw new InvalidOperationException(
                        $"Tile {tileName} was added to index but not found. This should not happen.");
                }

                _logger.LogInformation(
                    "✅ Successfully resolved tile {TileName}. Total index size: {Count} tiles",
                    tileName, _mutator.GetIndexSize());

                return resolvedTile;
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to resolve tile for coordinates [{Lat:F2}, {Lon:F2}]",
                latitude, longitude);
            throw;
        }
    }

    /// <summary>
    /// Get current tile cache statistics.
    /// </summary>
    /// <returns>Number of tiles currently in the cache</returns>
    public int GetCacheSize() => _mutator.GetIndexSize();

    /// <summary>
    /// Check if a specific tile is already cached.
    /// </summary>
    /// <param name="tileName">SRTM tile name (e.g., "N46W113.hgt")</param>
    /// <returns>True if tile is in cache</returns>
    public bool IsTileCached(string tileName) => _mutator.IsTileIndexed(tileName);
}
