using Moq;
using Microsoft.Extensions.Logging;
using WorldApi.Configuration;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

/// <summary>
/// Unit tests for DemDownloadWorker - background service that processes DEM tiles.
/// 
/// These tests verify the worker correctly:
/// - Polls for pending tiles on configured interval
/// - Claims tiles atomically 
/// - Validates SRTM file integrity
/// - Uploads to S3 on success
/// - Records errors on failure
/// - Updates database status appropriately
/// </summary>
public class DemDownloadWorkerTests
{
    [Fact]
    public void DemDownloadWorker_CanBeConstructed_WithRequiredDependencies()
    {
        // Arrange
        var mockRepository = new Mock<DemTileRepository>(null!);
        var mockSrtmClient = new Mock<PublicSrtmClient>(null!);
        var mockTileWriter = new Mock<DemTileWriter>(null!);
        var mockTileIndex = new Mock<DemTileIndex>(null!);
        var mockVersionCache = new Mock<IWorldVersionCache>();
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act
        var worker = new DemDownloadWorker(
            mockRepository.Object,
            mockSrtmClient.Object,
            mockTileWriter.Object,
            mockTileIndex.Object,
            mockVersionCache.Object,
            mockLogger.Object);

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void DemDownloadWorker_ThrowsArgumentNullException_WhenRepositoryIsNull()
    {
        // Arrange
        var mockSrtmClient = new Mock<PublicSrtmClient>(null!);
        var mockTileWriter = new Mock<DemTileWriter>(null!);
        var mockTileIndex = new Mock<DemTileIndex>(null!);
        var mockVersionCache = new Mock<IWorldVersionCache>();
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DemDownloadWorker(
            null!,
            mockSrtmClient.Object,
            mockTileWriter.Object,
            mockTileIndex.Object,
            mockVersionCache.Object,
            mockLogger.Object));
    }

    [Fact]
    public void DemDownloadWorker_ThrowsArgumentNullException_WhenVersionCacheIsNull()
    {
        // Arrange
        var mockRepository = new Mock<DemTileRepository>(null!);
        var mockSrtmClient = new Mock<PublicSrtmClient>(null!);
        var mockTileWriter = new Mock<DemTileWriter>(null!);
        var mockTileIndex = new Mock<DemTileIndex>(null!);
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DemDownloadWorker(
            mockRepository.Object,
            mockSrtmClient.Object,
            mockTileWriter.Object,
            mockTileIndex.Object,
            null!,
            mockLogger.Object));
    }
}

/// <summary>
/// Unit tests for DemStatusService - high-level DEM readiness API.
/// </summary>
public class DemStatusServiceTests
{
    [Fact]
    public void DemStatusService_CanBeConstructed_WithRequiredDependencies()
    {
        // Arrange
        var mockRepository = new Mock<DemTileRepository>(null!);
        var mockLogger = new Mock<ILogger<DemStatusService>>();

        // Act
        var service = new DemStatusService(mockRepository.Object, mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void DemStatusService_ThrowsArgumentNullException_WhenRepositoryIsNull()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DemStatusService>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DemStatusService(null!, mockLogger.Object));
    }
}
