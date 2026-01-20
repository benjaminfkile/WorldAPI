using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Chunks;

public sealed class TerrainChunkReader : ITerrainChunkReader
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public TerrainChunkReader(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
    }

    public async Task<TerrainChunk> ReadAsync(int chunkX, int chunkZ, int resolution, string worldVersion)
    {
        // Construct S3 key using same convention as writer
        string s3Key = BuildS3Key(chunkX, chunkZ, resolution, worldVersion);

        // Download object bytes from S3
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        using var response = await _s3Client.GetObjectAsync(request);
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        byte[] data = memoryStream.ToArray();

        // Deserialize into TerrainChunk
        return TerrainChunkSerializer.Deserialize(data, chunkX, chunkZ);
    }

    /// <summary>
    /// Gets S3 object response for streaming directly to HTTP response.
    /// Caller is responsible for disposing the response.
    /// </summary>
    public async Task<GetObjectResponse> GetStreamAsync(int chunkX, int chunkZ, int resolution, string worldVersion)
    {
        string s3Key = BuildS3Key(chunkX, chunkZ, resolution, worldVersion);
        
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        return await _s3Client.GetObjectAsync(request);
    }

    private string BuildS3Key(int chunkX, int chunkZ, int resolution, string worldVersion)
    {
        return $"chunks/{worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
    }
}
