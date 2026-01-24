using WorldApi.Configuration;
using Microsoft.Extensions.Options;

namespace WorldApi.World.Dem;

/// <summary>
/// Background hosted service that processes DEM tile downloads.
/// 
/// Responsibilities:
/// - Poll for tiles in 'missing' state
/// - Atomically claim tiles (missing → downloading)
/// - Download DEM from public SRTM source
/// - Validate file integrity and size
/// - Upload to S3
/// - Update status to 'ready' or 'failed'
/// 
/// Design:
/// - Fire-and-forget: Request threads never block on downloads
/// - Atomic claiming: Only one worker processes each tile via database lock
/// - Retry resilience: Failures are recorded, manual retries available
/// - No tight retry loops: Poll on configurable interval (e.g., 30 seconds)
/// </summary>
public sealed class DemDownloadWorker : BackgroundService
{
    private readonly DemTileRepository _demTileRepository;
    private readonly PublicSrtmClient _publicSrtmClient;
    private readonly DemTileWriter _demTileWriter;
    private readonly DemTileIndex _demTileIndex;
    private readonly IWorldVersionCache _versionCache;
    private readonly ILogger<DemDownloadWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);  //TODO: Poll every 1 second this sucks fix this somehow

    public DemDownloadWorker(
        DemTileRepository demTileRepository,
        PublicSrtmClient publicSrtmClient,
        DemTileWriter demTileWriter,
        DemTileIndex demTileIndex,
        IWorldVersionCache versionCache,
        ILogger<DemDownloadWorker> logger)
    {
        _demTileRepository = demTileRepository ?? throw new ArgumentNullException(nameof(demTileRepository));
        _publicSrtmClient = publicSrtmClient ?? throw new ArgumentNullException(nameof(publicSrtmClient));
        _demTileWriter = demTileWriter ?? throw new ArgumentNullException(nameof(demTileWriter));
        _demTileIndex = demTileIndex ?? throw new ArgumentNullException(nameof(demTileIndex));
        _versionCache = versionCache ?? throw new ArgumentNullException(nameof(versionCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🌍 DEM Download Worker started. Poll interval: {Interval} seconds", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTilesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DEM Download Worker encountered an error during processing");
            }

            // Wait before next poll
            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 DEM Download Worker stopping");
                break;
            }
        }

        _logger.LogInformation("🛑 DEM Download Worker stopped");
    }

    /// <summary>
    /// Process all world versions, looking for missing or downloading DEM tiles.
    /// </summary>
    private async Task ProcessPendingTilesAsync(CancellationToken cancellationToken)
    {
        // Get active world versions from cache
        var activeVersions = _versionCache.GetActiveWorldVersions();
        
        if (activeVersions.Count == 0)
        {
            _logger.LogDebug("No active world versions found in cache");
            return;
        }

        _logger.LogDebug("🔍 Polling for pending DEM tiles across {VersionCount} world version(s)...", activeVersions.Count);

        // For each active world version, process missing and downloading tiles
        foreach (var worldVersion in activeVersions)
        {
            try
            {
                _logger.LogDebug("Checking world version: {Version} (id={Id})", worldVersion.Version, worldVersion.Id);
                
                // Process missing tiles first
                await ProcessTilesByStatusAsync(worldVersion.Version, "missing", 5, cancellationToken);
                
                // Then check downloading tiles (in case a worker crashed)
                await ProcessTilesByStatusAsync(worldVersion.Version, "downloading", 2, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing tiles for world version {Version}", worldVersion.Version);
            }
        }
    }

    /// <summary>
    /// Query and process tiles with a specific status.
    /// </summary>
    private async Task ProcessTilesByStatusAsync(string worldVersion, string status, int limit, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("📋 Querying tiles with status='{Status}', world='{WorldVersion}', limit={Limit}", status, worldVersion, limit);
            
            var tiles = await _demTileRepository.GetAllByStatusAsync(worldVersion, status, limit);
            
            // _logger.LogInformation("📊 Query result: Found {Count} tile(s) with status '{Status}' for world {Version}", 
            //     tiles.Count, status, worldVersion);
            
            if (tiles.Count == 0)
            {
                return;
            }

            foreach (var tile in tiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await ProcessTileAsync(worldVersion, tile.TileKey, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error querying tiles with status '{Status}' for world {Version}", status, worldVersion);
        }
    }

    /// <summary>
    /// Download and process a single DEM tile.
    /// Updates database status throughout the process.
    /// </summary>
    private async Task ProcessTileAsync(
        string worldVersion,
        string tileKey,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing DEM tile: world={WorldVersion}, tileKey={TileKey}",
            worldVersion, tileKey);

        try
        {
            // 1. Atomically claim the tile (missing → downloading)
            bool claimed = await _demTileRepository.TryClaimForDownloadAsync(worldVersion, tileKey);
            
            if (!claimed)
            {
                _logger.LogDebug(
                    "DEM tile already claimed by another worker: world={WorldVersion}, tileKey={TileKey}",
                    worldVersion, tileKey);
                return;
            }

            _logger.LogInformation(
                "DEM tile claimed for download: world={WorldVersion}, tileKey={TileKey}",
                worldVersion, tileKey);

            // 2. Download from public SRTM source
            _logger.LogInformation("Downloading SRTM tile: {TileKey}", tileKey);
            
            var hgtData = await _publicSrtmClient.FetchAndDecompressTileAsync(tileKey);
            if (hgtData == null || hgtData.Length == 0)
            {
                throw new InvalidOperationException($"SRTM download returned empty data for tile {tileKey}");
            }

            _logger.LogInformation("SRTM download complete: {TileKey}, size={Size} bytes", tileKey, hgtData.Length);

            // 3. Validate file integrity (SRTM tiles are fixed size)
            // Public dataset contains SRTM1 (3601x3601 = 25,934,402 bytes), may also have SRTM3 (1201x1201 = 2,884,802 bytes)
            int srtm3Size = 1201 * 1201 * 2;  // 2,884,802 bytes
            int srtm1Size = 3601 * 3601 * 2;  // 25,934,402 bytes
            if (hgtData.Length != srtm3Size && hgtData.Length != srtm1Size)
            {
                throw new InvalidOperationException(
                    $"SRTM tile {tileKey} has unexpected size {hgtData.Length}, expected {srtm3Size} (SRTM3) or {srtm1Size} (SRTM1)");
            }

            _logger.LogInformation("SRTM tile validation passed: {TileKey}", tileKey);

            // 4. Upload to S3
            _logger.LogInformation("Uploading to S3: tileKey={TileKey}", tileKey);

            string s3Key = await _demTileWriter.WriteTileAsync(tileKey, hgtData);

            _logger.LogInformation("S3 upload complete: s3Key={S3Key}", s3Key);

            // 5. Update database status to 'ready'
            await _demTileRepository.MarkReadyAsync(worldVersion, tileKey, s3Key);

            _logger.LogInformation(
                "DEM tile marked ready: world={WorldVersion}, tileKey={TileKey}, s3Key={S3Key}",
                worldVersion, tileKey, s3Key);

            // 6. Update in-memory index for lazy-fetch integration
            // Parse the S3 key to determine tile boundaries and create DemTile entry
            var demTile = CreateDemTileFromKey(tileKey, s3Key);
            _demTileIndex.Add(demTile);

            _logger.LogInformation("DEM tile indexed in memory: {TileKey}", tileKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DEM tile download failed: world={WorldVersion}, tileKey={TileKey}",
                worldVersion, tileKey);

            // Mark as failed in database with error message
            try
            {
                await _demTileRepository.MarkFailedAsync(worldVersion, tileKey, ex.Message);
            }
            catch (Exception markFailedEx)
            {
                _logger.LogError(markFailedEx,
                    "Failed to mark DEM tile as failed in database: world={WorldVersion}, tileKey={TileKey}",
                    worldVersion, tileKey);
            }
        }
    }

    /// <summary>
    /// Create a DemTile entry from a tile key for in-memory indexing.
    /// </summary>
    private static DemTile CreateDemTileFromKey(string tileKey, string s3Key)
    {
        // Parse tile key (e.g., "N46W113")
        char latPrefix = tileKey[0];
        char lonPrefix = tileKey[3];
        
        int lat = int.Parse(tileKey[1..3]);
        int lon = int.Parse(tileKey[4..]);

        // Adjust for hemisphere
        if (latPrefix == 'S') lat = -lat;
        if (lonPrefix == 'W') lon = -lon;

        // Each SRTM tile covers 1 degree × 1 degree
        double minLat = lat;
        double maxLat = lat + 1;
        double minLon = lon;
        double maxLon = lon + 1;

        return new DemTile
        {
            S3Key = s3Key,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon
        };
    }
}
