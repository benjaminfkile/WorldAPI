using WorldApi.World.Dem;

namespace WorldApi.Tests;

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
}
