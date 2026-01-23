using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Moq;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

/// <summary>
/// Unit tests for PublicSrtmClient.
/// 
/// Tests the following scenarios:
/// - Successfully fetching a tile by tile name
/// - Successfully fetching a tile by coordinates
/// - Handling 404 errors for missing tiles (TileNotFoundException)
/// - Handling AWS S3 errors (InvalidOperationException)
/// - Invalid input validation
/// 
/// Note: These are unit tests using mocks. Integration tests with real public S3 are
/// marked as [Fact(Skip = "Integration test - requires real AWS S3 access")]
/// </summary>
public class PublicSrtmClientTests
{
    private readonly Mock<IAmazonS3> _mockS3Client;
    private readonly Mock<ILogger<PublicSrtmClient>> _mockLogger;
    private PublicSrtmClient _client;

    public PublicSrtmClientTests()
    {
        _mockS3Client = new Mock<IAmazonS3>();
        _mockLogger = new Mock<ILogger<PublicSrtmClient>>();
        _client = new PublicSrtmClient(_mockS3Client.Object, _mockLogger.Object);
    }

    #region FetchTileAsync Tests

    [Fact]
    public async Task FetchTileAsync_WithValidTileName_ReturnsTileData()
    {
        // Arrange
        var tileName = "N46W113.hgt";
        var tileData = GenerateMockTileData(3601, 3601); // SRTM1 data: 3601x3601 samples

        // Create a real GetObjectResponse-like behavior by using a custom callback
        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => 
                    r.BucketName == "usgs-eros-dem-srtm1" && 
                    r.Key == tileName),
                It.IsAny<CancellationToken>()))
            .Returns((GetObjectRequest req, CancellationToken ct) =>
            {
                var response = new GetObjectResponse 
                { 
                    ResponseStream = new MemoryStream(tileData)
                };
                return Task.FromResult(response);
            });

        // Act
        var result = await _client.FetchTileAsync(tileName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tileData, result);
        _mockS3Client.Verify(
            s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FetchTileAsync_WithTileNotFound_ThrowsTileNotFoundException()
    {
        // Arrange
        var tileName = "N99W999.hgt"; // Non-existent tile

        var s3Exception = new AmazonS3Exception("Not Found")
        {
            StatusCode = System.Net.HttpStatusCode.NotFound,
            ErrorCode = "NoSuchKey"
        };

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TileNotFoundException>(
            () => _client.FetchTileAsync(tileName));

        Assert.Equal(tileName, ex.TileName);
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchTileAsync_WithS3AccessError_ThrowsInvalidOperationException()
    {
        // Arrange
        var tileName = "N46W113.hgt";

        var s3Exception = new AmazonS3Exception("Access Denied")
        {
            StatusCode = System.Net.HttpStatusCode.Forbidden,
            ErrorCode = "AccessDenied"
        };

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _client.FetchTileAsync(tileName));

        Assert.Contains(tileName, ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task FetchTileAsync_WithNullTileName_ThrowsArgumentException()
    {
        // Act & Assert
#pragma warning disable CS8625
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.FetchTileAsync(null));
#pragma warning restore CS8625
    }

    [Fact]
    public async Task FetchTileAsync_WithEmptyTileName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.FetchTileAsync(""));
    }

    [Fact]
    public async Task FetchTileAsync_WithWhitespaceTileName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.FetchTileAsync("   "));
    }

    #endregion

    #region FetchTileByCoordinateAsync Tests

    [Fact]
    public async Task FetchTileByCoordinateAsync_WithValidCoordinates_ComputesTileNameAndFetches()
    {
        // Arrange
        double latitude = 46.5;
        double longitude = -113.2;
        var expectedTileName = "N46W114.hgt"; // Floor values: N46W114

        var tileData = GenerateMockTileData(3601, 3601);

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()))
            .Returns((GetObjectRequest req, CancellationToken ct) =>
            {
                var response = new GetObjectResponse 
                { 
                    ResponseStream = new MemoryStream(tileData)
                };
                return Task.FromResult(response);
            });

        // Act
        var result = await _client.FetchTileByCoordinateAsync(latitude, longitude);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(tileData, result);
        _mockS3Client.Verify(
            s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FetchTileByCoordinateAsync_WithNegativeLatitude_ComputesCorrectTileName()
    {
        // Arrange
        double latitude = -12.1; // Should map to S12 (floor of -12.1 = -13, but abs gives 13)
        double longitude = 44.9; // Should map to E44 (floor of 44.9 = 44)
        var expectedTileName = "S13E044.hgt";

        var tileData = GenerateMockTileData(3601, 3601);

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()))
            .Returns((GetObjectRequest req, CancellationToken ct) =>
            {
                var response = new GetObjectResponse 
                { 
                    ResponseStream = new MemoryStream(tileData)
                };
                return Task.FromResult(response);
            });

        // Act
        var result = await _client.FetchTileByCoordinateAsync(latitude, longitude);

        // Assert
        Assert.NotNull(result);
        _mockS3Client.Verify(
            s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FetchTileByCoordinateAsync_WithEquatorPrimeMeridian_ComputesCorrectTileName()
    {
        // Arrange
        double latitude = 0.1;
        double longitude = 0.1;
        var expectedTileName = "N00E000.hgt";

        var tileData = GenerateMockTileData(3601, 3601);

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()))
            .Returns((GetObjectRequest req, CancellationToken ct) =>
            {
                var response = new GetObjectResponse 
                { 
                    ResponseStream = new MemoryStream(tileData)
                };
                return Task.FromResult(response);
            });

        // Act
        var result = await _client.FetchTileByCoordinateAsync(latitude, longitude);

        // Assert
        Assert.NotNull(result);
        _mockS3Client.Verify(
            s3 => s3.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == expectedTileName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FetchTileByCoordinateAsync_WithTileNotFound_ThrowsTileNotFoundException()
    {
        // Arrange
        double latitude = 45.5;
        double longitude = 179.5;

        var s3Exception = new AmazonS3Exception("Not Found")
        {
            StatusCode = System.Net.HttpStatusCode.NotFound,
            ErrorCode = "NoSuchKey"
        };

        _mockS3Client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act & Assert
        await Assert.ThrowsAsync<TileNotFoundException>(
            () => _client.FetchTileByCoordinateAsync(latitude, longitude));
    }

    #endregion

    #region Integration Tests (marked as Skip by default)

    /// <summary>
    /// Integration test: Actually fetch a tile from the public SRTM bucket.
    /// This requires real AWS S3 access and internet connectivity.
    /// Skip by default - run manually to test against real S3.
    /// 
    /// Known working tile: N46W114.hgt (near Kalispell, Montana)
    /// </summary>
    [Fact(Skip = "Integration test - requires real AWS S3 access")]
    public async Task FetchTileAsync_IntegrationTest_DownloadsRealSrtmTile()
    {
        // Arrange
        var s3Client = new AmazonS3Client();
        var logger = new Mock<ILogger<PublicSrtmClient>>().Object;
        var client = new PublicSrtmClient(s3Client, logger);

        var tileName = "N46W114.hgt";

        // Act
        var result = await client.FetchTileAsync(tileName);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // SRTM1 tiles are 3601x3601 samples of 2 bytes each = 25,934,402 bytes
        // SRTM3 tiles are 1201x1201 samples of 2 bytes each = 2,884,802 bytes
        var sizeInSamples = result.Length / 2;
        var isValidSize = sizeInSamples == (3601 * 3601) || sizeInSamples == (1201 * 1201);
        Assert.True(isValidSize, $"Tile size {result.Length} bytes is not a valid SRTM format");
    }

    /// <summary>
    /// Integration test: Verify that non-existent tiles return 404.
    /// This requires real AWS S3 access.
    /// Skip by default - run manually to test against real S3.
    /// </summary>
    [Fact(Skip = "Integration test - requires real AWS S3 access")]
    public async Task FetchTileAsync_IntegrationTest_InvalidTileThrowsException()
    {
        // Arrange
        var s3Client = new AmazonS3Client();
        var logger = new Mock<ILogger<PublicSrtmClient>>().Object;
        var client = new PublicSrtmClient(s3Client, logger);

        var invalidTileName = "Z99Z999.hgt"; // Non-existent tile

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TileNotFoundException>(
            () => client.FetchTileAsync(invalidTileName));

        Assert.Equal(invalidTileName, ex.TileName);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generate mock SRTM tile data (HGT format).
    /// HGT files contain binary elevation data in big-endian 16-bit signed integers.
    /// For testing, we just generate the right amount of bytes with reasonable values.
    /// </summary>
    private static byte[] GenerateMockTileData(int width, int height)
    {
        var sizeBytes = width * height * 2; // Each elevation is 2 bytes (int16)
        var data = new byte[sizeBytes];

        // Fill with pseudo-realistic elevation data (0-3000m)
        var random = new Random(42); // Fixed seed for reproducibility
        for (int i = 0; i < data.Length; i += 2)
        {
            // Random elevation between 0 and 3000 meters
            int elevation = random.Next(0, 3001);
            
            // Store as big-endian int16
            data[i] = (byte)((elevation >> 8) & 0xFF);
            data[i + 1] = (byte)(elevation & 0xFF);
        }

        return data;
    }

    #endregion
}
