using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Dem;

/// <summary>
/// Persists DEM tiles to local S3 storage for caching.
/// Stores uncompressed .hgt files in dem/srtm/ prefix.
/// </summary>
public class DemTileWriter
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public DemTileWriter(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
    }

    /// <summary>
    /// Writes an uncompressed DEM tile to local S3 storage.
    /// </summary>
    /// <param name="tileName">SRTM tile name without extension (e.g., "N27E086")</param>
    /// <param name="tileData">Uncompressed .hgt file content</param>
    /// <returns>S3 key where the tile was stored</returns>
    public virtual async Task<string> WriteTileAsync(string tileName, byte[] tileData)
    {
        string s3Key = $"dem/srtm/{tileName}.hgt";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            InputStream = new MemoryStream(tileData),
            ContentType = "application/octet-stream"
        };

        await _s3Client.PutObjectAsync(request);

        return s3Key;
    }

    /// <summary>
    /// Checks if a tile already exists in local S3 storage.
    /// </summary>
    /// <param name="tileName">SRTM tile name without extension</param>
    /// <returns>True if tile exists, false otherwise</returns>
    public virtual async Task<bool> TileExistsAsync(string tileName)
    {
        string s3Key = $"dem/srtm/{tileName}.hgt";

        try
        {
            await _s3Client.GetObjectMetadataAsync(_bucketName, s3Key);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
