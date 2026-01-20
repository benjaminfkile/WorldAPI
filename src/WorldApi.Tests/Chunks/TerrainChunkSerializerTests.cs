using WorldApi.World.Chunks;

namespace WorldApi.Tests.Chunks;

public class TerrainChunkSerializerTests
{
    private const double Tolerance = 0.000001;

    private static TerrainChunk CreateSampleChunk(int chunkX, int chunkZ, int resolution)
    {
        int gridSize = resolution + 1;
        var heights = new float[gridSize * gridSize];
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = i * 0.5f; // Simple gradient
        }

        return new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = resolution,
            Heights = heights,
            MinElevation = 1000.0,
            MaxElevation = 1500.0
        };
    }

    [Fact]
    public void SerializeDeserialize_Roundtrip_PreservesAllData()
    {
        // Arrange
        var originalChunk = CreateSampleChunk(5, 10, 10);

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(originalChunk);
        var deserializedChunk = TerrainChunkSerializer.Deserialize(serialized, 5, 10);

        // Assert
        Assert.Equal(originalChunk.ChunkX, deserializedChunk.ChunkX);
        Assert.Equal(originalChunk.ChunkZ, deserializedChunk.ChunkZ);
        Assert.Equal(originalChunk.Resolution, deserializedChunk.Resolution);
        Assert.Equal(originalChunk.MinElevation, deserializedChunk.MinElevation, Tolerance);
        Assert.Equal(originalChunk.MaxElevation, deserializedChunk.MaxElevation, Tolerance);
        Assert.Equal(originalChunk.Heights.Length, deserializedChunk.Heights.Length);

        for (int i = 0; i < originalChunk.Heights.Length; i++)
        {
            Assert.Equal(originalChunk.Heights[i], deserializedChunk.Heights[i], Tolerance);
        }
    }

    [Fact]
    public void Serialize_SameInput_ProducesDeterministicOutput()
    {
        // Arrange
        var chunk = CreateSampleChunk(0, 0, 5);

        // Act
        byte[] result1 = TerrainChunkSerializer.Serialize(chunk);
        byte[] result2 = TerrainChunkSerializer.Serialize(chunk);

        // Assert
        Assert.Equal(result1.Length, result2.Length);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Serialize_IncludesCorrectResolution()
    {
        // Arrange
        var chunk5 = CreateSampleChunk(0, 0, 5);
        var chunk10 = CreateSampleChunk(0, 0, 10);

        // Act
        byte[] serialized5 = TerrainChunkSerializer.Serialize(chunk5);
        byte[] serialized10 = TerrainChunkSerializer.Serialize(chunk10);
        
        var deserialized5 = TerrainChunkSerializer.Deserialize(serialized5, 0, 0);
        var deserialized10 = TerrainChunkSerializer.Deserialize(serialized10, 0, 0);

        // Assert
        Assert.Equal(5, deserialized5.Resolution);
        Assert.Equal(10, deserialized10.Resolution);
    }

    [Fact]
    public void Serialize_IncludesCorrectHeightCount()
    {
        // Arrange
        var chunk = CreateSampleChunk(0, 0, 10);

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.Equal(121, deserialized.Heights.Length); // (10 + 1) * (10 + 1)
    }

    [Fact]
    public void Deserialize_RestoresChunkCoordinates()
    {
        // Arrange
        var chunk = CreateSampleChunk(100, 200, 5);

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 100, 200);

        // Assert
        Assert.Equal(100, deserialized.ChunkX);
        Assert.Equal(200, deserialized.ChunkZ);
    }

    [Fact]
    public void Serialize_PreservesMinMaxElevation()
    {
        // Arrange
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 4,
            Heights = new float[25],
            MinElevation = 1234.567,
            MaxElevation = 9876.543
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.Equal(1234.567, deserialized.MinElevation, Tolerance);
        Assert.Equal(9876.543, deserialized.MaxElevation, Tolerance);
    }

    [Fact]
    public void Serialize_PreservesHeightValues()
    {
        // Arrange
        var heights = new float[] { 0.0f, 1.5f, -2.3f, 100.25f, 999.99f };
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 1,
            Heights = new float[4] { heights[0], heights[1], heights[2], heights[3] },
            MinElevation = 1000.0,
            MaxElevation = 2000.0
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.Equal(heights[0], deserialized.Heights[0], Tolerance);
        Assert.Equal(heights[1], deserialized.Heights[1], Tolerance);
        Assert.Equal(heights[2], deserialized.Heights[2], Tolerance);
        Assert.Equal(heights[3], deserialized.Heights[3], Tolerance);
    }

    [Fact]
    public void Serialize_WithZeroHeights_PreservesZeros()
    {
        // Arrange
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 4,
            Heights = new float[25], // All zeros
            MinElevation = 0.0,
            MaxElevation = 0.0
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.All(deserialized.Heights, h => Assert.Equal(0.0f, h, Tolerance));
    }

    [Fact]
    public void Serialize_WithNegativeHeights_PreservesValues()
    {
        // Arrange
        var heights = new float[4] { -100.0f, -50.0f, -25.0f, -10.0f };
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 1,
            Heights = heights,
            MinElevation = -100.0,
            MaxElevation = -10.0
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        for (int i = 0; i < heights.Length; i++)
        {
            Assert.Equal(heights[i], deserialized.Heights[i], Tolerance);
        }
    }

    [Fact]
    public void Serialize_DifferentResolutions_ProduceDifferentSizes()
    {
        // Arrange
        var chunk5 = CreateSampleChunk(0, 0, 5);
        var chunk10 = CreateSampleChunk(0, 0, 10);

        // Act
        byte[] serialized5 = TerrainChunkSerializer.Serialize(chunk5);
        byte[] serialized10 = TerrainChunkSerializer.Serialize(chunk10);

        // Assert - 10x10 should produce larger binary than 5x5
        Assert.True(serialized10.Length > serialized5.Length);
    }

    [Fact]
    public void Deserialize_InvalidVersion_ThrowsInvalidDataException()
    {
        // Arrange
        var chunk = CreateSampleChunk(0, 0, 5);
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        
        // Corrupt the version byte (first byte)
        serialized[0] = 99;

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(
            () => TerrainChunkSerializer.Deserialize(serialized, 0, 0));
        
        Assert.Contains("version", exception.Message.ToLower());
    }

    [Fact]
    public void Serialize_LargeResolution_HandlesCorrectly()
    {
        // Arrange
        var chunk = CreateSampleChunk(0, 0, 100);

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.Equal(100, deserialized.Resolution);
        Assert.Equal(10201, deserialized.Heights.Length); // (100 + 1) * (100 + 1)
    }

    [Fact]
    public void Serialize_MultipleTimes_ProducesSameResult()
    {
        // Arrange
        var chunk = CreateSampleChunk(5, 10, 8);

        // Act
        byte[] result1 = TerrainChunkSerializer.Serialize(chunk);
        byte[] result2 = TerrainChunkSerializer.Serialize(chunk);
        byte[] result3 = TerrainChunkSerializer.Serialize(chunk);

        // Assert - all should be identical
        Assert.Equal(result1, result2);
        Assert.Equal(result1, result3);
    }

    [Fact]
    public void Serialize_RowMajorOrder_PreservesOrder()
    {
        // Arrange - create known pattern
        var heights = new float[9];
        for (int i = 0; i < 9; i++)
        {
            heights[i] = i * 10.0f;
        }

        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 2,
            Heights = heights,
            MinElevation = 0.0,
            MaxElevation = 80.0
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert - order preserved
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(heights[i], deserialized.Heights[i], Tolerance);
        }
    }

    [Fact]
    public void Serialize_MinimalChunk_WorksCorrectly()
    {
        // Arrange - smallest valid chunk (2x2)
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 1,
            Heights = new float[4] { 1.0f, 2.0f, 3.0f, 4.0f },
            MinElevation = 100.0,
            MaxElevation = 200.0
        };

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);

        // Assert
        Assert.Equal(1, deserialized.Resolution);
        Assert.Equal(4, deserialized.Heights.Length);
        Assert.Equal(100.0, deserialized.MinElevation, Tolerance);
        Assert.Equal(200.0, deserialized.MaxElevation, Tolerance);
    }

    [Theory]
    [InlineData(1)]   // 2x2 grid
    [InlineData(4)]   // 5x5 grid
    [InlineData(8)]   // 9x9 grid
    [InlineData(16)]  // 17x17 grid
    [InlineData(32)]  // 33x33 grid
    [InlineData(64)]  // 65x65 grid
    public void Serialize_ExactBufferSize_MatchesContract(int resolution)
    {
        // Arrange
        int gridSize = resolution + 1;
        int expectedHeightCount = gridSize * gridSize;
        int expectedByteSize = 1 + 2 + 8 + 8 + (expectedHeightCount * 4);

        var chunk = CreateSampleChunk(0, 0, resolution);

        // Act
        byte[] serialized = TerrainChunkSerializer.Serialize(chunk);

        // Assert - exact contract enforcement
        Assert.Equal(expectedByteSize, serialized.Length);
        Assert.Equal(expectedHeightCount, chunk.Heights.Length);
        
        // Verify roundtrip maintains exact size
        var deserialized = TerrainChunkSerializer.Deserialize(serialized, 0, 0);
        Assert.Equal(expectedHeightCount, deserialized.Heights.Length);
    }

    [Fact]
    public void Serialize_WrongHeightCount_Throws()
    {
        // Arrange - intentionally create chunk with wrong height count
        var chunk = new TerrainChunk
        {
            ChunkX = 0,
            ChunkZ = 0,
            Resolution = 16,
            Heights = new float[256],  // 16*16 instead of 17*17 (289)
            MinElevation = 1000.0,
            MaxElevation = 1500.0
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => TerrainChunkSerializer.Serialize(chunk));
        Assert.Contains("expected heights length 289", ex.Message);
        Assert.Contains("chunk (0,0)", ex.Message);
        Assert.Contains("r=16", ex.Message);
    }

    [Fact]
    public void Deserialize_WrongBufferSize_Throws()
    {
        // Arrange - create a valid 16x16 serialized chunk, then corrupt it by removing bytes
        var validChunk = CreateSampleChunk(0, 0, 16);
        byte[] validSerialized = TerrainChunkSerializer.Serialize(validChunk);

        // Truncate to simulate undersized payload
        byte[] corruptedData = new byte[validSerialized.Length - 10];
        Array.Copy(validSerialized, corruptedData, corruptedData.Length);

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => TerrainChunkSerializer.Deserialize(corruptedData, 0, 0));
        Assert.Contains("byte size mismatch", ex.Message);
    }
}
