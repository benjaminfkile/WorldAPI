using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class SrtmTileNameCalculatorTests
{
    [Theory]
    [InlineData(46.5, -113.2, "N46W114")]  // Design doc said W113, but floor(-113.2) = -114
    [InlineData(-12.1, 44.9, "S13E044")]
    [InlineData(0.1, 0.1, "N00E000")]
    public void Calculate_DesignDocumentExamples_ReturnsExpectedTileName(
        double latitude, double longitude, string expectedTileName)
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal(expectedTileName, result);
    }

    [Theory]
    [InlineData(46.0, -113.0, "N46W113")]  // Exact corner
    [InlineData(46.999, -113.999, "N46W114")]  // Near next tile
    [InlineData(47.0, -113.0, "N47W113")]  // Next latitude tile
    public void Calculate_EdgeCases_ReturnsCorrectTile(
        double latitude, double longitude, string expectedTileName)
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal(expectedTileName, result);
    }

    [Theory]
    [InlineData(0.0, 0.0, "N00E000")]  // Equator and Prime Meridian
    [InlineData(-0.1, -0.1, "S01W001")]  // Just south and west of origin
    [InlineData(89.9, 179.9, "N89E179")]  // Near north pole
    [InlineData(-89.9, -179.9, "S90W180")]  // Near south pole
    public void Calculate_BoundaryConditions_ReturnsCorrectTile(
        double latitude, double longitude, string expectedTileName)
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal(expectedTileName, result);
    }

    [Theory]
    [InlineData(27.5, 86.5, "N27E086")]  // Known-good tile from design doc
    [InlineData(45.0, -122.0, "N45W122")]  // Portland, OR area
    [InlineData(-33.9, 151.2, "S34E151")]  // Sydney, Australia area
    [InlineData(51.5, -0.1, "N51W001")]  // London, UK area
    public void Calculate_RealWorldLocations_ReturnsExpectedTile(
        double latitude, double longitude, string expectedTileName)
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal(expectedTileName, result);
    }

    [Fact]
    public void Calculate_NorthernHemisphere_UsesPrefixN()
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(45.5, 10.5);

        // Assert
        Assert.StartsWith("N", result);
    }

    [Fact]
    public void Calculate_SouthernHemisphere_UsesPrefixS()
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(-45.5, 10.5);

        // Assert
        Assert.StartsWith("S", result);
    }

    [Fact]
    public void Calculate_EasternHemisphere_ContainsPrefixE()
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(45.5, 10.5);

        // Assert
        Assert.Contains("E", result);
    }

    [Fact]
    public void Calculate_WesternHemisphere_ContainsPrefixW()
    {
        // Act
        string result = SrtmTileNameCalculator.Calculate(45.5, -10.5);

        // Assert
        Assert.Contains("W", result);
    }

    [Fact]
    public void Calculate_LongitudeZeroPadding_PadsToThreeDigits()
    {
        // Arrange: Single-digit longitude
        double latitude = 45.0;
        double longitude = 5.0;

        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal("N45E005", result);
    }

    [Fact]
    public void Calculate_LatitudeZeroPadding_PadsToTwoDigits()
    {
        // Arrange: Single-digit latitude
        double latitude = 5.0;
        double longitude = 10.0;

        // Act
        string result = SrtmTileNameCalculator.Calculate(latitude, longitude);

        // Assert
        Assert.Equal("N05E010", result);
    }
}
