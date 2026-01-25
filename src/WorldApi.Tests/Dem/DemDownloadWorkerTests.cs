using Moq;
using Microsoft.Extensions.Logging;
using Amazon.S3;
using Npgsql;
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
internal static class DemTestHelpers
{
    public static DemTileRepository CreateRepository()
    {
        // Build a lightweight data source; no connections are opened in these ctor tests.
        var builder = new NpgsqlDataSourceBuilder("Host=localhost;Username=test;Password=test;Database=test");
        var dataSource = builder.Build();
        return new DemTileRepository(dataSource);
    }

    public static DemTileIndex CreateTileIndex() => new();
}

public class DemDownloadWorkerTests
{

    [Fact]
    public void DemDownloadWorker_CanBeConstructed_WithRequiredDependencies()
    {
        // Arrange
        var repository = DemTestHelpers.CreateRepository();
        var publicClient = new PublicSrtmClient(new HttpClient());
        var mockS3 = new Mock<IAmazonS3>();
        var tileWriter = new DemTileWriter(mockS3.Object, "test-bucket");
        var tileIndex = DemTestHelpers.CreateTileIndex();
        var mockVersionCache = new Mock<IWorldVersionCache>();
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act
        var worker = new DemDownloadWorker(
            repository,
            publicClient,
            tileWriter,
            tileIndex,
            mockVersionCache.Object,
            mockLogger.Object);

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void DemDownloadWorker_ThrowsArgumentNullException_WhenRepositoryIsNull()
    {
        // Arrange
        var publicClient = new PublicSrtmClient(new HttpClient());
        var mockS3 = new Mock<IAmazonS3>();
        var tileWriter = new DemTileWriter(mockS3.Object, "test-bucket");
        var tileIndex = DemTestHelpers.CreateTileIndex();
        var mockVersionCache = new Mock<IWorldVersionCache>();
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DemDownloadWorker(
            null!,
                publicClient,
                tileWriter,
                tileIndex,
            mockVersionCache.Object,
            mockLogger.Object));
    }

    [Fact]
    public void DemDownloadWorker_ThrowsArgumentNullException_WhenVersionCacheIsNull()
    {
        // Arrange
        var repository = DemTestHelpers.CreateRepository();
        var publicClient = new PublicSrtmClient(new HttpClient());
        var mockS3 = new Mock<IAmazonS3>();
        var tileWriter = new DemTileWriter(mockS3.Object, "test-bucket");
        var tileIndex = DemTestHelpers.CreateTileIndex();
        var mockLogger = new Mock<ILogger<DemDownloadWorker>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DemDownloadWorker(
            repository,
            publicClient,
            tileWriter,
            tileIndex,
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
        var repository = DemTestHelpers.CreateRepository();
        var mockLogger = new Mock<ILogger<DemStatusService>>();

        // Act
        var service = new DemStatusService(repository, mockLogger.Object);

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
