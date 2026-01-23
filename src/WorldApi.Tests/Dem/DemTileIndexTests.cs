using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class DemTileIndexTests
{
    private const double Tolerance = 0.000001;

    private static DemTile CreateTile(double minLat, double minLon, string s3Key)
    {
        return new DemTile
        {
            MinLatitude = minLat,
            MaxLatitude = minLat + 1.0,
            MinLongitude = minLon,
            MaxLongitude = minLon + 1.0,
            S3Key = s3Key
        };
    }

    [Fact]
    public void FindTileContaining_LatLonInsideTile_ReturnsCorrectTile()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(tile);

        // Act - point in the middle of the tile
        var result = index.FindTileContaining(46.5, -112.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tile.S3Key, result.S3Key);
        Assert.Equal(46.0, result.MinLatitude, Tolerance);
        Assert.Equal(47.0, result.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, result.MinLongitude, Tolerance);
        Assert.Equal(-112.0, result.MaxLongitude, Tolerance);
    }

    [Fact]
    public void FindTileContaining_LatLonAtMinBoundary_ReturnsTile()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(tile);

        // Act - point exactly at the minimum boundary
        var result = index.FindTileContaining(46.0, -113.0);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tile.S3Key, result.S3Key);
    }

    [Fact]
    public void FindTileContaining_LatLonAtMaxBoundary_DoesNotReturnTile()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(tile);

        // Act - point exactly at the maximum boundary (exclusive)
        var resultLat = index.FindTileContaining(47.0, -112.5);
        var resultLon = index.FindTileContaining(46.5, -112.0);

        // Assert - max boundaries are exclusive
        Assert.Null(resultLat);
        Assert.Null(resultLon);
    }

    [Fact]
    public void FindTileContaining_LatLonOutsideAllTiles_ReturnsNull()
    {
        // Arrange
        var index = new DemTileIndex();
        index.Add(CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt"));
        index.Add(CreateTile(45.0, -113.0, "dem/srtm/N45W113.hgt"));

        // Act - point far outside any tile
        var result = index.FindTileContaining(50.0, -100.0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindTileContaining_EmptyIndex_ReturnsNull()
    {
        // Arrange
        var index = new DemTileIndex();

        // Act
        var result = index.FindTileContaining(46.0, -113.0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindTileContaining_IsDeterministic()
    {
        // Arrange
        var index = new DemTileIndex();
        index.Add(CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt"));
        index.Add(CreateTile(47.0, -113.0, "dem/srtm/N47W113.hgt"));

        // Act - query same coordinates multiple times
        var result1 = index.FindTileContaining(46.5, -112.5);
        var result2 = index.FindTileContaining(46.5, -112.5);
        var result3 = index.FindTileContaining(46.5, -112.5);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Equal(result1.S3Key, result2.S3Key);
        Assert.Equal(result1.S3Key, result3.S3Key);
    }

    [Fact]
    public void FindTileContaining_MultipleAdjacentTiles_ReturnsCorrectTile()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile1 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        var tile2 = CreateTile(46.0, -112.0, "dem/srtm/N46W112.hgt");
        var tile3 = CreateTile(47.0, -113.0, "dem/srtm/N47W113.hgt");
        index.Add(tile1);
        index.Add(tile2);
        index.Add(tile3);

        // Act
        var result1 = index.FindTileContaining(46.5, -112.5); // tile1
        var result2 = index.FindTileContaining(46.5, -111.5); // tile2
        var result3 = index.FindTileContaining(47.5, -112.5); // tile3

        // Assert
        Assert.NotNull(result1);
        Assert.Equal(tile1.S3Key, result1.S3Key);
        Assert.NotNull(result2);
        Assert.Equal(tile2.S3Key, result2.S3Key);
        Assert.NotNull(result3);
        Assert.Equal(tile3.S3Key, result3.S3Key);
    }

    [Fact]
    public void Add_NewTile_IncreasesCount()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");

        // Act
        index.Add(tile);

        // Assert
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void Add_DuplicateS3Key_ReplacesExistingTile()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile1 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        var tile2 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");

        // Act
        index.Add(tile1);
        index.Add(tile2);

        // Assert - should only have one tile
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void GetAllTiles_EmptyIndex_ReturnsEmptyCollection()
    {
        // Arrange
        var index = new DemTileIndex();

        // Act
        var tiles = index.GetAllTiles();

        // Assert
        Assert.Empty(tiles);
    }

    [Fact]
    public void GetAllTiles_WithTiles_ReturnsAllTiles()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile1 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        var tile2 = CreateTile(47.0, -113.0, "dem/srtm/N47W113.hgt");
        var tile3 = CreateTile(46.0, -112.0, "dem/srtm/N46W112.hgt");
        index.Add(tile1);
        index.Add(tile2);
        index.Add(tile3);

        // Act
        var tiles = index.GetAllTiles();

        // Assert
        Assert.Equal(3, tiles.Count);
        Assert.Contains(tiles, t => t.S3Key == tile1.S3Key);
        Assert.Contains(tiles, t => t.S3Key == tile2.S3Key);
        Assert.Contains(tiles, t => t.S3Key == tile3.S3Key);
    }

    [Fact]
    public void FindTileContaining_NegativeLatitude_WorksCorrectly()
    {
        // Arrange - southern hemisphere
        var index = new DemTileIndex();
        var tile = CreateTile(-46.0, 10.0, "dem/srtm/S46E010.hgt");
        index.Add(tile);

        // Act
        var result = index.FindTileContaining(-45.5, 10.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tile.S3Key, result.S3Key);
    }

    [Fact]
    public void FindTileContaining_NegativeLongitude_WorksCorrectly()
    {
        // Arrange - western hemisphere
        var index = new DemTileIndex();
        var tile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(tile);

        // Act
        var result = index.FindTileContaining(46.5, -112.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tile.S3Key, result.S3Key);
    }

    [Fact]
    public void FindTileContaining_EquatorAndPrimeMeridian_WorksCorrectly()
    {
        // Arrange - tile at 0,0
        var index = new DemTileIndex();
        var tile = CreateTile(0.0, 0.0, "dem/srtm/N00E000.hgt");
        index.Add(tile);

        // Act
        var result = index.FindTileContaining(0.5, 0.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tile.S3Key, result.S3Key);
    }

    // Thread-safety and runtime mutation tests (Step 5)

    [Fact]
    public void Add_ConcurrentAdds_ThreadSafe()
    {
        // Arrange
        var index = new DemTileIndex();
        const int threadCount = 10;
        const int tilesPerThread = 100;
        var tasks = new List<Task>();

        // Act - multiple threads adding tiles concurrently
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            var task = Task.Run(() =>
            {
                for (int i = 0; i < tilesPerThread; i++)
                {
                    var tile = CreateTile(
                        threadId * 10.0 + i,
                        threadId * 10.0 + i,
                        $"dem/srtm/tile_{threadId}_{i}.hgt");
                    index.Add(tile);
                }
            });
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - all tiles should be added
        Assert.Equal(threadCount * tilesPerThread, index.Count);
    }

    [Fact]
    public void Add_IdempotentAdd_DoesNotIncreaseCount()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile1 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        var tile2 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");

        // Act - add same tile twice (idempotent)
        index.Add(tile1);
        int countAfterFirst = index.Count;
        index.Add(tile2);
        int countAfterSecond = index.Count;

        // Assert - count should remain 1
        Assert.Equal(1, countAfterFirst);
        Assert.Equal(1, countAfterSecond);
    }

    [Fact]
    public void FindTileContaining_AfterRuntimeAdd_FindsNewTile()
    {
        // Arrange
        var index = new DemTileIndex();
        
        // Act - simulate runtime lazy fetch scenario
        var tileBefore = index.FindTileContaining(46.5, -112.5);
        
        // Add tile at runtime (lazy fetch)
        var newTile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(newTile);
        
        var tileAfter = index.FindTileContaining(46.5, -112.5);

        // Assert
        Assert.Null(tileBefore); // Not found before runtime add
        Assert.NotNull(tileAfter); // Found after runtime add
        Assert.Equal(newTile.S3Key, tileAfter.S3Key);
    }

    [Fact]
    public void Count_AfterRuntimeAdd_Increases()
    {
        // Arrange
        var index = new DemTileIndex();
        var tile1 = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(tile1);
        int countBefore = index.Count;

        // Act - runtime add
        var tile2 = CreateTile(47.0, -113.0, "dem/srtm/N47W113.hgt");
        index.Add(tile2);
        int countAfter = index.Count;

        // Assert
        Assert.Equal(1, countBefore);
        Assert.Equal(2, countAfter);
    }

    [Fact]
    public void GetAllTiles_ConcurrentReadsAndWrites_ThreadSafe()
    {
        // Arrange
        var index = new DemTileIndex();
        const int writerCount = 5;
        const int readerCount = 5;
        const int operations = 100;
        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        // Act - concurrent readers and writers
        // Writers
        for (int w = 0; w < writerCount; w++)
        {
            int writerId = w;
            var task = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < operations; i++)
                    {
                        var tile = CreateTile(
                            writerId * 10.0 + i,
                            writerId * 10.0 + i,
                            $"dem/srtm/tile_{writerId}_{i}.hgt");
                        index.Add(tile);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });
            tasks.Add(task);
        }

        // Readers
        for (int r = 0; r < readerCount; r++)
        {
            var task = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < operations; i++)
                    {
                        var tiles = index.GetAllTiles();
                        int count = index.Count;
                        // Just accessing to test thread safety
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - no exceptions should occur
        Assert.Empty(exceptions);
        Assert.Equal(writerCount * operations, index.Count);
    }

    [Fact]
    public void FindTileContaining_ConcurrentSearches_ThreadSafe()
    {
        // Arrange
        var index = new DemTileIndex();
        for (int i = 0; i < 100; i++)
        {
            var tile = CreateTile(i * 1.0, i * 1.0, $"dem/srtm/tile_{i}.hgt");
            index.Add(tile);
        }

        const int threadCount = 10;
        const int searchesPerThread = 100;
        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        // Act - concurrent searches
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            var task = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < searchesPerThread; i++)
                    {
                        double lat = (threadId * 10.0 + i) % 100 + 0.5;
                        double lon = (threadId * 10.0 + i) % 100 + 0.5;
                        var result = index.FindTileContaining(lat, lon);
                        // Just searching to test thread safety
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            });
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - no exceptions should occur
        Assert.Empty(exceptions);
    }
}

