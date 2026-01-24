using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using WorldApi.World.Config;
using WorldApi.World.Coordinates;

namespace WorldApi.World.Chunks;

/// <summary>
/// Generates a single, minimal-resolution anchor chunk at world coordinates (0, 0).
///
/// Purpose:
/// - Lock world-space to real-world latitude/longitude from the configured origin
/// - Provide a deterministic foundation for the world at startup
/// - Enable clients to know the world's anchor point without DEM data
///
/// Behavior:
/// - Always generates at chunk (0, 0)
/// - Uses a very small resolution (2x2 minimum to satisfy contract: (resolution+1)² grid)
/// - Generates flat terrain (all heights = 0) without requiring DEM data
/// - World-space size matches the canonical chunk size (ChunkSizeMeters)
/// - Immutable: resolution choice does not affect world-space math or chunk indexing
/// - Idempotent: safe to run on every boot; only persisted if not already present
/// </summary>
public sealed class AnchorChunkGenerator
{
    private const int AnchorResolution = 2;  // Minimal: 3x3 vertex grid = (2+1)²
    private const float FlatElevation = 0f;   // Flat terrain (no elevation)
    private const string AnchorLayer = "terrain";

    private readonly WorldConfig _config;
    private readonly WorldCoordinateService _coordinateService;
    private readonly ILogger<AnchorChunkGenerator> _logger;

    public AnchorChunkGenerator(
        IOptions<WorldConfig> config,
        WorldCoordinateService coordinateService,
        ILogger<AnchorChunkGenerator> logger)
    {
        _config = config.Value;
        _coordinateService = coordinateService;
        _logger = logger;
    }

    /// <summary>
    /// Generate the anchor chunk for world coordinates (0, 0).
    /// Creates a flat terrain chunk with minimal resolution.
    /// 
    /// Returns a TerrainChunk ready for persistence to S3.
    /// </summary>
    public TerrainChunk GenerateAnchorChunk()
    {
        const int chunkX = 0;
        const int chunkZ = 0;

        _logger.LogInformation(
            "Generating anchor chunk at ({ChunkX}, {ChunkZ}) with resolution {Resolution}",
            chunkX, chunkZ, AnchorResolution);

        // Validate that chunk (0, 0) is at the world origin
        var originLatLon = _coordinateService.GetChunkOriginLatLon(chunkX, chunkZ);
        _logger.LogInformation(
            "Anchor chunk origin: Latitude={Latitude:F6}, Longitude={Longitude:F6}",
            originLatLon.Latitude, originLatLon.Longitude);

        // Generate flat heightmap: (resolution + 1) × (resolution + 1) grid
        int gridSize = AnchorResolution + 1;  // 3x3 grid
        int vertexCount = gridSize * gridSize;  // 9 vertices

        var heights = new float[vertexCount];

        // Fill grid with flat elevation (all zeros)
        // Row-major order: heights[z * gridSize + x]
        for (int i = 0; i < vertexCount; i++)
        {
            heights[i] = FlatElevation;
        }

        var chunk = new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = AnchorResolution,
            Heights = heights,
            MinElevation = FlatElevation,
            MaxElevation = FlatElevation
        };

        _logger.LogInformation(
            "✓ Anchor chunk generated: {VertexCount} vertices, all elevations = {Elevation}",
            vertexCount, FlatElevation);

        return chunk;
    }

    /// <summary>
    /// Get the S3 key for the anchor chunk.
    /// Format: "chunks/{worldVersion}/terrain/0_0_r{resolution}.bin"
    /// </summary>
    public string GetAnchorChunkS3Key(string worldVersion)
    {
        return $"chunks/{worldVersion}/{AnchorLayer}/0_0_r{AnchorResolution}.bin";
    }

    /// <summary>
    /// Get the layer name for anchor chunks (always "terrain").
    /// </summary>
    public string GetAnchorLayer() => AnchorLayer;

    /// <summary>
    /// Get the resolution of anchor chunks.
    /// </summary>
    public int GetAnchorResolution() => AnchorResolution;
}
