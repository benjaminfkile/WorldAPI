using Microsoft.Extensions.Options;

namespace WorldApi.World;

public sealed class TerrainChunkGenerator
{
    private readonly WorldCoordinateService _coordinateService;
    private readonly DemTileIndex _tileIndex;
    private readonly HgtTileCache _tileCache;
    private readonly HgtTileLoader _tileLoader;
    private readonly WorldConfig _config;

    public TerrainChunkGenerator(
        WorldCoordinateService coordinateService,
        DemTileIndex tileIndex,
        HgtTileCache tileCache,
        HgtTileLoader tileLoader,
        IOptions<WorldConfig> config)
    {
        _coordinateService = coordinateService;
        _tileIndex = tileIndex;
        _tileCache = tileCache;
        _tileLoader = tileLoader;
        _config = config.Value;
    }

    public async Task<TerrainChunk> GenerateAsync(int chunkX, int chunkZ, int resolution)
    {
        // Step 1: Determine lat/lon bounds of the chunk
        var chunkOrigin = _coordinateService.GetChunkOriginLatLon(chunkX, chunkZ);
        
        // For simplicity, we'll sample at the chunk origin to find the tile
        // In production, you might need to handle chunks that span multiple tiles
        double centerLat = chunkOrigin.Latitude + (_config.ChunkSizeMeters / 2.0) / 111320.0;
        double centerLon = chunkOrigin.Longitude + (_config.ChunkSizeMeters / 2.0) / (111320.0 * Math.Cos(chunkOrigin.Latitude * Math.PI / 180.0));

        // Step 2: Resolve required DEM tile
        var demTile = _tileIndex.FindTileContaining(centerLat, centerLon);
        if (demTile == null)
        {
            throw new InvalidOperationException(
                $"No DEM tile found for chunk ({chunkX}, {chunkZ}) at lat/lon ({centerLat:F6}, {centerLon:F6})");
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

        // Step 5: Normalize heights
        var normalized = HeightNormalizer.Normalize(rawHeights);

        // Step 6: Return TerrainChunk
        return new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = resolution,
            Heights = normalized.Heights,
            MinElevation = normalized.MinElevation,
            MaxElevation = normalized.MaxElevation
        };
    }
}
