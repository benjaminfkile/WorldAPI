namespace WorldApi.World.Dem;

/// <summary>
/// Service for querying and managing DEM tile readiness status.
/// 
/// Responsibilities:
/// - Resolve coordinates to tile_key
/// - Query database for tile status
/// - Insert new tiles as 'missing' if they don't exist
/// - Fire-and-forget enqueue DEM downloads when tiles transition to missing
/// - Return status to clients for polling/gating logic
/// </summary>
public sealed class DemStatusService
{
    private readonly DemTileRepository _repository;
    private readonly ILogger<DemStatusService> _logger;
    private readonly Action<string, string>? _onTileMissingCallback;

    public DemStatusService(
        DemTileRepository repository,
        ILogger<DemStatusService> logger,
        Action<string, string>? onTileMissingCallback = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onTileMissingCallback = onTileMissingCallback;
    }

    /// <summary>
    /// Get DEM tile status for coordinates in a specific world version.
    /// 
    /// Flow:
    /// 1. Compute tile_key from latitude/longitude
    /// 2. Query database for status
    /// 3. If no row exists, create one with status='missing' and enqueue download
    /// 4. Return current status
    /// </summary>
    public async Task<DemStatusResponse> GetStatusAsync(string worldVersion, double latitude, double longitude)
    {
        // 1. Compute tile_key from coordinates
        string tileKey = SrtmTileNameCalculator.Calculate(latitude, longitude);

        _logger.LogInformation(
            "DEM status request: world={WorldVersion}, lat={Latitude}, lon={Longitude}, tileKey={TileKey}",
            worldVersion, latitude, longitude, tileKey);

        // 2. Get or create status row (idempotent insert if missing)
        var tileStatus = await _repository.GetOrCreateMissingAsync(worldVersion, tileKey);

        // 3. If newly inserted as 'missing', fire-and-forget enqueue download
        if (tileStatus.Status == "missing")
        {
            _logger.LogInformation(
                "DEM tile transitioned to missing: world={WorldVersion}, tileKey={TileKey}. Enqueueing download.",
                worldVersion, tileKey);

            // Fire-and-forget callback (typically enqueues to background worker queue)
            _onTileMissingCallback?.Invoke(worldVersion, tileKey);
        }

        // 4. Return current status
        return new DemStatusResponse
        {
            TileKey = tileKey,
            Status = tileStatus.Status,
            LastError = tileStatus.Status == "failed" ? tileStatus.LastError : null
        };
    }

    /// <summary>
    /// Check if a DEM tile is ready for chunk generation.
    /// Returns true only if status is exactly 'ready'.
    /// 
    /// IMPORTANT: This also auto-creates missing tiles so the worker can find them.
    /// If a tile doesn't exist, it's created with status='missing' and enqueued for download.
    /// </summary>
    public async Task<bool> IsTileReadyAsync(string worldVersion, double latitude, double longitude)
    {
        string tileKey = SrtmTileNameCalculator.Calculate(latitude, longitude);
        
        // Get or create - this ensures the tile entry exists for the worker to find
        var tileStatus = await _repository.GetOrCreateMissingAsync(worldVersion, tileKey);
        
        // If newly created as missing, fire-and-forget enqueue download
        if (tileStatus.Status == "missing")
        {
            _logger.LogDebug(
                "DEM tile auto-created and missing: world={WorldVersion}, tileKey={TileKey}. Enqueueing download.",
                worldVersion, tileKey);

            _onTileMissingCallback?.Invoke(worldVersion, tileKey);
        }

        return tileStatus.Status == "ready";
    }
}

/// <summary>
/// API response for DEM status endpoint.
/// </summary>
public record DemStatusResponse
{
    public string TileKey { get; init; } = string.Empty;
    public string Status { get; init; } = "missing"; // missing, downloading, ready, failed
    public string? LastError { get; init; } // Only populated if status is 'failed'
}
