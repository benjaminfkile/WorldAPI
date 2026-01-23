using System.Collections.Concurrent;

namespace WorldApi.World.Dem;

/// <summary>
/// Adds newly fetched DEM tiles to the runtime index in a thread-safe manner.
/// Enables lazy-loaded tiles to become discoverable without application restart.
/// 
/// Responsibilities:
/// - Add tiles to DemTileIndex after they are fetched and persisted
/// - Ensure thread-safety for concurrent additions
/// - Provide idempotent operations (adding the same tile multiple times is safe)
/// - No restart required to discover new tiles
/// </summary>
public sealed class RuntimeDemIndexMutator
{
    private readonly DemTileIndex _index;
    private readonly ILogger<RuntimeDemIndexMutator> _logger;
    
    // Lock for thread-safe index mutations
    // Using ReaderWriterLockSlim would be overkill; simple lock is sufficient
    // since Add operations are infrequent compared to queries
    private readonly object _indexLock = new object();

    public RuntimeDemIndexMutator(
        DemTileIndex index,
        ILogger<RuntimeDemIndexMutator> logger)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Add a tile to the runtime index after it has been fetched and persisted.
    /// 
    /// Thread-safe and idempotent:
    /// - Multiple concurrent calls are serialized
    /// - Adding the same tile twice overwrites the first (idempotent)
    /// - No restart required
    /// </summary>
    /// <param name="tileName">SRTM tile name (e.g., "N46W113.hgt")</param>
    /// <param name="latitude">Latitude of the tile's southwest corner</param>
    /// <param name="longitude">Longitude of the tile's southwest corner</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentException">If tile name or coordinates are invalid</exception>
    public Task AddTileToIndexAsync(
        string tileName,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(tileName))
            throw new ArgumentException("Tile name cannot be null or empty", nameof(tileName));

        if (latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90");

        if (longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180");

        // Check cancellation
        cancellationToken.ThrowIfCancellationRequested();

        lock (_indexLock)
        {
            try
            {
                // Compute S3 key from tile name
                // S3 key format: dem/srtm/{tileName}
                string s3Key = $"dem/srtm/{tileName}";

                // Compute geographic bounds
                // SRTM tiles are 1x1 degree, with the tile name representing the southwest corner
                double minLatitude = latitude;
                double maxLatitude = latitude + 1;
                double minLongitude = longitude;
                double maxLongitude = longitude + 1;

                // Create the tile record
                var tile = new DemTile
                {
                    MinLatitude = minLatitude,
                    MaxLatitude = maxLatitude,
                    MinLongitude = minLongitude,
                    MaxLongitude = maxLongitude,
                    S3Key = s3Key
                };

                // Add to index (idempotent; same S3Key will be replaced)
                _index.Add(tile);

                _logger.LogInformation(
                    "✓ Added tile {TileName} to runtime index. Coverage: [{MinLat:F1}, {MaxLat:F1}] x [{MinLon:F1}, {MaxLon:F1}]. Index now contains {Count} tile(s)",
                    tileName,
                    minLatitude,
                    maxLatitude,
                    minLongitude,
                    maxLongitude,
                    _index.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "✗ Failed to add tile {TileName} to runtime index", tileName);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if a tile is currently in the runtime index.
    /// Thread-safe read operation.
    /// </summary>
    /// <param name="tileName">SRTM tile name</param>
    /// <returns>True if tile exists in index</returns>
    public bool IsTileIndexed(string tileName)
    {
        if (string.IsNullOrWhiteSpace(tileName))
            return false;

        string s3Key = $"dem/srtm/{tileName}";

        lock (_indexLock)
        {
            // Check if any existing tile has this S3 key
            var existingTile = _index.GetAllTiles().FirstOrDefault(t => t.S3Key == s3Key);
            return existingTile != null;
        }
    }

    /// <summary>
    /// Get the current number of tiles in the index.
    /// Thread-safe read operation.
    /// </summary>
    public int GetIndexSize()
    {
        lock (_indexLock)
        {
            return _index.Count;
        }
    }
}
