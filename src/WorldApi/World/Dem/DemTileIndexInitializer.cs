namespace WorldApi.World.Dem;

/// <summary>
/// Background service that populates the DemTileIndex at application startup
/// by scanning S3 for available DEM tiles.
/// </summary>
public sealed class DemTileIndexInitializer : IHostedService
{
    private readonly DemTileIndexBuilder _builder;
    private readonly DemTileIndex _index;
    private readonly ILogger<DemTileIndexInitializer> _logger;

    public DemTileIndexInitializer(
        DemTileIndexBuilder builder,
        DemTileIndex index,
        ILogger<DemTileIndexInitializer> logger)
    {
        _builder = builder;
        _index = index;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Starting DEM tile index initialization...");

        try
        {
            // Ensure folder structure exists before building index
            await _builder.EnsureFolderStructureAsync();
            
            var populatedIndex = await _builder.BuildAsync();
            
            // Copy tiles into the singleton index
            foreach (var tile in populatedIndex.GetAllTiles())
            {
                _index.Add(tile);
            }

            _logger.LogInformation("✓ DEM tile index initialized with {TileCount} tile(s)", _index.Count);
            
            if (_index.Count == 0)
            {
                _logger.LogInformation("⚠️  No local DEM tiles found. Lazy-loading from public SRTM will be enabled at runtime.");
            }
        }
        catch (Exception ex)
        {
            // Only fail startup on critical S3 configuration errors, not on missing tiles
            // If S3 is reachable but empty, that's acceptable (lazy-load mode)
            _logger.LogError(ex, "Failed to initialize DEM tile index during startup");
            
            // Check if this is a configuration error vs. a retriable error
            if (ex is InvalidOperationException)
            {
                // Configuration issue - fail startup
                throw;
            }
            
            // For other exceptions (S3 timeouts, auth errors), also fail startup
            // because we can't determine if S3 is accessible
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed
        return Task.CompletedTask;
    }
}
