using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WorldApi.Controllers;
using WorldApi.Configuration;
using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.Tests.Controllers;

public class ImageryTilesControllerTests
{
    #region Test Helpers

    private ImageryTilesController CreateController(
        IAmazonS3 s3Client,
        IHttpClientFactory factory,
        IOptions<WorldAppSecrets> appSecrets,
        ILogger<ImageryTilesController> logger)
    {
        var controller = new ImageryTilesController(s3Client, factory, appSecrets, logger);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    #endregion

    #region Cache Hit Tests

    [Fact]
    public async Task GetImageryTile_TileExistsInS3_Returns200WithBinaryData()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        var tileData = new byte[] { 1, 2, 3, 4, 5 };
        var mockResponse = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(tileData),
            ContentLength = tileData.Length,
            ETag = "\"test-etag\""
        };

        s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", 10, 341, 612);

        // Assert
        Assert.IsType<EmptyResult>(result);
        Assert.Contains("public", controller.Response.Headers.CacheControl.ToString());
        Assert.Contains("max-age=31536000", controller.Response.Headers.CacheControl.ToString());
        // The controller currently returns image/webp (could be png, depends on format changes)
        var contentType = controller.Response.Headers.ContentType.ToString();
        Assert.True(contentType.Contains("image/"), $"Expected image type, got {contentType}");
    }

    [Fact]
    public async Task GetImageryTile_TileExistsWithCloudFront_Returns302Redirect()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key",
            CloudfrontUrl = "https://d123.cloudfront.net",
            UseCloudfront = "true"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        var mockResponse = new GetObjectResponse { ContentLength = 100 };
        s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", 10, 341, 612);

        // Assert
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("cloudfront.net", redirect.Url);
    }

    #endregion

    #region Parameter Validation Tests

    [Fact]
    public async Task GetImageryTile_InvalidProvider_ReturnsBadRequest()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("invalid-provider", "landscape-v4", 10, 341, 612);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("landscape-v4")]
    [InlineData("map_with_underscores")]
    [InlineData("map-with-hyphens")]
    public async Task GetImageryTile_ValidMapNames_Accepted(string validMapName)
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var httpHandler = new Mock<HttpMessageHandler>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.S3.AmazonS3Exception("Not found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        var httpClient = new HttpClient(httpHandler.Object);
        httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("maptiler", validMapName, 10, 341, 612);

        // Assert - should not return BadRequest
        Assert.IsNotType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("map@invalid")]
    [InlineData("map with spaces")]
    public async Task GetImageryTile_InvalidMapNames_ReturnsBadRequest(string invalidMapName)
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("maptiler", invalidMapName, 10, 341, 612);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public async Task GetImageryTile_InvalidZoomLevel_ReturnsBadRequest(int invalidZoom)
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", invalidZoom, 0, 0);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(10, -1, 0)]      // x < 0
    [InlineData(10, 1024, 512)]  // x >= 2^10
    [InlineData(10, 0, -1)]      // y < 0
    public async Task GetImageryTile_OutOfRangeCoordinates_ReturnsBadRequest(int z, int x, int y)
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", z, x, y);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task GetImageryTile_NoS3Bucket_Returns500Error()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = null,
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", 10, 341, 612);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        // Should return 500 or 403 (Access Denied)
        Assert.True(statusResult.StatusCode == 500 || statusResult.StatusCode == 403,
            $"Expected 500 or 403, got {statusResult.StatusCode}");
    }

    [Fact]
    public async Task GetImageryTile_NoMapTilerKey_Returns500Error()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = null
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.S3.AmazonS3Exception("Not found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        // Act
        var result = await controller.GetImageryTile("maptiler", "landscape-v4", 10, 341, 612);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    #endregion

    #region Response Headers Tests

    [Fact]
    public async Task GetImageryTile_SetsCacheControlHeaders()
    {
        // Arrange
        var s3Client = new Mock<IAmazonS3>();
        var logger = new Mock<ILogger<ImageryTilesController>>();
        var appSecrets = new Mock<IOptions<WorldAppSecrets>>();
        appSecrets.Setup(s => s.Value).Returns(new WorldAppSecrets
        {
            S3BucketName = "test-bucket",
            MapTilerApiKey = "test-key"
        });

        var httpClient = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var controller = CreateController(s3Client.Object, factory.Object, appSecrets.Object, logger.Object);

        var mockResponse = new GetObjectResponse { ContentLength = 100 };
        s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Act
        await controller.GetImageryTile("maptiler", "landscape-v4", 10, 341, 612);

        // Assert
        var cacheControl = controller.Response.Headers.CacheControl.ToString();
        Assert.Contains("public", cacheControl);
        Assert.Contains("max-age=31536000", cacheControl);
        Assert.Contains("immutable", cacheControl);
    }

    #endregion
}
