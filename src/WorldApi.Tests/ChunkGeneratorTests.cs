using Xunit;
using WorldApi.World;

namespace WorldApi.Tests;

public class ChunkGeneratorTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void Generate_WithSameCoordinates_ReturnsDeterministicResults()
    {
        // Arrange
        int chunkX = 0, chunkZ = 0;

        // Act
        var result1 = ChunkGenerator.Generate(chunkX, chunkZ);
        var result2 = ChunkGenerator.Generate(chunkX, chunkZ);

        // Assert
        var heights1 = GetHeights(result1);
        var heights2 = GetHeights(result2);

        Assert.Equal(heights1.Length, heights2.Length);
        for (int i = 0; i < heights1.Length; i++)
        {
            Assert.Equal(heights1[i], heights2[i], Tolerance);
        }
    }

    [Fact]
    public void Generate_WithDifferentCoordinates_ReturnsDifferentHeightmaps()
    {
        // Arrange & Act
        var chunk1 = ChunkGenerator.Generate(0, 0);
        var chunk2 = ChunkGenerator.Generate(1, 0);

        // Assert
        var heights1 = GetHeights(chunk1);
        var heights2 = GetHeights(chunk2);

        Assert.Equal(heights1.Length, heights2.Length);

        bool foundDifference = false;
        for (int i = 0; i < heights1.Length; i++)
        {
            if (Math.Abs(heights1[i] - heights2[i]) > Tolerance)
            {
                foundDifference = true;
                break;
            }
        }

        Assert.True(foundDifference, "Different chunks should produce different heightmaps");
    }

    [Fact]
    public void Generate_AdjacentChunksEastWest_ShareEdgeHeights()
    {
        // Arrange & Act
        var westChunk = ChunkGenerator.Generate(0, 0);
        var eastChunk = ChunkGenerator.Generate(1, 0);

        // Assert
        var westHeights = GetHeights(westChunk);
        var eastHeights = GetHeights(eastChunk);
        int resolution = GetResolution(westChunk);

        // East edge of west chunk should match west edge of east chunk
        for (int z = 0; z < resolution; z++)
        {
            int westEastEdgeIndex = z * resolution + (resolution - 1); // Last column
            int eastWestEdgeIndex = z * resolution + 0; // First column

            Assert.Equal(westHeights[westEastEdgeIndex], eastHeights[eastWestEdgeIndex], Tolerance);
        }
    }

    [Fact]
    public void Generate_AdjacentChunksNorthSouth_ShareEdgeHeights()
    {
        // Arrange & Act
        var northChunk = ChunkGenerator.Generate(0, 0);
        var southChunk = ChunkGenerator.Generate(0, 1);

        // Assert
        var northHeights = GetHeights(northChunk);
        var southHeights = GetHeights(southChunk);
        int resolution = GetResolution(northChunk);

        // South edge of north chunk should match north edge of south chunk
        for (int x = 0; x < resolution; x++)
        {
            int northSouthEdgeIndex = (resolution - 1) * resolution + x; // Last row
            int southNorthEdgeIndex = 0 * resolution + x; // First row

            Assert.Equal(northHeights[northSouthEdgeIndex], southHeights[southNorthEdgeIndex], Tolerance);
        }
    }

    [Fact]
    public void Generate_HeightMetadata_MatchesActualMinMax()
    {
        // Arrange & Act
        var result = ChunkGenerator.Generate(0, 0);

        // Assert
        var heights = GetHeights(result);
        var minHeight = GetMinHeight(result);
        var maxHeight = GetMaxHeight(result);

        float actualMin = heights.Min();
        float actualMax = heights.Max();

        Assert.Equal(actualMin, minHeight, Tolerance);
        Assert.Equal(actualMax, maxHeight, Tolerance);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, -1)]
    [InlineData(5, 7)]
    public void Generate_VariousCoordinates_ReturnsValidMetadata(int chunkX, int chunkZ)
    {
        // Act
        var result = ChunkGenerator.Generate(chunkX, chunkZ);

        // Assert
        var heights = GetHeights(result);
        var minHeight = GetMinHeight(result);
        var maxHeight = GetMaxHeight(result);
        var resolution = GetResolution(result);

        Assert.NotNull(heights);
        Assert.Equal(resolution * resolution, heights.Length);
        Assert.True(minHeight <= maxHeight);
        Assert.True(heights.All(h => h >= minHeight && h <= maxHeight));
    }

    // Helper methods to extract data from anonymous type using reflection
    private static float[] GetHeights(object chunk)
    {
        var terrainProp = chunk.GetType().GetProperty("terrain");
        var terrain = terrainProp!.GetValue(chunk);
        var heightsProp = terrain!.GetType().GetProperty("heights");
        return (float[])heightsProp!.GetValue(terrain)!;
    }

    private static int GetResolution(object chunk)
    {
        var terrainProp = chunk.GetType().GetProperty("terrain");
        var terrain = terrainProp!.GetValue(chunk);
        var resolutionProp = terrain!.GetType().GetProperty("resolution");
        return (int)resolutionProp!.GetValue(terrain)!;
    }

    private static float GetMinHeight(object chunk)
    {
        var terrainProp = chunk.GetType().GetProperty("terrain");
        var terrain = terrainProp!.GetValue(chunk);
        var minHeightProp = terrain!.GetType().GetProperty("minHeight");
        return (float)minHeightProp!.GetValue(terrain)!;
    }

    private static float GetMaxHeight(object chunk)
    {
        var terrainProp = chunk.GetType().GetProperty("terrain");
        var terrain = terrainProp!.GetValue(chunk);
        var maxHeightProp = terrain!.GetType().GetProperty("maxHeight");
        return (float)maxHeightProp!.GetValue(terrain)!;
    }
}
