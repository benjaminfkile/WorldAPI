using Microsoft.Extensions.Options;
using WorldApi.World.Config;
using WorldApi.World.Coordinates;
using WorldApi.World.Dem;
using WorldApi.World.Chunks;
using Moq;
using Microsoft.Extensions.Logging;

namespace WorldApi.Tests.Chunks;

public class TerrainChunkGeneratorTests
{
    private const double Tolerance = 0.000001;
    private const short MissingDataValue = -32768;

    private static WorldConfig CreateConfig()
    {
        return new WorldConfig
        {
            Origin = new OriginConfig
            {
                Latitude = 46.0,
                Longitude = -113.0
            },
            ChunkSizeMeters = 100,
            MetersPerDegreeLatitude = 111320
        };
    }

    private static SrtmTileData CreateSyntheticTile(double minLat, double minLon, short constantElevation)
    {
        const int srtmSize = 1201;
        var elevations = new short[srtmSize * srtmSize];
        Array.Fill(elevations, constantElevation);

        return new SrtmTileData
        {
            MinLatitude = minLat,
            MaxLatitude = minLat + 1.0,
            MinLongitude = minLon,
            MaxLongitude = minLon + 1.0,
            Width = srtmSize,
            Height = srtmSize,
            Elevations = elevations
        };
    }

    private static SrtmTileData CreateGradientTile(double minLat, double minLon)
    {
        const int srtmSize = 1201;
        var elevations = new short[srtmSize * srtmSize];
        
        // Create a simple gradient: elevation increases with row index
        for (int i = 0; i < elevations.Length; i++)
        {
            int row = i / srtmSize;
            elevations[i] = (short)(1000 + row); // 1000 to ~2200 meters
        }

        return new SrtmTileData
        {
            MinLatitude = minLat,
            MaxLatitude = minLat + 1.0,
            MinLongitude = minLon,
            MaxLongitude = minLon + 1.0,
            Width = srtmSize,
            Height = srtmSize,
            Elevations = elevations
        };
    }

    private static TerrainChunkGenerator CreateGenerator(
        WorldConfig config,
        DemTileIndex tileIndex,
        HgtTileCache cache)
    {
        var coordinateService = new WorldCoordinateService(Options.Create(config));
        
        // Create a fake loader that should never be called if cache is pre-populated
        var fakeLoader = new HgtTileLoader(null!, "test-bucket");

        // Create a mock logger
        var mockLogger = new Mock<ILogger<TerrainChunkGenerator>>();

        return new TerrainChunkGenerator(
            coordinateService,
            tileIndex,
            cache,
            fakeLoader,
            Options.Create(config),
            mockLogger.Object);
    }

    [Fact]
    public async Task GenerateAsync_WithGivenResolution_GeneratesCorrectGridSize()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateSyntheticTile(46.0, -113.0, 1500);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk = await generator.GenerateAsync(0, 0, 10);

        // Assert
        Assert.Equal(10, chunk.Resolution);
        Assert.Equal(121, chunk.Heights.Length); // (10 + 1) * (10 + 1)
    }

    [Fact]
    public async Task GenerateAsync_WithConstantElevation_NormalizesHeightsToZero()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateSyntheticTile(46.0, -113.0, 1500);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk = await generator.GenerateAsync(0, 0, 5);

        // Assert - all heights should be absolute elevation (1500m)
        Assert.All(chunk.Heights, h => Assert.Equal(1500.0f, h, Tolerance));
        Assert.Equal(1500.0, chunk.MinElevation, Tolerance);
        Assert.Equal(1500.0, chunk.MaxElevation, Tolerance);
    }

    [Fact]
    public async Task GenerateAsync_WithVaryingElevation_NormalizesWithMinAtZero()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateGradientTile(46.0, -113.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk = await generator.GenerateAsync(0, 0, 5);

        // Assert - heights should be in absolute elevation range
        float minHeight = chunk.Heights.Min();
        float maxHeight = chunk.Heights.Max();
        
        // Min/max heights should match MinElevation/MaxElevation metadata (with tolerance for float conversion)
        Assert.Equal(chunk.MinElevation, minHeight, 0.01); // Slightly larger tolerance for float precision
        Assert.Equal(chunk.MaxElevation, maxHeight, 0.01);
        
        // Heights should be reasonable elevation values (not negative, not too high)
        Assert.True(chunk.MinElevation >= 0 && chunk.MinElevation < 9000);
        Assert.True(chunk.MaxElevation >= 0 && chunk.MaxElevation < 9000);
    }

    [Fact]
    public async Task GenerateAsync_SameInputs_ProducesDeterministicOutput()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateGradientTile(46.0, -113.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act - generate same chunk twice
        var chunk1 = await generator.GenerateAsync(0, 0, 5);
        var chunk2 = await generator.GenerateAsync(0, 0, 5);

        // Assert
        Assert.Equal(chunk1.ChunkX, chunk2.ChunkX);
        Assert.Equal(chunk1.ChunkZ, chunk2.ChunkZ);
        Assert.Equal(chunk1.Resolution, chunk2.Resolution);
        Assert.Equal(chunk1.MinElevation, chunk2.MinElevation, Tolerance);
        Assert.Equal(chunk1.MaxElevation, chunk2.MaxElevation, Tolerance);
        Assert.Equal(chunk1.Heights.Length, chunk2.Heights.Length);
        
        for (int i = 0; i < chunk1.Heights.Length; i++)
        {
            Assert.Equal(chunk1.Heights[i], chunk2.Heights[i], Tolerance);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithMissingData_HandlesCorrectly()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        // Create tile with missing data
        const int srtmSize = 1201;
        var elevations = new short[srtmSize * srtmSize];
        Array.Fill(elevations, MissingDataValue);
        
        var srtmTile = new SrtmTileData
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            Width = srtmSize,
            Height = srtmSize,
            Elevations = elevations
        };
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk = await generator.GenerateAsync(0, 0, 5);

        // Assert - missing data should be normalized to 0
        Assert.All(chunk.Heights, h => Assert.Equal(0.0f, h, Tolerance));
        Assert.Equal(0.0, chunk.MinElevation, Tolerance);
        Assert.Equal(0.0, chunk.MaxElevation, Tolerance);
    }

    [Fact]
    public async Task GenerateAsync_NoTileFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex(); // Empty index
        var cache = new HgtTileCache();

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(0, 0, 5));
        
        Assert.Contains("No DEM tile found", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_SetsCorrectChunkCoordinates()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateSyntheticTile(46.0, -113.0, 1500);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk = await generator.GenerateAsync(5, 10, 5);

        // Assert
        Assert.Equal(5, chunk.ChunkX);
        Assert.Equal(10, chunk.ChunkZ);
    }

    [Fact]
    public async Task GenerateAsync_DifferentResolutions_GeneratesDifferentGridSizes()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateSyntheticTile(46.0, -113.0, 1500);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act
        var chunk5 = await generator.GenerateAsync(0, 0, 5);
        var chunk10 = await generator.GenerateAsync(0, 0, 10);
        var chunk20 = await generator.GenerateAsync(0, 0, 20);

        // Assert
        Assert.Equal(36, chunk5.Heights.Length);   // (5 + 1) * (5 + 1)
        Assert.Equal(121, chunk10.Heights.Length); // (10 + 1) * (10 + 1)
        Assert.Equal(441, chunk20.Heights.Length); // (20 + 1) * (20 + 1)
    }

    [Fact]
    public async Task GenerateAsync_UsesCacheWhenAvailable()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -113.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W113.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateSyntheticTile(46.0, -113.0, 1500);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);

        // Act - generate multiple chunks that use same tile
        var chunk1 = await generator.GenerateAsync(0, 0, 5);
        var chunk2 = await generator.GenerateAsync(1, 0, 5);

        // Assert - both should succeed (using cached tile)
        Assert.NotNull(chunk1);
        Assert.NotNull(chunk2);
        Assert.Equal(1, cache.Count); // Should still have only one tile in cache
    }

    [Fact]
    public async Task GenerateAsync_AdjacentChunksEastWest_ShareIdenticalEdgeHeights()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -114.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W114.hgt"
        };
        tileIndex.Add(demTile);

        // Use gradient tile for more interesting elevation values
        var srtmTile = CreateGradientTile(46.0, -114.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);
        const int resolution = 16;

        // Act - generate two adjacent chunks horizontally
        var westChunk = await generator.GenerateAsync(0, 0, resolution);
        var eastChunk = await generator.GenerateAsync(1, 0, resolution);

        // Assert - right edge of west chunk == left edge of east chunk
        int gridSize = resolution + 1;
        
        for (int z = 0; z < gridSize; z++)
        {
            int westRightEdgeIndex = z * gridSize + (gridSize - 1); // Last column
            int eastLeftEdgeIndex = z * gridSize + 0; // First column

            float westHeight = westChunk.Heights[westRightEdgeIndex];
            float eastHeight = eastChunk.Heights[eastLeftEdgeIndex];

            Assert.Equal(westHeight, eastHeight, Tolerance);
        }
    }

    [Fact]
    public async Task GenerateAsync_AdjacentChunksNorthSouth_ShareIdenticalEdgeHeights()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -114.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W114.hgt"
        };
        tileIndex.Add(demTile);

        // Use gradient tile for more interesting elevation values
        var srtmTile = CreateGradientTile(46.0, -114.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);
        const int resolution = 16;

        // Act - generate two adjacent chunks vertically
        var southChunk = await generator.GenerateAsync(0, 0, resolution);
        var northChunk = await generator.GenerateAsync(0, 1, resolution);

        // Assert - top edge of south chunk == bottom edge of north chunk
        int gridSize = resolution + 1;
        
        for (int x = 0; x < gridSize; x++)
        {
            int southTopEdgeIndex = (gridSize - 1) * gridSize + x; // Last row
            int northBottomEdgeIndex = 0 * gridSize + x; // First row

            float southHeight = southChunk.Heights[southTopEdgeIndex];
            float northHeight = northChunk.Heights[northBottomEdgeIndex];

            Assert.Equal(southHeight, northHeight, Tolerance);
        }
    }

    [Fact]
    public async Task GenerateAsync_MultipleAdjacentChunks_AllEdgesMatch()
    {
        // Arrange
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -114.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W114.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateGradientTile(46.0, -114.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);
        const int resolution = 8;
        int gridSize = resolution + 1;

        // Act - generate a 2x2 grid of chunks
        var chunk00 = await generator.GenerateAsync(0, 0, resolution);
        var chunk10 = await generator.GenerateAsync(1, 0, resolution);
        var chunk01 = await generator.GenerateAsync(0, 1, resolution);
        var chunk11 = await generator.GenerateAsync(1, 1, resolution);

        // Assert - verify all shared edges match
        
        // East-West edges
        for (int z = 0; z < gridSize; z++)
        {
            // chunk00 right edge == chunk10 left edge
            Assert.Equal(
                chunk00.Heights[z * gridSize + (gridSize - 1)],
                chunk10.Heights[z * gridSize + 0],
                Tolerance);

            // chunk01 right edge == chunk11 left edge
            Assert.Equal(
                chunk01.Heights[z * gridSize + (gridSize - 1)],
                chunk11.Heights[z * gridSize + 0],
                Tolerance);
        }

        // North-South edges
        for (int x = 0; x < gridSize; x++)
        {
            // chunk00 top edge == chunk01 bottom edge
            Assert.Equal(
                chunk00.Heights[(gridSize - 1) * gridSize + x],
                chunk01.Heights[0 * gridSize + x],
                Tolerance);

            // chunk10 top edge == chunk11 bottom edge
            Assert.Equal(
                chunk10.Heights[(gridSize - 1) * gridSize + x],
                chunk11.Heights[0 * gridSize + x],
                Tolerance);
        }

        // Corner vertices should also match
        Assert.Equal(
            chunk00.Heights[(gridSize - 1) * gridSize + (gridSize - 1)], // top-right of chunk00
            chunk11.Heights[0], // bottom-left of chunk11
            Tolerance);
    }

    [Fact]
    public void WorldCoordinateService_ConsistentConversion_ForSharedEdges()
    {
        // Arrange
        var config = CreateConfig();
        var service = new WorldCoordinateService(Options.Create(config));
        const int resolution = 16;
        int gridSize = resolution + 1;
        double cellSize = config.ChunkSizeMeters / resolution;

        // Act - Calculate coordinates for right edge of chunk (0,0) and left edge of chunk (1,0)
        // Use the SAME calculation method as ChunkHeightSampler to verify bit-exact equality
        var chunk0RightEdge = new List<LatLon>();
        var chunk1LeftEdge = new List<LatLon>();

        for (int z = 0; z < gridSize; z++)
        {
            // Right edge of chunk (0,0): x = resolution
            int globalCellX0 = 0 * resolution + resolution;
            int globalCellZ0 = 0 * resolution + z;
            double worldX0 = globalCellX0 * cellSize;
            double worldZ0 = globalCellZ0 * cellSize;
            chunk0RightEdge.Add(service.WorldMetersToLatLon(worldX0, worldZ0));

            // Left edge of chunk (1,0): x = 0
            int globalCellX1 = 1 * resolution + 0;
            int globalCellZ1 = 0 * resolution + z;
            double worldX1 = globalCellX1 * cellSize;
            double worldZ1 = globalCellZ1 * cellSize;
            chunk1LeftEdge.Add(service.WorldMetersToLatLon(worldX1, worldZ1));

            // Verify that globalCellX0 == globalCellX1 (both equal resolution)
            Assert.Equal(globalCellX0, globalCellX1);
            Assert.Equal(globalCellZ0, globalCellZ1);
        }

        // Assert - Coordinates must be EXACTLY identical
        for (int z = 0; z < gridSize; z++)
        {
            Assert.Equal(chunk0RightEdge[z].Latitude, chunk1LeftEdge[z].Latitude);
            Assert.Equal(chunk0RightEdge[z].Longitude, chunk1LeftEdge[z].Longitude);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public async Task GenerateAsync_VariousResolutions_EdgesMatchExactly(int resolution)
    {
        // Arrange - use a high-precision gradient tile to catch any floating-point issues
        var config = CreateConfig();
        var tileIndex = new DemTileIndex();
        var cache = new HgtTileCache();

        var demTile = new DemTile
        {
            MinLatitude = 46.0,
            MaxLatitude = 47.0,
            MinLongitude = -114.0,
            MaxLongitude = -112.0,
            S3Key = "dem/srtm/N46W114.hgt"
        };
        tileIndex.Add(demTile);

        var srtmTile = CreateGradientTile(46.0, -114.0);
        cache.Add(demTile.S3Key, srtmTile);

        var generator = CreateGenerator(config, tileIndex, cache);
        int gridSize = resolution + 1;

        // Act - generate adjacent chunks in positive directions
        var chunk00 = await generator.GenerateAsync(0, 0, resolution);
        var chunk10 = await generator.GenerateAsync(1, 0, resolution);
        var chunk01 = await generator.GenerateAsync(0, 1, resolution);

        // Assert - all shared edges must match EXACTLY (no tolerance, bit-exact equality)
        for (int i = 0; i < gridSize; i++)
        {
            // chunk00 east edge == chunk10 west edge
            int chunk00EastIdx = i * gridSize + (gridSize - 1);
            int chunk10WestIdx = i * gridSize + 0;
            Assert.Equal(chunk00.Heights[chunk00EastIdx], chunk10.Heights[chunk10WestIdx]);

            // chunk00 north edge == chunk01 south edge
            int chunk00NorthIdx = (gridSize - 1) * gridSize + i;
            int chunk01SouthIdx = 0 * gridSize + i;
            Assert.Equal(chunk00.Heights[chunk00NorthIdx], chunk01.Heights[chunk01SouthIdx]);
        }
    }
}
