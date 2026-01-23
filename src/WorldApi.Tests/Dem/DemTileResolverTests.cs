using Moq;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class DemTileResolverTests
{
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
    public async Task ResolveTileAsync_TileInIndex_DoesNotFetch()
    {
        // Arrange
        var index = new DemTileIndex();
        var existingTile = CreateTile(46.0, -113.0, "dem/srtm/N46W113.hgt");
        index.Add(existingTile);

        var mockPublicClient = new Mock<PublicSrtmClient>(MockBehavior.Strict, new HttpClient());
        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act
        var result = await resolver.ResolveTileAsync(46.5, -112.5);

        // Assert
        Assert.Equal(existingTile.S3Key, result.S3Key);
        
        // Should not fetch or write
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync(It.IsAny<string>()),
            Times.Never);
        mockWriter.Verify(
            w => w.WriteTileAsync(It.IsAny<string>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveTileAsync_TileMissing_FetchesAndStores()
    {
        // Arrange
        var index = new DemTileIndex();
        
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        byte[] fakeTileData = new byte[1201 * 1201 * 2];
        mockPublicClient
            .Setup(c => c.FetchAndDecompressTileAsync("N46W113"))
            .ReturnsAsync(fakeTileData);

        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync("N46W113"))
            .ReturnsAsync(false);
        mockWriter
            .Setup(w => w.WriteTileAsync("N46W113", fakeTileData))
            .ReturnsAsync("dem/srtm/N46W113.hgt");

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act
        var result = await resolver.ResolveTileAsync(46.5, -112.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("dem/srtm/N46W113.hgt", result.S3Key);
        
        // Should have fetched and stored
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync("N46W113"),
            Times.Once);
        mockWriter.Verify(
            w => w.WriteTileAsync("N46W113", fakeTileData),
            Times.Once);
        
        // Should be in index
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task ResolveTileAsync_TileInS3ButNotIndex_AddsToIndex()
    {
        // Arrange
        var index = new DemTileIndex();
        
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync("N46W113"))
            .ReturnsAsync(true); // Exists in S3

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act
        var result = await resolver.ResolveTileAsync(46.5, -112.5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("dem/srtm/N46W113.hgt", result.S3Key);
        
        // Should NOT have fetched
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync(It.IsAny<string>()),
            Times.Never);
        
        // Should be in index now
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task ResolveTileAsync_ConcurrentRequests_OnlyFetchesOnce()
    {
        // Arrange
        var index = new DemTileIndex();
        
        int fetchCount = 0;
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        byte[] fakeTileData = new byte[1201 * 1201 * 2];
        mockPublicClient
            .Setup(c => c.FetchAndDecompressTileAsync("N46W113"))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref fetchCount);
                Thread.Sleep(100); // Simulate network delay
                return fakeTileData;
            });

        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync("N46W113"))
            .ReturnsAsync(false);
        mockWriter
            .Setup(w => w.WriteTileAsync("N46W113", fakeTileData))
            .ReturnsAsync("dem/srtm/N46W113.hgt");

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act - 10 concurrent requests for same tile
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => resolver.ResolveTileAsync(46.5, -112.5))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.Equal("dem/srtm/N46W113.hgt", r.S3Key));
        
        // Should only fetch once
        Assert.Equal(1, fetchCount);
        
        // Should only be in index once
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task ResolveTileAsync_PublicFetchFails_PropagatesException()
    {
        // Arrange
        var index = new DemTileIndex();
        
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        mockPublicClient
            .Setup(c => c.FetchAndDecompressTileAsync("S92E000"))
            .ThrowsAsync(new TileNotFoundException("S92E000", "test-url"));

    var mockWriter = new Mock<DemTileWriter>(MockBehavior.Strict, Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act & Assert
        await Assert.ThrowsAsync<TileNotFoundException>(
            () => resolver.ResolveTileAsync(-91.5, 0.5));
    }

    [Fact]
    public async Task ResolveTileAsync_DifferentTiles_FetchesBoth()
    {
        // Arrange
        var index = new DemTileIndex();
        
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        byte[] fakeTileData = new byte[1201 * 1201 * 2];
        mockPublicClient
            .Setup(c => c.FetchAndDecompressTileAsync(It.IsAny<string>()))
            .ReturnsAsync(fakeTileData);

        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        mockWriter
            .Setup(w => w.WriteTileAsync(It.IsAny<string>(), fakeTileData))
            .ReturnsAsync((string tileName, byte[] data) => $"dem/srtm/{tileName}.hgt");

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act - request two different tiles
        var result1 = await resolver.ResolveTileAsync(46.5, -112.5); // N46W113
        var result2 = await resolver.ResolveTileAsync(47.5, -112.5); // N47W113

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotEqual(result1.S3Key, result2.S3Key);
        
        // Should have fetched both
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync("N46W113"),
            Times.Once);
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync("N47W113"),
            Times.Once);
        
        // Should have both in index
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public async Task ResolveTileAsync_SecondRequest_UsesCachedTile()
    {
        // Arrange
        var index = new DemTileIndex();
        
        var mockPublicClient = new Mock<PublicSrtmClient>(new HttpClient());
        byte[] fakeTileData = new byte[1201 * 1201 * 2];
        mockPublicClient
            .Setup(c => c.FetchAndDecompressTileAsync("N46W113"))
            .ReturnsAsync(fakeTileData);

        var mockWriter = new Mock<DemTileWriter>(Mock.Of<Amazon.S3.IAmazonS3>(), "bucket");
        mockWriter
            .Setup(w => w.TileExistsAsync("N46W113"))
            .ReturnsAsync(false);
        mockWriter
            .Setup(w => w.WriteTileAsync("N46W113", fakeTileData))
            .ReturnsAsync("dem/srtm/N46W113.hgt");

        var resolver = new DemTileResolver(index, mockPublicClient.Object, mockWriter.Object);

        // Act - request same coordinates twice
        var result1 = await resolver.ResolveTileAsync(46.5, -112.5);
        var result2 = await resolver.ResolveTileAsync(46.5, -112.5);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(result1.S3Key, result2.S3Key);
        
        // Should only fetch once
        mockPublicClient.Verify(
            c => c.FetchAndDecompressTileAsync("N46W113"),
            Times.Once);
        mockWriter.Verify(
            w => w.WriteTileAsync("N46W113", fakeTileData),
            Times.Once);
    }
}
