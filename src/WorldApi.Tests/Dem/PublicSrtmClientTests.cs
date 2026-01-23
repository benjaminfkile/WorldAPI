using System.Net;
using WorldApi.World.Dem;

namespace WorldApi.Tests.Dem;

public class PublicSrtmClientTests
{
    [Fact]
    public async Task FetchAndDecompressTileAsync_KnownGoodTile_DownloadsAndDecompressesSuccessfully()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "N27E086"; // Known-good tile from design doc

        // Act
        byte[] result = await client.FetchAndDecompressTileAsync(tileName);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // SRTM tiles are either 1201x1201 (SRTM3) or 3601x3601 (SRTM1), 2 bytes per sample
        // The actual size depends on what's available in the public dataset
        int srtm3Size = 1201 * 1201 * 2;
        int srtm1Size = 3601 * 3601 * 2;
        
        Assert.True(result.Length == srtm3Size || result.Length == srtm1Size,
            $"Expected SRTM3 ({srtm3Size}) or SRTM1 ({srtm1Size}) size, got {result.Length}");
    }

    [Fact]
    public async Task FetchAndDecompressTileAsync_MissingTile_ThrowsTileNotFoundException()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "S91E000"; // Beyond SRTM coverage (south of -60)

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TileNotFoundException>(
            () => client.FetchAndDecompressTileAsync(tileName));

        Assert.Equal(tileName, exception.TileName);
        Assert.Contains("elevation-tiles-prod", exception.Url);
        Assert.Contains(tileName, exception.Message);
    }

    [Fact]
    public async Task FetchAndDecompressTileAsync_ValidTile_BuildsCorrectUrl()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "N27E086";

        // Act
        byte[] result = await client.FetchAndDecompressTileAsync(tileName);

        // Assert - If we got data, the URL was correct
        Assert.NotNull(result);
        // URL format: https://s3.amazonaws.com/elevation-tiles-prod/skadi/N27/N27E086.hgt.gz
    }

    [Fact]
    public async Task FetchAndDecompressTileAsync_SouthernHemisphereTile_BuildsCorrectFolder()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "S34E151"; // Sydney area

        // Act & Assert - May throw TileNotFoundException if tile doesn't exist
        // but if it exists, folder should be "S34"
        try
        {
            byte[] result = await client.FetchAndDecompressTileAsync(tileName);
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }
        catch (TileNotFoundException ex)
        {
            // Expected for ocean regions - verify folder was correctly extracted
            Assert.Contains("S34", ex.Url);
        }
    }

    [Fact]
    public async Task FetchAndDecompressTileAsync_DecompressedData_IsNotGzipped()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "N27E086";

        // Act
        byte[] result = await client.FetchAndDecompressTileAsync(tileName);

        // Assert - Verify it's not still gzipped (gzip magic bytes are 0x1f 0x8b)
        Assert.NotNull(result);
        Assert.True(result.Length > 2);
        bool isGzipped = result[0] == 0x1f && result[1] == 0x8b;
        Assert.False(isGzipped, "Data should be decompressed, not gzipped");
    }

    [Fact]
    public async Task TileNotFoundException_Properties_AreSetCorrectly()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "S91E000"; // Beyond SRTM coverage (south of -60)

        // Act
        var exception = await Assert.ThrowsAsync<TileNotFoundException>(
            () => client.FetchAndDecompressTileAsync(tileName));

        // Assert
        Assert.Equal(tileName, exception.TileName);
        Assert.NotNull(exception.Url);
        Assert.Contains("https://", exception.Url);
        Assert.Contains("elevation-tiles-prod", exception.Url);
        Assert.Contains("skadi", exception.Url);
        Assert.Contains(tileName, exception.Url);
    }

    [Fact]
    public async Task FetchAndDecompressTileAsync_NoAwsCredentials_StillWorks()
    {
        // Arrange - Use a plain HttpClient without any AWS configuration
        using var httpClient = new HttpClient();
        var client = new PublicSrtmClient(httpClient);
        string tileName = "N27E086";

        // Act
        byte[] result = await client.FetchAndDecompressTileAsync(tileName);

        // Assert - Should work without AWS credentials (public bucket)
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}
