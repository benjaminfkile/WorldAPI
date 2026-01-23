using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class SrtmTileNamerTests
{
    [Theory]
    [InlineData(46.5, -113.2, "N46W114.hgt")]
    [InlineData(-12.1, 44.9, "S13E044.hgt")]
    [InlineData(0.1, 0.1, "N00E000.hgt")]
    [InlineData(45.0, -120.0, "N45W120.hgt")]
    [InlineData(-45.0, -120.0, "S45W120.hgt")]
    [InlineData(89.9, 179.9, "N89E179.hgt")]
    [InlineData(-89.9, -179.9, "S90W180.hgt")]
    public void ComputeTileName_WithValidCoordinates_ReturnsCorrectFilename(
        double latitude, double longitude, string expectedFilename)
    {
        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal(expectedFilename, result);
    }

    [Fact]
    public void ComputeTileName_DesignDocExample1_Returns_N46W114_hgt()
    {
        // Arrange: (46.5, -113.2) using floor() convention
        var latitude = 46.5;
        var longitude = -113.2;

        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert: floor(46.5)=46, floor(-113.2)=-114 → N46W114
        Assert.Equal("N46W114.hgt", result);
    }

    [Fact]
    public void ComputeTileName_DesignDocExample2_Returns_S13E044_hgt()
    {
        // Arrange: (-12.1, 44.9)
        var latitude = -12.1;
        var longitude = 44.9;

        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal("S13E044.hgt", result);
    }

    [Fact]
    public void ComputeTileName_DesignDocExample3_Returns_N00E000_hgt()
    {
        // Arrange: (0.1, 0.1)
        var latitude = 0.1;
        var longitude = 0.1;

        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal("N00E000.hgt", result);
    }

    [Fact]
    public void ComputeTileName_EquatorAndPrimeMeridian_UseFloorBehavior()
    {
        // Test floor behavior at exact tile boundaries
        // Lat 0.0 should floor to 0 (N), not -1
        var result = SrtmTileNamer.ComputeTileName(0.0, 0.0);
        Assert.Equal("N00E000.hgt", result);
    }

    [Theory]
    [InlineData(-90.0, 0.0, "S90E000.hgt")]  // South pole
    [InlineData(90.0, 0.0, "N90E000.hgt")]   // North pole (approximately)
    [InlineData(0.0, -180.0, "N00W180.hgt")] // International Date Line
    [InlineData(0.0, 180.0, "N00E180.hgt")]
    public void ComputeTileName_AtExtremeCoordinates_WorksCorrectly(
        double latitude, double longitude, string expectedFilename)
    {
        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal(expectedFilename, result);
    }

    [Fact]
    public void ComputeTileName_NorthernHemisphere_UsesNPrefix()
    {
        var result = SrtmTileNamer.ComputeTileName(45.0, 0.0);
        Assert.StartsWith("N", result);
    }

    [Fact]
    public void ComputeTileName_SouthernHemisphere_UsesSPrefix()
    {
        var result = SrtmTileNamer.ComputeTileName(-45.0, 0.0);
        Assert.StartsWith("S", result);
    }

    [Fact]
    public void ComputeTileName_EasternHemisphere_UsesEPrefix()
    {
        var result = SrtmTileNamer.ComputeTileName(0.0, 90.0);
        Assert.EndsWith("E090.hgt", result);
    }

    [Fact]
    public void ComputeTileName_WesternHemisphere_UsesWPrefix()
    {
        var result = SrtmTileNamer.ComputeTileName(0.0, -90.0);
        Assert.EndsWith("W090.hgt", result);
    }

    [Theory]
    [InlineData(46.0, -113.0, "N46W113.hgt")] // Exactly at tile origin
    [InlineData(46.999, -113.999, "N46W114.hgt")] // Just before next tile
    [InlineData(46.001, -113.001, "N46W114.hgt")] // Just after tile origin
    public void ComputeTileName_WithinSameTile_ReturnsSameName(
        double latitude, double longitude, string expectedFilename)
    {
        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal(expectedFilename, result);
    }

    [Theory]
    [InlineData(46.0, -112.999, "N46W113.hgt")] // Different longitude tile
    [InlineData(47.0, -113.0, "N47W113.hgt")]   // Different latitude tile
    public void ComputeTileName_AcrossTileBoundary_ReturnsDifferentName(
        double latitude, double longitude, string expectedFilename)
    {
        // Act
        var result = SrtmTileNamer.ComputeTileName(latitude, longitude);

        // Assert
        Assert.Equal(expectedFilename, result);
    }

    [Fact]
    public void ComputeTileName_InvalidLatitude_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SrtmTileNamer.ComputeTileName(91.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrtmTileNamer.ComputeTileName(-91.0, 0.0));
    }

    [Fact]
    public void ComputeTileName_InvalidLongitude_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SrtmTileNamer.ComputeTileName(0.0, 181.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrtmTileNamer.ComputeTileName(0.0, -181.0));
    }

    [Fact]
    public void ComputeTileName_AlwaysReturnsHgtExtension()
    {
        var result = SrtmTileNamer.ComputeTileName(45.0, -120.0);
        Assert.EndsWith(".hgt", result);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(45.5, -120.3)]
    [InlineData(-30.0, 150.0)]
    public void ComputeS3Key_ReturnsCorrectS3Path(double latitude, double longitude)
    {
        // Act
        var result = SrtmTileNamer.ComputeS3Key(latitude, longitude);

        // Assert
        Assert.StartsWith("dem/srtm/", result);
        Assert.EndsWith(".hgt", result);
        
        // Should contain only valid filename characters
        var fileName = result.Substring("dem/srtm/".Length);
        var computedTileName = SrtmTileNamer.ComputeTileName(latitude, longitude);
        Assert.Equal(computedTileName, fileName);
    }

    [Fact]
    public void ComputeS3Key_Example_ReturnsCorrectPath()
    {
        // Act
        var result = SrtmTileNamer.ComputeS3Key(46.5, -113.2);

        // Assert
        Assert.Equal("dem/srtm/N46W114.hgt", result);
    }

    [Fact]
    public void ComputeTileName_OutputFormatAlwaysHasNineCharacters()
    {
        // Format: {N|S}{lat:2}{E|W}{lon:3}.hgt = 1+2+1+3+4 = 11 chars
        var result = SrtmTileNamer.ComputeTileName(46.0, -113.0);
        
        // {N|S}{lat:2}{E|W}{lon:3}.hgt
        Assert.Equal("N46W113.hgt", result);
        Assert.Equal(11, result.Length);
    }

    [Theory]
    [InlineData(46.0, -113.0)]
    [InlineData(-46.0, -113.0)]
    [InlineData(46.0, 113.0)]
    [InlineData(-46.0, 113.0)]
    public void ComputeTileName_IsDeterministic(double latitude, double longitude)
    {
        // Call multiple times and verify same result
        var result1 = SrtmTileNamer.ComputeTileName(latitude, longitude);
        var result2 = SrtmTileNamer.ComputeTileName(latitude, longitude);
        var result3 = SrtmTileNamer.ComputeTileName(latitude, longitude);

        Assert.Equal(result1, result2);
        Assert.Equal(result1, result3);
    }
}
