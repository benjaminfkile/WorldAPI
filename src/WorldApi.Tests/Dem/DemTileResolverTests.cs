using Microsoft.Extensions.Logging;
using Moq;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

/// <summary>
/// Integration tests for DemTileResolver orchestrating the full fetch-persist-index pipeline.
/// </summary>
public class DemTileResolverIntegrationTests
{
    [Fact]
    public void DemTileResolver_Constructor_ValidatesDependencies()
    {
        var index = new DemTileIndex();
        var mockLogger = new Mock<ILogger<DemTileResolver>>().Object;
        var mockPublicClient = new Mock<IPublicSrtmClient>().Object;
        var mockPersistence = new Mock<ILocalSrtmPersistence>().Object;
        var mutator = new RuntimeDemIndexMutator(index, new Mock<ILogger<RuntimeDemIndexMutator>>().Object);

        // Verify null checks
        Assert.Throws<ArgumentNullException>(() =>
            new DemTileResolver(null!, mockPublicClient, mockPersistence, mutator, mockLogger));

        Assert.Throws<ArgumentNullException>(() =>
            new DemTileResolver(index, null!, mockPersistence, mutator, mockLogger));

        Assert.Throws<ArgumentNullException>(() =>
            new DemTileResolver(index, mockPublicClient, null!, mutator, mockLogger));

        Assert.Throws<ArgumentNullException>(() =>
            new DemTileResolver(index, mockPublicClient, mockPersistence, null!, mockLogger));

        Assert.Throws<ArgumentNullException>(() =>
            new DemTileResolver(index, mockPublicClient, mockPersistence, mutator, null!));
    }

    [Fact]
    public void DemTileResolver_Instantiation_Succeeds()
    {
        var index = new DemTileIndex();
        var mockLogger = new Mock<ILogger<DemTileResolver>>().Object;
        var mockPublicClient = new Mock<IPublicSrtmClient>().Object;
        var mockPersistence = new Mock<ILocalSrtmPersistence>().Object;
        var mutator = new RuntimeDemIndexMutator(index, new Mock<ILogger<RuntimeDemIndexMutator>>().Object);

        var resolver = new DemTileResolver(index, mockPublicClient, mockPersistence, mutator, mockLogger);

        Assert.NotNull(resolver);
        Assert.Equal(0, resolver.GetCacheSize());
        Assert.False(resolver.IsTileCached("N46W113.hgt"));
    }

    [Fact]
    public async Task DemTileResolver_InvalidLatitude_Throws()
    {
        var index = new DemTileIndex();
        var mockLogger = new Mock<ILogger<DemTileResolver>>().Object;
        var mockPublicClient = new Mock<IPublicSrtmClient>().Object;
        var mockPersistence = new Mock<ILocalSrtmPersistence>().Object;
        var mutator = new RuntimeDemIndexMutator(index, new Mock<ILogger<RuntimeDemIndexMutator>>().Object);
        var resolver = new DemTileResolver(index, mockPublicClient, mockPersistence, mutator, mockLogger);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => resolver.ResolveTileAsync(91, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => resolver.ResolveTileAsync(-91, 0));
    }

    [Fact]
    public async Task DemTileResolver_InvalidLongitude_Throws()
    {
        var index = new DemTileIndex();
        var mockLogger = new Mock<ILogger<DemTileResolver>>().Object;
        var mockPublicClient = new Mock<IPublicSrtmClient>().Object;
        var mockPersistence = new Mock<ILocalSrtmPersistence>().Object;
        var mutator = new RuntimeDemIndexMutator(index, new Mock<ILogger<RuntimeDemIndexMutator>>().Object);
        var resolver = new DemTileResolver(index, mockPublicClient, mockPersistence, mutator, mockLogger);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => resolver.ResolveTileAsync(0, 181));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => resolver.ResolveTileAsync(0, -181));
    }
}
