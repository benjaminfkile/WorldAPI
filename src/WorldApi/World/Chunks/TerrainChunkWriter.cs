using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using WorldApi.World.Config;

namespace WorldApi.World.Chunks;

public readonly record struct ChunkUploadResult(string S3Key, string Checksum);

public sealed class TerrainChunkWriter
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _worldVersion;
    private readonly ILogger<TerrainChunkWriter> _logger;

    public TerrainChunkWriter(IAmazonS3 s3Client, string bucketName, IOptions<WorldConfig> config, ILogger<TerrainChunkWriter> logger)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
        _worldVersion = config.Value.Version;
        _logger = logger;
    }

    public async Task<ChunkUploadResult> WriteAsync(TerrainChunk chunk)
    {
        // Build S3 key
        string s3Key = BuildS3Key(chunk.ChunkX, chunk.ChunkZ, chunk.Resolution);

        // _logger.LogInformation(
        //     "[TRACE] WriteAsync entry: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, HeightsLength={HeightsLength}",
        //     chunk.ChunkX, chunk.ChunkZ, chunk.Resolution, chunk.Heights.Length);

        // Check if object already exists
        bool exists = await ObjectExistsAsync(s3Key);
        if (exists)
        {
            // Object already exists, return existing info
            var headResponse = await _s3Client.GetObjectMetadataAsync(_bucketName, s3Key);
            // _logger.LogInformation(
            //     "[TRACE] Object already exists in S3: S3Key={S3Key}, ETag={ETag}",
            //     s3Key, headResponse.ETag);
            return new ChunkUploadResult(s3Key, headResponse.ETag);
        }

        // Serialize chunk to binary format
        byte[] data = TerrainChunkSerializer.Serialize(chunk);

        // _logger.LogInformation(
        //     "[TRACE] Serialized chunk: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, PayloadBytes={PayloadBytes}, HeightsLength={HeightsLength}",
        //     chunk.ChunkX, chunk.ChunkZ, chunk.Resolution, data.Length, chunk.Heights.Length);

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

        // _logger.LogInformation(
        //     "[TRACE] Upload complete: S3Key={S3Key}, ETag={ETag}, ContentLength={ContentLength}",
        //     s3Key, response.ETag, data.Length);

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
