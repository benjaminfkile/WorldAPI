using Amazon.S3;

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
        // _logger.LogInformation("Starting DEM tile index initialization...");

        try
        {
            var populatedIndex = await _builder.BuildAsync();
            
            // Copy tiles into the singleton index
            foreach (var tile in populatedIndex.GetAllTiles())
            {
                _index.Add(tile);
            }

            // Startup succeeds even with 0 tiles - allows lazy fetching
            // _logger.LogInformation("Loaded {TileCount} DEM tiles into index", _index.Count);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Bucket or prefix doesn't exist - this is OK, treat as empty
            // Allows startup with empty DEM folder for lazy fetching
            _logger.LogWarning("DEM bucket or folder not found, starting with empty index");
        }
        catch (Exception ex)
        {
            // S3 unreachable, configuration invalid, or other critical errors
            _logger.LogError(ex, "Failed to initialize DEM tile index");
            throw; // Fail startup only for critical errors
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed
        return Task.CompletedTask;
    }
}
