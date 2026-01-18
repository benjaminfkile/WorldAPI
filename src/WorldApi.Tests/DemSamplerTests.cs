using WorldApi.World;

namespace WorldApi.Tests;

public class DemSamplerTests
{
    private const double Tolerance = 0.000001;
    private const short MissingDataValue = -32768;

    private static SrtmTileData CreateTile(double minLat, double minLon, int width, int height, short[] elevations)
    {
        return new SrtmTileData
        {
            MinLatitude = minLat,
            MaxLatitude = minLat + 1.0,
            MinLongitude = minLon,
            MaxLongitude = minLon + 1.0,
            Width = width,
            Height = height,
            Elevations = elevations
        };
    }

    [Fact]
    public void SampleElevation_AtExactGridPoint_ReturnsExactValue()
    {
        // Arrange - simple 2x2 grid
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample at northwest corner (should be elevation[0] = 100)
        double result = DemSampler.SampleElevation(47.0, -113.0, tile);

        // Assert
        Assert.Equal(100.0, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_AtCenter_InterpolatesCorrectly()
    {
        // Arrange - 2x2 grid with known values
        // Layout: 100  200
        //         300  400
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample at center (46.5, -112.5)
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert - should be average of all four corners
        double expected = (100.0 + 200.0 + 300.0 + 400.0) / 4.0;
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_IncreasingLongitude_MovesEast()
    {
        // Arrange - gradient increasing eastward
        // 100  200
        // 100  200
        short[] elevations = { 100, 200, 100, 200 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample at west and east
        double westResult = DemSampler.SampleElevation(46.5, -113.0, tile);
        double eastResult = DemSampler.SampleElevation(46.5, -112.0, tile);

        // Assert - east should have higher elevation
        Assert.Equal(100.0, westResult, Tolerance);
        Assert.Equal(200.0, eastResult, Tolerance);
        Assert.True(eastResult > westResult, "East should have higher elevation");
    }

    [Fact]
    public void SampleElevation_IncreasingLatitude_MovesNorth()
    {
        // Arrange - gradient increasing northward
        // 200  200  (north edge, lat=47)
        // 100  100  (south edge, lat=46)
        short[] elevations = { 200, 200, 100, 100 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample at south and north
        double southResult = DemSampler.SampleElevation(46.0, -112.5, tile);
        double northResult = DemSampler.SampleElevation(47.0, -112.5, tile);

        // Assert - north should have higher elevation
        Assert.Equal(100.0, southResult, Tolerance);
        Assert.Equal(200.0, northResult, Tolerance);
        Assert.True(northResult > southResult, "North should have higher elevation");
    }

    [Fact]
    public void SampleElevation_DecreasingLatitude_MovesSouth()
    {
        // Arrange - gradient decreasing southward
        // 200  200  (north, y=0)
        // 100  100  (south, y=1)
        short[] elevations = { 200, 200, 100, 100 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act
        double northResult = DemSampler.SampleElevation(47.0, -112.5, tile);
        double southResult = DemSampler.SampleElevation(46.0, -112.5, tile);

        // Assert - as latitude decreases (moving south), elevation decreases
        Assert.True(southResult < northResult, "Moving south (decreasing latitude) should decrease elevation");
    }

    [Fact]
    public void SampleElevation_BeyondNorthEdge_ClampsToNorthBoundary()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample beyond north edge
        double result = DemSampler.SampleElevation(48.0, -112.5, tile);

        // Assert - should clamp to north edge and interpolate
        Assert.True(result >= 100.0 && result <= 200.0, "Should clamp to north edge values");
    }

    [Fact]
    public void SampleElevation_BeyondSouthEdge_ClampsToSouthBoundary()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample beyond south edge
        double result = DemSampler.SampleElevation(45.0, -112.5, tile);

        // Assert - should clamp to south edge and interpolate
        Assert.True(result >= 300.0 && result <= 400.0, "Should clamp to south edge values");
    }

    [Fact]
    public void SampleElevation_BeyondWestEdge_ClampsToWestBoundary()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample beyond west edge
        double result = DemSampler.SampleElevation(46.5, -114.0, tile);

        // Assert - should clamp to west edge and interpolate
        Assert.True(result >= 100.0 && result <= 300.0, "Should clamp to west edge values");
    }

    [Fact]
    public void SampleElevation_BeyondEastEdge_ClampsToEastBoundary()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample beyond east edge
        double result = DemSampler.SampleElevation(46.5, -111.0, tile);

        // Assert - should clamp to east edge and interpolate
        Assert.True(result >= 200.0 && result <= 400.0, "Should clamp to east edge values");
    }

    [Fact]
    public void SampleElevation_WithOneMissingSample_ReturnsMissingData()
    {
        // Arrange - one corner has missing data
        short[] elevations = { 100, MissingDataValue, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample at center where all four corners are used
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert
        Assert.Equal(MissingDataValue, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_WithAllMissingSamples_ReturnsMissingData()
    {
        // Arrange - all missing
        short[] elevations = { MissingDataValue, MissingDataValue, MissingDataValue, MissingDataValue };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert
        Assert.Equal(MissingDataValue, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_WithNoMissingData_InterpolatesNormally()
    {
        // Arrange - no missing data
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert - should interpolate normally
        Assert.NotEqual(MissingDataValue, result);
        Assert.Equal(250.0, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_IsDeterministic()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - sample same location multiple times
        double result1 = DemSampler.SampleElevation(46.5, -112.5, tile);
        double result2 = DemSampler.SampleElevation(46.5, -112.5, tile);
        double result3 = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert
        Assert.Equal(result1, result2, Tolerance);
        Assert.Equal(result1, result3, Tolerance);
    }

    [Fact]
    public void SampleElevation_WithLargerGrid_InterpolatesCorrectly()
    {
        // Arrange - 3x3 grid
        short[] elevations = {
            100, 150, 200,  // north row
            250, 300, 350,  // middle row
            400, 450, 500   // south row
        };
        var tile = CreateTile(46.0, -113.0, 3, 3, elevations);

        // Act - sample at exact middle cell
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert - should be value at center cell
        Assert.Equal(300.0, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_AtNorthwestCorner_ReturnsFirstSample()
    {
        // Arrange
        short[] elevations = { 123, 200, 300, 400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - northwest corner is max lat, min lon
        double result = DemSampler.SampleElevation(47.0, -113.0, tile);

        // Assert
        Assert.Equal(123.0, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_AtSoutheastCorner_ReturnsLastSample()
    {
        // Arrange
        short[] elevations = { 100, 200, 300, 456 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act - southeast corner is min lat, max lon
        double result = DemSampler.SampleElevation(46.0, -112.0, tile);

        // Assert
        Assert.Equal(456.0, result, Tolerance);
    }

    [Fact]
    public void SampleElevation_WithNegativeElevations_InterpolatesCorrectly()
    {
        // Arrange - all negative (e.g., below sea level)
        short[] elevations = { -100, -200, -300, -400 };
        var tile = CreateTile(46.0, -113.0, 2, 2, elevations);

        // Act
        double result = DemSampler.SampleElevation(46.5, -112.5, tile);

        // Assert - should be average
        double expected = (-100.0 + -200.0 + -300.0 + -400.0) / 4.0;
        Assert.Equal(expected, result, Tolerance);
    }
}
