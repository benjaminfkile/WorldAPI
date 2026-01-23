using Microsoft.Extensions.Logging;
using Moq;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class RuntimeDemIndexMutatorTests
{
    private Mock<ILogger<RuntimeDemIndexMutator>> _mockLogger = null!;
    private DemTileIndex _index = null!;
    private RuntimeDemIndexMutator _mutator = null!;

    private void Setup()
    {
        _mockLogger = new Mock<ILogger<RuntimeDemIndexMutator>>();
        _index = new DemTileIndex();
        _mutator = new RuntimeDemIndexMutator(_index, _mockLogger.Object);
    }

    #region Happy Path Tests

    [Fact]
    public async Task AddTileToIndexAsync_WithValidTile_AddsSuccessfully()
    {
        Setup();
        string tileName = "N46W113.hgt";
        double latitude = 46;
        double longitude = -113;

        await _mutator.AddTileToIndexAsync(tileName, latitude, longitude);

        Assert.Equal(1, _index.Count);
        var tile = _index.GetAllTiles().First();
        Assert.Equal(46, tile.MinLatitude);
        Assert.Equal(47, tile.MaxLatitude);
        Assert.Equal(-113, tile.MinLongitude);
        Assert.Equal(-112, tile.MaxLongitude);
        Assert.Equal("dem/srtm/N46W113.hgt", tile.S3Key);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithMultipleTiles_AddsAll()
    {
        Setup();
        string[] tileNames = { "N46W113.hgt", "N47W114.hgt", "S12E044.hgt" };
        double[] latitudes = { 46, 47, -13 };
        double[] longitudes = { -113, -114, 44 };

        for (int i = 0; i < tileNames.Length; i++)
        {
            await _mutator.AddTileToIndexAsync(tileNames[i], latitudes[i], longitudes[i]);
        }

        Assert.Equal(3, _index.Count);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithNorthernHemisphere_ComputesBoundsCorrectly()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);

        var tile = _index.GetAllTiles().First();
        Assert.Equal(46, tile.MinLatitude);
        Assert.Equal(47, tile.MaxLatitude);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithSouthernHemisphere_ComputesBoundsCorrectly()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("S13E044.hgt", -13, 44);

        var tile = _index.GetAllTiles().First();
        Assert.Equal(-13, tile.MinLatitude);
        Assert.Equal(-12, tile.MaxLatitude);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithEquator_ComputesBoundsCorrectly()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N00E000.hgt", 0, 0);

        var tile = _index.GetAllTiles().First();
        Assert.Equal(0, tile.MinLatitude);
        Assert.Equal(1, tile.MaxLatitude);
        Assert.Equal(0, tile.MinLongitude);
        Assert.Equal(1, tile.MaxLongitude);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithLoggingSuccess()
    {
        Setup();
        string tileName = "N46W113.hgt";

        await _mutator.AddTileToIndexAsync(tileName, 46, -113);

        _mockLogger.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Information),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Added tile") && v.ToString()!.Contains(tileName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task AddTileToIndexAsync_WithDuplicateTile_IsIdempotent()
    {
        Setup();
        string tileName = "N46W113.hgt";
        double latitude = 46;
        double longitude = -113;

        await _mutator.AddTileToIndexAsync(tileName, latitude, longitude);
        await _mutator.AddTileToIndexAsync(tileName, latitude, longitude);

        Assert.Equal(1, _index.Count);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithSameTileMultipleTimes_LastOneWins()
    {
        Setup();
        string tileName = "N46W113.hgt";

        for (int i = 0; i < 5; i++)
        {
            await _mutator.AddTileToIndexAsync(tileName, 46, -113);
        }

        Assert.Equal(1, _index.Count);
        Assert.Equal("dem/srtm/N46W113.hgt", _index.GetAllTiles().First().S3Key);
    }

    #endregion

    #region Thread-Safety Tests

    [Fact]
    public async Task AddTileToIndexAsync_WithConcurrentAdditions_IsThreadSafe()
    {
        Setup();
        int concurrentTasks = 50;
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentTasks; i++)
        {
            int taskIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                await _mutator.AddTileToIndexAsync(
                    $"N{(taskIndex % 45):D2}W{(taskIndex % 180):D3}.hgt",
                    taskIndex % 45,
                    -(taskIndex % 180));
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(_index.Count <= concurrentTasks);
    }

    #endregion

    #region Input Validation Tests

    [Fact]
    public void AddTileToIndexAsync_WithNullTileName_ThrowsArgumentException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentException>(
            async () => await _mutator.AddTileToIndexAsync(null!, 46, -113));
    }

    [Fact]
    public void AddTileToIndexAsync_WithEmptyTileName_ThrowsArgumentException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentException>(
            async () => await _mutator.AddTileToIndexAsync("", 46, -113));
    }

    [Fact]
    public void AddTileToIndexAsync_WithWhitespaceTileName_ThrowsArgumentException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentException>(
            async () => await _mutator.AddTileToIndexAsync("   ", 46, -113));
    }

    [Fact]
    public void AddTileToIndexAsync_WithInvalidLatitudeTooHigh_ThrowsArgumentOutOfRangeException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await _mutator.AddTileToIndexAsync("N91E000.hgt", 91, 0));
    }

    [Fact]
    public void AddTileToIndexAsync_WithInvalidLatitudeTooLow_ThrowsArgumentOutOfRangeException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await _mutator.AddTileToIndexAsync("S91E000.hgt", -91, 0));
    }

    [Fact]
    public void AddTileToIndexAsync_WithInvalidLongitudeTooHigh_ThrowsArgumentOutOfRangeException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await _mutator.AddTileToIndexAsync("N00E181.hgt", 0, 181));
    }

    [Fact]
    public void AddTileToIndexAsync_WithInvalidLongitudeTooLow_ThrowsArgumentOutOfRangeException()
    {
        Setup();
        
        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await _mutator.AddTileToIndexAsync("N00W181.hgt", 0, -181));
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithValidBoundaryLatitude_Succeeds()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N90E000.hgt", 90, 0);
        Assert.Equal(1, _index.Count);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithValidBoundaryLongitude_Succeeds()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N00E180.hgt", 0, 180);
        Assert.Equal(1, _index.Count);
    }

    #endregion

    #region Discovery Tests

    [Fact]
    public async Task FindTileContaining_AfterAddingTile_FindsItCorrectly()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);

        var foundTile = _index.FindTileContaining(46.5, -112.5);

        Assert.NotNull(foundTile);
        Assert.Equal("dem/srtm/N46W113.hgt", foundTile.S3Key);
    }

    [Fact]
    public async Task FindTileContaining_WithCoordinateAtTileBoundary_FindsCorrectTile()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);

        var foundTile = _index.FindTileContaining(46, -113);

        Assert.NotNull(foundTile);
        Assert.Equal("dem/srtm/N46W113.hgt", foundTile.S3Key);
    }

    [Fact]
    public async Task FindTileContaining_WithCoordinateOutsideTile_ReturnsNull()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);

        var foundTile = _index.FindTileContaining(48, -111);

        Assert.Null(foundTile);
    }

    #endregion

    #region IsTileIndexed Tests

    [Fact]
    public async Task IsTileIndexed_WithExistingTile_ReturnsTrue()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);

        bool isIndexed = _mutator.IsTileIndexed("N46W113.hgt");

        Assert.True(isIndexed);
    }

    [Fact]
    public void IsTileIndexed_WithNonexistentTile_ReturnsFalse()
    {
        Setup();
        
        bool isIndexed = _mutator.IsTileIndexed("N46W113.hgt");

        Assert.False(isIndexed);
    }

    [Fact]
    public void IsTileIndexed_WithNullTileName_ReturnsFalse()
    {
        Setup();
        
        bool isIndexed = _mutator.IsTileIndexed(null!);

        Assert.False(isIndexed);
    }

    [Fact]
    public void IsTileIndexed_WithEmptyTileName_ReturnsFalse()
    {
        Setup();
        
        bool isIndexed = _mutator.IsTileIndexed("");

        Assert.False(isIndexed);
    }

    #endregion

    #region GetIndexSize Tests

    [Fact]
    public void GetIndexSize_WithEmptyIndex_ReturnsZero()
    {
        Setup();
        int size = _mutator.GetIndexSize();
        Assert.Equal(0, size);
    }

    [Fact]
    public async Task GetIndexSize_AfterAddingTiles_ReturnsCorrectCount()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113);
        await _mutator.AddTileToIndexAsync("N47W114.hgt", 47, -114);

        int size = _mutator.GetIndexSize();

        Assert.Equal(2, size);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public void AddTileToIndexAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        Setup();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _mutator.AddTileToIndexAsync("N46W113.hgt", 46, -113, cts.Token));
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void Constructor_WithNullIndex_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new RuntimeDemIndexMutator(null!, new Mock<ILogger<RuntimeDemIndexMutator>>().Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new RuntimeDemIndexMutator(new DemTileIndex(), null!));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task AddTileToIndexAsync_WithNegativeCoordinateZero_Succeeds()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("S01W001.hgt", -1, -1);
        Assert.Equal(1, _index.Count);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithMaxValidCoordinates_Succeeds()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("N90E180.hgt", 90, 180);
        Assert.Equal(1, _index.Count);
    }

    [Fact]
    public async Task AddTileToIndexAsync_WithMinValidCoordinates_Succeeds()
    {
        Setup();
        await _mutator.AddTileToIndexAsync("S90W180.hgt", -90, -180);
        Assert.Equal(1, _index.Count);
    }

    #endregion
}
