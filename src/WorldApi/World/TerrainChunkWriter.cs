using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace WorldApi.World;

public readonly record struct ChunkUploadResult(string S3Key, string Checksum);

public sealed class TerrainChunkWriter
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _worldVersion;

    public TerrainChunkWriter(IAmazonS3 s3Client, string bucketName, IOptions<WorldConfig> config)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
        _worldVersion = config.Value.Version;
    }

    public async Task<ChunkUploadResult> WriteAsync(TerrainChunk chunk)
    {
        // Build S3 key
        string s3Key = BuildS3Key(chunk.ChunkX, chunk.ChunkZ, chunk.Resolution);

        // Check if object already exists
        bool exists = await ObjectExistsAsync(s3Key);
        if (exists)
        {
            // Object already exists, return existing info
            var headResponse = await _s3Client.GetObjectMetadataAsync(_bucketName, s3Key);
            return new ChunkUploadResult(s3Key, headResponse.ETag);
        }

        // Serialize chunk to binary format
        byte[] data = TerrainChunkSerializer.Serialize(chunk);

        // Upload to S3
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            InputStream = new MemoryStream(data),
            ContentType = "application/octet-stream"
        };
        
        // Set Cache-Control header
        request.Headers.CacheControl = "public, max-age=31536000, immutable";

        var response = await _s3Client.PutObjectAsync(request);

        return new ChunkUploadResult(s3Key, response.ETag);
    }

    private string BuildS3Key(int chunkX, int chunkZ, int resolution)
    {
        return $"chunks/{_worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
    }

    private async Task<bool> ObjectExistsAsync(string s3Key)
    {
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
