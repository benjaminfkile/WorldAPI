using Microsoft.Extensions.Options;
using WorldApi.World.Config;
using WorldApi.World.Coordinates;

namespace WorldApi.Tests;

public class WorldCoordinateServiceTests
{
    private const double Tolerance = 0.000001;

    private static WorldCoordinateService CreateService(double originLat = 40.0, double originLon = -105.0, int chunkSize = 100)
    {
        var config = new WorldConfig
        {
            Version = "v1",
            Origin = new OriginConfig
            {
                Latitude = originLat,
                Longitude = originLon
            },
            ChunkSizeMeters = chunkSize,
            MetersPerDegreeLatitude = 111320
        };

        return new WorldCoordinateService(Options.Create(config));
    }

    [Fact]
    public void GetChunkOriginLatLon_ChunkZeroZero_ReturnsConfiguredOrigin()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        var service = CreateService(originLat, originLon);

        // Act
        var result = service.GetChunkOriginLatLon(0, 0);

        // Assert
        Assert.Equal(originLat, result.Latitude, Tolerance);
        Assert.Equal(originLon, result.Longitude, Tolerance);
    }

    [Fact]
    public void GetChunkOriginLatLon_PositiveChunkX_IncreasesLongitude()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        var service = CreateService(originLat, originLon, chunkSize: 100);

        // Act
        var origin = service.GetChunkOriginLatLon(0, 0);
        var eastChunk = service.GetChunkOriginLatLon(1, 0);

        // Assert - moving east (+X) increases longitude
        Assert.Equal(origin.Latitude, eastChunk.Latitude, Tolerance);
        Assert.True(eastChunk.Longitude > origin.Longitude, 
            $"Expected longitude to increase when moving east. Origin: {origin.Longitude}, East: {eastChunk.Longitude}");
    }

    [Fact]
    public void GetChunkOriginLatLon_PositiveChunkZ_IncreasesLatitude()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        var service = CreateService(originLat, originLon, chunkSize: 100);

        // Act
        var origin = service.GetChunkOriginLatLon(0, 0);
        var northChunk = service.GetChunkOriginLatLon(0, 1);

        // Assert - moving north (+Z) increases latitude
        Assert.Equal(origin.Longitude, northChunk.Longitude, Tolerance);
        Assert.True(northChunk.Latitude > origin.Latitude, 
            $"Expected latitude to increase when moving north. Origin: {origin.Latitude}, North: {northChunk.Latitude}");
    }

    [Fact]
    public void GetChunkOriginLatLon_LatitudeCalculation_UsesCorrectMetersPerDegree()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        const int chunkSize = 111320; // exactly 1 degree of latitude
        var service = CreateService(originLat, originLon, chunkSize);

        // Act
        var result = service.GetChunkOriginLatLon(0, 1);

        // Assert - 1 chunk north should be exactly 1 degree latitude higher
        Assert.Equal(originLat + 1.0, result.Latitude, Tolerance);
        Assert.Equal(originLon, result.Longitude, Tolerance);
    }

    [Fact]
    public void GetChunkOriginLatLon_LongitudeCalculation_AccountsForLatitude()
    {
        // Arrange - at equator, longitude degrees are wider
        const double equatorLat = 0.0;
        const double equatorLon = 0.0;
        const int chunkSize = 100;
        var equatorService = CreateService(equatorLat, equatorLon, chunkSize);

        // At 60° latitude, longitude degrees are narrower (cos(60°) = 0.5)
        const double highLat = 60.0;
        const double highLon = 0.0;
        var highLatService = CreateService(highLat, highLon, chunkSize);

        // Act
        var equatorResult = equatorService.GetChunkOriginLatLon(1, 0);
        var highLatResult = highLatService.GetChunkOriginLatLon(1, 0);

        // Assert - same chunk distance covers more longitude degrees at higher latitude
        double equatorLonDelta = equatorResult.Longitude - equatorLon;
        double highLatLonDelta = highLatResult.Longitude - highLon;

        Assert.True(highLatLonDelta > equatorLonDelta,
            $"Expected larger longitude delta at higher latitude. Equator: {equatorLonDelta}, High: {highLatLonDelta}");
    }

    [Fact]
    public void GetChunkOriginLatLon_NegativeChunkX_DecreasesLongitude()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        var service = CreateService(originLat, originLon, chunkSize: 100);

        // Act
        var origin = service.GetChunkOriginLatLon(0, 0);
        var westChunk = service.GetChunkOriginLatLon(-1, 0);

        // Assert - moving west (-X) decreases longitude
        Assert.Equal(origin.Latitude, westChunk.Latitude, Tolerance);
        Assert.True(westChunk.Longitude < origin.Longitude,
            $"Expected longitude to decrease when moving west. Origin: {origin.Longitude}, West: {westChunk.Longitude}");
    }

    [Fact]
    public void GetChunkOriginLatLon_NegativeChunkZ_DecreasesLatitude()
    {
        // Arrange
        const double originLat = 40.0;
        const double originLon = -105.0;
        var service = CreateService(originLat, originLon, chunkSize: 100);

        // Act
        var origin = service.GetChunkOriginLatLon(0, 0);
        var southChunk = service.GetChunkOriginLatLon(0, -1);

        // Assert - moving south (-Z) decreases latitude
        Assert.Equal(origin.Longitude, southChunk.Longitude, Tolerance);
        Assert.True(southChunk.Latitude < origin.Latitude,
            $"Expected latitude to decrease when moving south. Origin: {origin.Latitude}, South: {southChunk.Latitude}");
    }

    [Fact]
    public void GetChunkOriginLatLon_IsDeterministic()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result1 = service.GetChunkOriginLatLon(5, 10);
        var result2 = service.GetChunkOriginLatLon(5, 10);

        // Assert
        Assert.Equal(result1.Latitude, result2.Latitude, Tolerance);
        Assert.Equal(result1.Longitude, result2.Longitude, Tolerance);
    }

    [Fact]
    public void GetChunkOriginLatLon_WithLargeChunkSize_ScalesCorrectly()
    {
        // Arrange
        const int smallChunkSize = 100;
        const int largeChunkSize = 1000;
        var smallService = CreateService(chunkSize: smallChunkSize);
        var largeService = CreateService(chunkSize: largeChunkSize);

        // Act
        var smallResult = smallService.GetChunkOriginLatLon(1, 1);
        var largeResult = largeService.GetChunkOriginLatLon(1, 1);

        // Assert - larger chunk size means greater coordinate deltas
        double smallLatDelta = Math.Abs(smallResult.Latitude - 40.0);
        double largeLatDelta = Math.Abs(largeResult.Latitude - 40.0);
        double smallLonDelta = Math.Abs(smallResult.Longitude - (-105.0));
        double largeLonDelta = Math.Abs(largeResult.Longitude - (-105.0));

        Assert.True(largeLatDelta > smallLatDelta);
        Assert.True(largeLonDelta > smallLonDelta);
    }
}
