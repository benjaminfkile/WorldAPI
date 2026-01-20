namespace WorldApi.World.Chunks;

/// <summary>
/// Binary serializer for terrain chunk data.
/// 
/// WIRE FORMAT (LOCKED - DO NOT MODIFY):
/// ======================================
/// This format is contractual and must remain stable for backwards compatibility.
/// Any changes require a new version number and migration support.
/// 
/// Structure (little-endian):
///   - byte       version        (1 byte)   - Format version, currently 1
///   - ushort     resolution     (2 bytes)  - Grid resolution (e.g., 16, 32, 64)
///   - double     minElevation   (8 bytes)  - Minimum elevation in meters (absolute)
///   - double     maxElevation   (8 bytes)  - Maximum elevation in meters (absolute)
///   - float[]    heights        (variable) - Height values in meters (absolute)
/// 
/// Heights Array:
///   - Length: (resolution + 1) × (resolution + 1)
///   - Order: Row-major (z * gridSize + x), where gridSize = resolution + 1
///   - Layout: Overlapping edges for seamless chunk boundaries
///     - Right edge of chunk (x, z) equals left edge of chunk (x+1, z)
///     - Top edge of chunk (x, z) equals bottom edge of chunk (x, z+1)
///   - Values: Absolute elevation in meters (NOT normalized to chunk min/max)
/// 
/// Total Size: 1 + 2 + 8 + 8 + (gridSize² × 4) bytes
/// Example (resolution=16): 1 + 2 + 8 + 8 + (17² × 4) = 1,175 bytes
/// </summary>
public static class TerrainChunkSerializer
{
    private const byte CurrentVersion = 1;

    public static byte[] Serialize(TerrainChunk chunk)
    {
        // Verify contract: heights array must be (resolution + 1)²
        int gridSize = chunk.Resolution + 1;
        int expectedLength = gridSize * gridSize;
        System.Diagnostics.Debug.WriteLine(
            $"[TerrainChunkSerializer] SERIALIZE: chunk=({chunk.ChunkX},{chunk.ChunkZ}) resolution={chunk.Resolution} gridSize={gridSize} expected={expectedLength} actual={chunk.Heights.Length}");
        if (chunk.Heights.Length != expectedLength)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TerrainChunkSerializer] ERROR: Heights length mismatch!");
            throw new InvalidDataException(
            $"Terrain serialization contract violated for chunk ({chunk.ChunkX},{chunk.ChunkZ}) r={chunk.Resolution}: expected heights length {expectedLength} (gridSize {gridSize}²), got {chunk.Heights.Length}");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Write version (1 byte)
        writer.Write(CurrentVersion);

        // Write resolution (2 bytes)
        writer.Write((ushort)chunk.Resolution);

        // Write min/max elevation (8 bytes each)
        writer.Write(chunk.MinElevation);
        writer.Write(chunk.MaxElevation);

        // Write heights array (4 bytes per float)
        // Row-major order: heights[z * gridSize + x]
        foreach (float height in chunk.Heights)
        {
            writer.Write(height);
        }

        return stream.ToArray();
    }

    public static TerrainChunk Deserialize(byte[] data, int chunkX, int chunkZ)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        // Read and validate version (1 byte)
        byte version = reader.ReadByte();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported terrain chunk version: {version}. Expected: {CurrentVersion}");
        }

        // Read resolution (2 bytes)
        ushort resolution = reader.ReadUInt16();

        // Read min/max elevation (8 bytes each)
        double minElevation = reader.ReadDouble();
        double maxElevation = reader.ReadDouble();

        // Read heights array (4 bytes per float)
        // Contract: Length must be (resolution + 1)² for overlapping edge vertices
        int gridSize = resolution + 1;
        int expectedCount = gridSize * gridSize;

        // Verify the total payload size matches the contract exactly
        // Total bytes: 1 (version) + 2 (resolution) + 8 (min) + 8 (max) + 4 * expectedCount (heights)
        int expectedTotalBytes = 1 + 2 + 8 + 8 + (expectedCount * 4);
        if (data.Length != expectedTotalBytes)
        {
            throw new InvalidDataException(
            $"Terrain chunk byte size mismatch: expected {expectedTotalBytes} bytes for resolution {resolution} (gridSize {gridSize}), got {data.Length}");
        }
        var heights = new float[expectedCount];
        for (int i = 0; i < expectedCount; i++)
        {
            heights[i] = reader.ReadSingle();
        }

        // Verify contract compliance
        if (heights.Length != expectedCount)
        {
            throw new InvalidDataException(
            $"Deserialized heights array length mismatch: expected {expectedCount}, got {heights.Length}");
        }

        return new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = resolution,
            Heights = heights,
            MinElevation = minElevation,
            MaxElevation = maxElevation
        };
    }
}
