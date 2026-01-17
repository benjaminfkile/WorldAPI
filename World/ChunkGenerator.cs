namespace WorldApi.World;

public static class ChunkGenerator
{
    public static object Generate(int chunkX, int chunkZ)
    {
        var seed = HashCode.Combine(chunkX, chunkZ);
        var random = new Random(seed);

        const int resolution = 16;
        var heights = new int[resolution * resolution];

        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = random.Next(0, 10);
        }

        return new
        {
            chunkX,
            chunkZ,
            terrain = new
            {
                resolution,
                heights
            },
            roads = Array.Empty<object>(),
            rivers = Array.Empty<object>()
        };
    }
}
