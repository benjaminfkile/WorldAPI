using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class DemTileWriterTests
{
    [Fact]
    public async Task WriteTileAsync_ValidTileData_WritesToBucketWithCorrectKey()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        byte[] tileData = new byte[1201 * 1201 * 2]; // SRTM3 size
        
        PutObjectRequest? capturedRequest = null;
        mockS3Client
            .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PutObjectResponse());

        // Act
        string result = await writer.WriteTileAsync(tileName, tileData);

        // Assert
        Assert.Equal("dem/srtm/N27E086.hgt", result);
        
        mockS3Client.Verify(
            s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default),
            Times.Once);
        
        Assert.NotNull(capturedRequest);
        Assert.Equal(bucketName, capturedRequest.BucketName);
        Assert.Equal("dem/srtm/N27E086.hgt", capturedRequest.Key);
        Assert.Equal("application/octet-stream", capturedRequest.ContentType);
    }

    [Fact]
    public async Task WriteTileAsync_StreamContainsCorrectData()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        byte[] tileData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        
        byte[]? capturedData = null;
        mockS3Client
            .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((req, ct) =>
            {
                using var ms = new MemoryStream();
                req.InputStream.CopyTo(ms);
                capturedData = ms.ToArray();
            })
            .ReturnsAsync(new PutObjectResponse());

        // Act
        await writer.WriteTileAsync(tileName, tileData);

        // Assert
        Assert.NotNull(capturedData);
        Assert.Equal(tileData, capturedData);
    }

    [Fact]
    public async Task WriteTileAsync_StoresUncompressedHgtFile()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        byte[] tileData = new byte[100];
        
        PutObjectRequest? capturedRequest = null;
        mockS3Client
            .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PutObjectResponse());

        // Act
        await writer.WriteTileAsync(tileName, tileData);

        // Assert - Key should end with .hgt, not .hgt.gz
        Assert.NotNull(capturedRequest);
        Assert.EndsWith(".hgt", capturedRequest.Key);
        Assert.DoesNotContain(".gz", capturedRequest.Key);
    }

    [Fact]
    public async Task TileExistsAsync_TileExists_ReturnsTrue()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        
        mockS3Client
            .Setup(s3 => s3.GetObjectMetadataAsync(bucketName, "dem/srtm/N27E086.hgt", default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        // Act
        bool result = await writer.TileExistsAsync(tileName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TileExistsAsync_TileDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        
        mockS3Client
            .Setup(s3 => s3.GetObjectMetadataAsync(bucketName, "dem/srtm/N27E086.hgt", default))
            .ThrowsAsync(new AmazonS3Exception("Not Found") 
            { 
                StatusCode = System.Net.HttpStatusCode.NotFound 
            });

        // Act
        bool result = await writer.TileExistsAsync(tileName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TileExistsAsync_OtherS3Exception_Propagates()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        string tileName = "N27E086";
        
        mockS3Client
            .Setup(s3 => s3.GetObjectMetadataAsync(bucketName, "dem/srtm/N27E086.hgt", default))
            .ThrowsAsync(new AmazonS3Exception("Access Denied") 
            { 
                StatusCode = System.Net.HttpStatusCode.Forbidden 
            });

        // Act & Assert
        await Assert.ThrowsAsync<AmazonS3Exception>(
            () => writer.TileExistsAsync(tileName));
    }

    [Fact]
    public async Task WriteTileAsync_DifferentTileNames_GenerateCorrectKeys()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        string bucketName = "test-bucket";
        var writer = new DemTileWriter(mockS3Client.Object, bucketName);
        
        var testCases = new[]
        {
            ("N27E086", "dem/srtm/N27E086.hgt"),
            ("S34E151", "dem/srtm/S34E151.hgt"),
            ("N00W000", "dem/srtm/N00W000.hgt"),
            ("S89W179", "dem/srtm/S89W179.hgt")
        };

        mockS3Client
            .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        // Act & Assert
        foreach (var (tileName, expectedKey) in testCases)
        {
            byte[] tileData = new byte[100];
            string result = await writer.WriteTileAsync(tileName, tileData);
            Assert.Equal(expectedKey, result);
        }
    }
}
