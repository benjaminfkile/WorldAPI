using Microsoft.Extensions.Options;
using WorldApi.World;

namespace WorldApi.Tests;

public class TerrainChunkGeneratorTests
{
    private const double Tolerance = 0.000001;
    private const short MissingDataValue = -32768;

    private static WorldConfig CreateConfig()
    {
        return new WorldConfig
        {
            Version = "v1",
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

        return new TerrainChunkGenerator(
            coordinateService,
            tileIndex,
            cache,
            fakeLoader,
            Options.Create(config));
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
        Assert.Equal(100, chunk.Heights.Length); // 10 * 10
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

        // Assert - all heights should be 0 (normalized)
        Assert.All(chunk.Heights, h => Assert.Equal(0.0f, h, Tolerance));
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

        // Assert - minimum normalized height should be approximately 0
        float minHeight = chunk.Heights.Min();
        Assert.Equal(0.0f, minHeight, Tolerance);
        
        // Max height should be (maxElevation - minElevation)
        float maxHeight = chunk.Heights.Max();
        Assert.Equal(chunk.MaxElevation - chunk.MinElevation, maxHeight, Tolerance);
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
        Assert.Equal(25, chunk5.Heights.Length);   // 5 * 5
        Assert.Equal(100, chunk10.Heights.Length); // 10 * 10
        Assert.Equal(400, chunk20.Heights.Length); // 20 * 20
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
}
