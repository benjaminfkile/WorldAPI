namespace WorldApi.World;

public static class ChunkGenerator
{
    public static object Generate(int chunkX, int chunkZ)
    {
        // Deterministic terrain generation using a continuous mathematical function based on global coordinates.
        // This ensures smooth, seamless borders between adjacent chunks and identical results across restarts.
        // Heights are computed as floating-point values using sine waves for a simple, repeatable terrain pattern.
        // Future: Replace with real DEM data or advanced noise algorithms when available.

        const int resolution = 16;
        var (heights, minHeight, maxHeight) = GenerateHeightmap(chunkX, chunkZ, resolution);

        return new
        {
            chunkX,
            chunkZ,
            terrain = new
            {
                resolution,
                heights,
                scale = 1.0f, // Vertical exaggeration (default 1.0)
                minHeight,
                maxHeight
            },
            roads = Array.Empty<object>(),
            rivers = Array.Empty<object>()
        };
    }

    // Helper method to generate deterministic heightmap and min/max values for a chunk
    private static (float[] heights, float minHeight, float maxHeight) GenerateHeightmap(int chunkX, int chunkZ, int resolution)
    {
        var heights = new float[resolution * resolution];
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;

        // Loop over z (rows) then x (columns) for row-major order: index = z * resolution + x
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Calculate global coordinates for this point in the chunk
                // Use (resolution - 1) as spacing so adjacent chunks share edge coordinates
                float globalX = chunkX * (resolution - 1) + x;
                float globalZ = chunkZ * (resolution - 1) + z;

                // Generate height using a deterministic continuous function (combination of sine waves)
                float height = (float)(
                    Math.Sin(globalX * 0.1) * 2.0 +
                    Math.Sin(globalZ * 0.1) * 2.0 +
                    Math.Sin((globalX + globalZ) * 0.05) * 1.0
                );
                heights[z * resolution + x] = height;
                if (height < minHeight) minHeight = height;
                if (height > maxHeight) maxHeight = height;
            }
        }
        return (heights, minHeight, maxHeight);
    }
}
