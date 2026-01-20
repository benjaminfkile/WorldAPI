using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Dem;

public sealed class HgtTileLoader
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public HgtTileLoader(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
    }

    public async Task<SrtmTileData> LoadAsync(DemTile tile)
    {
        // Fetch object bytes from S3
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = tile.S3Key
        };

        using var response = await _s3Client.GetObjectAsync(request);
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        byte[] data = memoryStream.ToArray();

        // Decode bytes using HGT decoder (detects SRTM1 or SRTM3)
        var (elevations, width, height) = SrtmDecoder.Decode(data);

        // Populate geographic bounds from DemTile
        return new SrtmTileData
        {
            MinLatitude = tile.MinLatitude,
            MaxLatitude = tile.MaxLatitude,
            MinLongitude = tile.MinLongitude,
            MaxLongitude = tile.MaxLongitude,
            Width = width,
            Height = height,
            Elevations = elevations
        };
    }
}
