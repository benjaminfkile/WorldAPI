namespace WorldApi.World;

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
        // _logger.LogInformation("Starting DEM tile index initialization...");

        try
        {
            var populatedIndex = await _builder.BuildAsync();
            
            // Copy tiles into the singleton index
            foreach (var tile in populatedIndex.GetAllTiles())
            {
                _index.Add(tile);
            }

            // _logger.LogInformation("Loaded {TileCount} DEM tiles into index", _index.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DEM tile index");
            throw; // Fail startup if index can't be built
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed
        return Task.CompletedTask;
    }
}
