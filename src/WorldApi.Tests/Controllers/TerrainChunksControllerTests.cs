using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WorldApi.Controllers;
using WorldApi.Configuration;
using WorldApi.World.Coordinates;
using WorldApi.World.Chunks;
using Amazon.S3.Model;

namespace WorldApi.Tests.Controllers;

public class TerrainChunksControllerTests
{
    private readonly Mock<ITerrainChunkCoordinator> _mockCoordinator;
    private readonly Mock<ITerrainChunkReader> _mockReader;
    private readonly Mock<IWorldVersionService> _mockWorldVersionService;
    private readonly Mock<ILogger<TerrainChunksController>> _mockLogger;
    private readonly Mock<IOptions<WorldAppSecrets>> _mockAppSecrets;
    private readonly TerrainChunksController _controller;

    public TerrainChunksControllerTests()
    {
        _mockCoordinator = new Mock<ITerrainChunkCoordinator>();
        _mockReader = new Mock<ITerrainChunkReader>();
        _mockWorldVersionService = new Mock<IWorldVersionService>();
        _mockLogger = new Mock<ILogger<TerrainChunksController>>();
        _mockAppSecrets = new Mock<IOptions<WorldAppSecrets>>();
        
        // Default: no CloudFront URL (legacy streaming mode)
        _mockAppSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets { CloudfrontUrl = null });
        
        // Mock successful world version lookup
        _mockWorldVersionService
            .Setup(w => w.GetWorldVersionAsync(It.IsAny<string>()))
            .ReturnsAsync(new IWorldVersionService.WorldVersionInfo
            {
                Id = 1,
                Version = "v1",
                IsActive = true
            });
        
        _controller = new TerrainChunksController(
            _mockCoordinator.Object, 
            _mockReader.Object,
            _mockWorldVersionService.Object,
            _mockAppSecrets.Object,
            _mockLogger.Object);
        
        // Set up HttpContext for Response headers
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunk_Returns200WithBinaryData()
    {
        // Arrange
        int chunkX = 10, chunkZ = 20, resolution = 64;
        string worldVersion = "v1";
        
        byte[] mockData = new byte[] { 1, 2, 3, 4, 5 };
        var mockStream = new MemoryStream(mockData);
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = mockStream,
            ETag = "\"abc123\"",
            ContentLength = mockData.Length
        };

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);

        _mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata 
            { 
                ChunkX = chunkX, 
                ChunkZ = chunkZ, 
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "test-key",
                Checksum = "test-checksum",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });

        _mockReader
            .Setup(r => r.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion))
            .ReturnsAsync(mockResponse);

        // Act
        var actionResult = await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        Assert.IsType<EmptyResult>(actionResult);
        Assert.Equal("public, max-age=31536000, immutable", _controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("application/octet-stream", _controller.Response.Headers.ContentType.ToString());
        Assert.Equal("\"abc123\"", _controller.Response.Headers.ETag.ToString());
        Assert.Equal(mockData.Length, _controller.Response.ContentLength);
    }

    [Fact]
    public async Task GetTerrainChunk_PendingChunk_Returns202WithNoStoreCache()
    {
        // Arrange
        int chunkX = 10, chunkZ = 20, resolution = 64;
        string worldVersion = "v1";

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Pending);

        // Act
        var actionResult = await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(actionResult);
        Assert.Equal(202, acceptedResult.StatusCode);
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetTerrainChunk_NotFoundChunk_TriggersGenerationAndReturns202()
    {
        // Arrange
        int chunkX = 10, chunkZ = 20, resolution = 64;
        string worldVersion = "v1";

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.NotFound);

        _mockCoordinator
            .Setup(c => c.TriggerGenerationAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var actionResult = await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(actionResult);
        Assert.Equal(202, acceptedResult.StatusCode);
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());
        
        // Verify generation was triggered
        _mockCoordinator.Verify(
            c => c.TriggerGenerationAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTerrainChunk_PendingChunk_DoesNotTriggerRegeneration()
    {
        // Arrange
        int chunkX = 10, chunkZ = 20, resolution = 64;
        string worldVersion = "v1";

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Pending);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert - verify TriggerGenerationAsync was NOT called
        _mockCoordinator.Verify(
            c => c.TriggerGenerationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunk_SetsContentLengthFromS3()
    {
        // Arrange
        int chunkX = 5, chunkZ = 15, resolution = 32;
        string worldVersion = "v1";
        
        byte[] mockData = new byte[1024];
        var mockStream = new MemoryStream(mockData);
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = mockStream,
            ETag = "\"test\"",
            ContentLength = 1024
        };

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);

        _mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata 
            { 
                ChunkX = chunkX, 
                ChunkZ = chunkZ, 
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "test-key",
                Checksum = "test-checksum",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });

        _mockReader
            .Setup(r => r.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion))
            .ReturnsAsync(mockResponse);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        Assert.Equal(1024, _controller.Response.ContentLength);
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunkWithoutETag_OmitsETagHeader()
    {
        // Arrange
        int chunkX = 1, chunkZ = 2, resolution = 16;
        string worldVersion = "v1";
        
        byte[] mockData = new byte[100];
        var mockStream = new MemoryStream(mockData);
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = mockStream,
            ETag = null, // No ETag
            ContentLength = mockData.Length
        };

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);
        _mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata 
            { 
                ChunkX = chunkX, 
                ChunkZ = chunkZ, 
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "test-key",
                Checksum = "test-checksum",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });
        _mockReader
            .Setup(r => r.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion))
            .ReturnsAsync(mockResponse);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        Assert.False(_controller.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunk_SetsImmutableCacheControl()
    {
        // Arrange
        int chunkX = 0, chunkZ = 0, resolution = 8;
        string worldVersion = "v2";
        
        byte[] mockData = new byte[10];
        var mockStream = new MemoryStream(mockData);
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = mockStream,
            ETag = "\"xyz\"",
            ContentLength = mockData.Length
        };

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);

        _mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata 
            { 
                ChunkX = chunkX, 
                ChunkZ = chunkZ, 
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "test-key",
                Checksum = "test-checksum",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });

        _mockReader
            .Setup(r => r.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion))
            .ReturnsAsync(mockResponse);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        var cacheControl = _controller.Response.Headers.CacheControl.ToString();
        Assert.Contains("public", cacheControl);
        Assert.Contains("max-age=31536000", cacheControl);
        Assert.Contains("immutable", cacheControl);
    }

    [Fact]
    public async Task GetTerrainChunk_NotFoundChunk_SetsNoStoreCacheControl()
    {
        // Arrange
        int chunkX = 99, chunkZ = 88, resolution = 128;
        string worldVersion = "v1";

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.NotFound);

        _mockCoordinator
            .Setup(c => c.TriggerGenerationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunk_SetsCorrectContentType()
    {
        // Arrange
        int chunkX = 3, chunkZ = 7, resolution = 64;
        string worldVersion = "v1";
        
        byte[] mockData = new byte[50];
        var mockStream = new MemoryStream(mockData);
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = mockStream,
            ETag = "\"tag\"",
            ContentLength = mockData.Length
        };

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);

        _mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata 
            { 
                ChunkX = chunkX, 
                ChunkZ = chunkZ, 
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "test-key",
                Checksum = "test-checksum",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });

        _mockReader
            .Setup(r => r.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion))
            .ReturnsAsync(mockResponse);

        // Act
        await _controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        Assert.Equal("application/octet-stream", _controller.Response.Headers.ContentType.ToString());
    }

    [Fact]
    public async Task GetTerrainChunk_DifferentWorldVersions_CallsCoordinatorWithCorrectVersion()
    {
        // Arrange
        int chunkX = 1, chunkZ = 1, resolution = 32;
        string worldVersion1 = "v1";
        string worldVersion2 = "v2";

        _mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.NotFound);

        _mockCoordinator
            .Setup(c => c.TriggerGenerationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _controller.GetTerrainChunk(worldVersion1, resolution, chunkX, chunkZ, CancellationToken.None);
        await _controller.GetTerrainChunk(worldVersion2, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        _mockCoordinator.Verify(
            c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion1, It.IsAny<string>()),
            Times.Once);
        
        _mockCoordinator.Verify(
            c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion2, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTerrainChunk_ReadyChunk_WithCloudfrontFlagTrue_RedirectsToCloudfront()
    {
        // Arrange
        var mockCoordinator = new Mock<ITerrainChunkCoordinator>();
        var mockReader = new Mock<ITerrainChunkReader>();
        var mockLogger = new Mock<ILogger<TerrainChunksController>>();
        var mockAppSecrets = new Mock<IOptions<WorldAppSecrets>>();

        mockAppSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            CloudfrontUrl = "https://d123.cloudfront.net",
            UseCloudfront = "true"
        });

        int chunkX = 2, chunkZ = 3, resolution = 64;
        string worldVersion = "v1";

        mockCoordinator
            .Setup(c => c.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(ChunkStatus.Ready);

        mockCoordinator
            .Setup(c => c.GetChunkMetadataAsync(chunkX, chunkZ, resolution, worldVersion, It.IsAny<string>()))
            .ReturnsAsync(new WorldChunkMetadata
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                Resolution = resolution,
                WorldVersion = worldVersion,
                Layer = "terrain",
                S3Key = "chunks/terrain/64/2/3.bin",
                Checksum = "abc",
                Status = ChunkStatus.Ready,
                GeneratedAt = DateTimeOffset.UtcNow
            });

        var mockWorldVersionService = new Mock<IWorldVersionService>();
        mockWorldVersionService
            .Setup(w => w.GetWorldVersionAsync(It.IsAny<string>()))
            .ReturnsAsync(new IWorldVersionService.WorldVersionInfo
            {
                Id = 1,
                Version = worldVersion,
                IsActive = true
            });

        var controller = new TerrainChunksController(
            mockCoordinator.Object,
            mockReader.Object,
            mockWorldVersionService.Object,
            mockAppSecrets.Object,
            mockLogger.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Act
        var result = await controller.GetTerrainChunk(worldVersion, resolution, chunkX, chunkZ, CancellationToken.None);

        // Assert
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://d123.cloudfront.net/chunks/terrain/64/2/3.bin", redirect.Url);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
    }
}
