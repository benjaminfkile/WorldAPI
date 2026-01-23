using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using Microsoft.Extensions.Logging;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

/// <summary>
/// Unit tests for LocalSrtmPersistence service.
/// 
/// Tests cover:
/// - Successful tile saves
/// - Overwrite behavior
/// - Error handling (S3 exceptions)
/// - Input validation
/// - S3 key computation
/// - Logging
/// </summary>
public class LocalSrtmPersistenceTests
{
    private const string TestBucketName = "test-bucket";
    private const string TestTileName = "N46W113.hgt";
    private const string ExpectedS3Key = "dem/srtm3/N46W113.hgt";

    /// <summary>
    /// Helper: Create a LocalSrtmPersistence instance with mocked S3 client and logger.
    /// </summary>
    private (LocalSrtmPersistence service, Mock<IAmazonS3> mockS3, Mock<ILogger<LocalSrtmPersistence>> mockLogger)
        CreateServiceWithMocks()
    {
        var mockS3 = new Mock<IAmazonS3>();
        var mockLogger = new Mock<ILogger<LocalSrtmPersistence>>();
        var service = new LocalSrtmPersistence(mockS3.Object, TestBucketName, mockLogger.Object);
        return (service, mockS3, mockLogger);
    }

    #region Successful Save Tests

    [Fact]
    public async Task SaveTileAsync_WithValidTileData_SavesSuccessfully()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3, 4, 5 };

        var mockResponse = new PutObjectResponse { ETag = "test-etag" };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await service.SaveTileAsync(TestTileName, tileData);

        // Assert
        Assert.Equal(ExpectedS3Key, result);
        mockS3.Verify(s3 => s3.PutObjectAsync(
            It.Is<PutObjectRequest>(req =>
                req.BucketName == TestBucketName &&
                req.Key == ExpectedS3Key &&
                req.ContentType == "application/octet-stream"),
            It.IsAny<CancellationToken>()), Times.Once);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Saving tile")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveTileAsync_WithValidTileData_LogsSuccessWithETag()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[100];
        var eTag = "abc123def456";

        var mockResponse = new PutObjectResponse { ETag = eTag };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        await service.SaveTileAsync(TestTileName, tileData);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("successfully saved")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveTileAsync_WithLargeTileData_SavesSuccessfully()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[25_934_402]; // Typical SRTM 1-arc tile size
        new Random(42).NextBytes(tileData);

        var mockResponse = new PutObjectResponse { ETag = "test-etag" };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await service.SaveTileAsync(TestTileName, tileData);

        // Assert
        Assert.Equal(ExpectedS3Key, result);
        mockS3.Verify(s3 => s3.PutObjectAsync(
            It.Is<PutObjectRequest>(req =>
                req.BucketName == TestBucketName &&
                req.Key == ExpectedS3Key &&
                req.ContentType == "application/octet-stream"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Overwrite Tests

    [Fact]
    public async Task SaveTileAsync_WithExistingTile_OverwritesSuccessfully()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var newTileData = new byte[] { 5, 4, 3, 2, 1 };

        var mockResponse = new PutObjectResponse { ETag = "new-etag" };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await service.SaveTileAsync(TestTileName, newTileData);

        // Assert
        Assert.Equal(ExpectedS3Key, result);
        mockS3.Verify(s3 => s3.PutObjectAsync(
            It.Is<PutObjectRequest>(req =>
                req.BucketName == TestBucketName &&
                req.Key == ExpectedS3Key),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task SaveTileAsync_WithNullTileName_ThrowsArgumentException()
    {
        // Arrange
        var (service, _, _) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveTileAsync(null, tileData));
    }

    [Fact]
    public async Task SaveTileAsync_WithEmptyTileName_ThrowsArgumentException()
    {
        // Arrange
        var (service, _, _) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveTileAsync("", tileData));
    }

    [Fact]
    public async Task SaveTileAsync_WithWhitespaceTileName_ThrowsArgumentException()
    {
        // Arrange
        var (service, _, _) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveTileAsync("   ", tileData));
    }

    [Fact]
    public async Task SaveTileAsync_WithNullTileData_ThrowsArgumentException()
    {
        // Arrange
        var (service, _, _) = CreateServiceWithMocks();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveTileAsync(TestTileName, null));
    }

    [Fact]
    public async Task SaveTileAsync_WithEmptyTileData_ThrowsArgumentException()
    {
        // Arrange
        var (service, _, _) = CreateServiceWithMocks();
        var emptyData = new byte[] { };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveTileAsync(TestTileName, emptyData));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SaveTileAsync_WithS3NotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        var s3Exception = new Amazon.S3.AmazonS3Exception("Access Denied") 
        { 
            StatusCode = System.Net.HttpStatusCode.Forbidden 
        };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveTileAsync(TestTileName, tileData));
    }

    [Fact]
    public async Task SaveTileAsync_WithS3Error_LogsError()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        var s3Exception = new Amazon.S3.AmazonS3Exception("Connection Error") 
        { 
            StatusCode = System.Net.HttpStatusCode.ServiceUnavailable 
        };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act
        try
        {
            await service.SaveTileAsync(TestTileName, tileData);
        }
        catch (InvalidOperationException) { }

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to save tile")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveTileAsync_WithGenericException_ThrowsInvalidOperationException()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveTileAsync(TestTileName, tileData));
    }

    [Fact]
    public async Task SaveTileAsync_WithGenericException_LogsError()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        try
        {
            await service.SaveTileAsync(TestTileName, tileData);
        }
        catch (InvalidOperationException) { }

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task SaveTileAsync_WithCancellationToken_PropagatesCancellation()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SaveTileAsync(TestTileName, tileData, cts.Token));
    }

    [Fact]
    public async Task SaveTileAsync_WithCancellation_LogsWarning()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        try
        {
            await service.SaveTileAsync(TestTileName, tileData, cts.Token);
        }
        catch (OperationCanceledException) { }

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("cancelled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    #endregion

    #region S3 Key Computation Tests

    [Theory]
    [InlineData("N46W113.hgt", "dem/srtm3/N46W113.hgt")]
    [InlineData("S12E044.hgt", "dem/srtm3/S12E044.hgt")]
    [InlineData("N00E000.hgt", "dem/srtm3/N00E000.hgt")]
    public async Task SaveTileAsync_ComputesCorrectS3Key(string tileName, string expectedKey)
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        var mockResponse = new PutObjectResponse { ETag = "test-etag" };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await service.SaveTileAsync(tileName, tileData);

        // Assert
        Assert.Equal(expectedKey, result);
        mockS3.Verify(s3 => s3.PutObjectAsync(
            It.Is<PutObjectRequest>(req => req.Key == expectedKey),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Content Stream Tests

    [Fact]
    public async Task SaveTileAsync_PassesDataAsInputStream()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3, 4, 5 };

        byte[] capturedData = null;
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, ct) =>
            {
                using var ms = new MemoryStream();
                req.InputStream.CopyTo(ms);
                capturedData = ms.ToArray();
            })
            .ReturnsAsync(new PutObjectResponse { ETag = "test-etag" });

        // Act
        await service.SaveTileAsync(TestTileName, tileData);

        // Assert
        Assert.NotNull(capturedData);
        Assert.Equal(tileData, capturedData);
    }

    #endregion

    #region Atomicity Tests

    [Fact]
    public async Task SaveTileAsync_WriteIsAtomic_UsesS3PutObject()
    {
        // Arrange
        var (service, mockS3, mockLogger) = CreateServiceWithMocks();
        var tileData = new byte[] { 1, 2, 3 };

        var mockResponse = new PutObjectResponse { ETag = "test-etag" };
        mockS3.Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        await service.SaveTileAsync(TestTileName, tileData);

        // Assert
        // PutObject is atomic in S3 (single operation, no multipart)
        mockS3.Verify(s3 => s3.PutObjectAsync(
            It.IsAny<PutObjectRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
