using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using WorldApi.World.Config;
using WorldApi.World.Coordinates;
using WorldApi.World.Dem;

namespace WorldApi.World.Chunks;

public sealed class TerrainChunkGenerator
{
    private readonly WorldCoordinateService _coordinateService;
    private readonly DemTileResolver _tileResolver;
    private readonly HgtTileCache _tileCache;
    private readonly HgtTileLoader _tileLoader;
    private readonly WorldConfig _config;
    private readonly ILogger<TerrainChunkGenerator> _logger;

    public TerrainChunkGenerator(
        WorldCoordinateService coordinateService,
        DemTileResolver tileResolver,
        HgtTileCache tileCache,
        HgtTileLoader tileLoader,
        IOptions<WorldConfig> config,
        ILogger<TerrainChunkGenerator> logger)
    {
        _coordinateService = coordinateService;
        _tileResolver = tileResolver;
        _tileCache = tileCache;
        _tileLoader = tileLoader;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<TerrainChunk> GenerateAsync(int chunkX, int chunkZ, int resolution)
    {
        // _logger.LogInformation("[TRACE] GenerateAsync start: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
        //     chunkX, chunkZ, resolution);

        // Step 1: Determine lat/lon bounds of the chunk
        var chunkOrigin = _coordinateService.GetChunkOriginLatLon(chunkX, chunkZ);
        
        // For simplicity, we'll sample at the chunk origin to find the tile
        // In production, you might need to handle chunks that span multiple tiles
        double centerLat = chunkOrigin.Latitude + (_config.ChunkSizeMeters / 2.0) / 111320.0;
        double centerLon = chunkOrigin.Longitude + (_config.ChunkSizeMeters / 2.0) / (111320.0 * Math.Cos(chunkOrigin.Latitude * Math.PI / 180.0));

        // Step 2: Resolve required DEM tile (with lazy fetch if missing)
        DemTile demTile;
        try
        {
            demTile = await _tileResolver.ResolveTileAsync(centerLat, centerLon);
        }
        catch (TileNotFoundException ex)
        {
            _logger.LogWarning("DEM tile not available for chunk ({ChunkX}, {ChunkZ}) at lat/lon ({CenterLat:F6}, {CenterLon:F6}): {Message}",
                chunkX, chunkZ, centerLat, centerLon, ex.Message);
            throw new InvalidOperationException(
                $"No DEM tile available for chunk ({chunkX}, {chunkZ}) at lat/lon ({centerLat:F6}, {centerLon:F6})", ex);
        }

        // Step 3: Load tile via cache/loader
        if (!_tileCache.TryGet(demTile.S3Key, out SrtmTileData? tileData))
        {
            tileData = await _tileLoader.LoadAsync(demTile);
            _tileCache.Add(demTile.S3Key, tileData);
        }

        // Step 4: Sample raw height grid
        double[] rawHeights = ChunkHeightSampler.SampleHeights(
            chunkX,
            chunkZ,
            resolution,
            _coordinateService,
            tileData!,
            _config.ChunkSizeMeters);

        // _logger.LogInformation("[TRACE] After SampleHeights: ChunkX={ChunkX}, ChunkZ={ChunkZ}, RawHeightsLength={RawHeightsLength}",
        //     chunkX, chunkZ, rawHeights.Length);

        // Step 5: Normalize heights
        var normalized = HeightNormalizer.Normalize(rawHeights);

        // _logger.LogInformation("[TRACE] After Normalize: ChunkX={ChunkX}, ChunkZ={ChunkZ}, NormalizedHeightsLength={NormalizedHeightsLength}",
        //     chunkX, chunkZ, normalized.Heights.Length);

        // Guard: ensure heights match (resolution + 1)^2 exactly
        int gridSize = resolution + 1;
        int expectedLength = gridSize * gridSize;
        if (normalized.Heights.Length != expectedLength)
        {
            _logger.LogError("[GUARD] Heights length mismatch: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, Expected={ExpectedLength}, Actual={ActualLength}",
                chunkX, chunkZ, resolution, expectedLength, normalized.Heights.Length);
            System.Diagnostics.Debug.WriteLine(
                $"[terrain] INVALID HEIGHTS LENGTH chunk=({chunkX},{chunkZ}) r={resolution} expected={expectedLength} actual={normalized.Heights.Length}");
            throw new InvalidDataException(
                $"Terrain heights length mismatch for chunk ({chunkX},{chunkZ}) r={resolution}: expected {expectedLength} (gridSize {gridSize}²), got {normalized.Heights.Length}");
        }

        // Step 6: Return TerrainChunk
        var result = new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = resolution,
            Heights = normalized.Heights,
            MinElevation = normalized.MinElevation,
            MaxElevation = normalized.MaxElevation
        };

        // _logger.LogInformation("[TRACE] GenerateAsync complete: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, FinalHeightsLength={FinalHeightsLength}",
        //     chunkX, chunkZ, resolution, result.Heights.Length);

        return result;
    }
}
