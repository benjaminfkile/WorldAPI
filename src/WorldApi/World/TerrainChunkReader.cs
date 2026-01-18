using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World;

public sealed class TerrainChunkReader
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

    private string BuildS3Key(int chunkX, int chunkZ, int resolution, string worldVersion)
    {
        return $"chunks/{worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
    }
}
