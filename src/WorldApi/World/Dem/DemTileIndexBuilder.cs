using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Dem;

public sealed class DemTileIndexBuilder
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public DemTileIndexBuilder(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
    }

    public async Task<DemTileIndex> BuildAsync()
    {
        var index = new DemTileIndex();

        var request = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = "dem/srtm/"
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request);

            foreach (var s3Object in response.S3Objects)
            {
                if (s3Object.Key.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase))
                {
                    string filename = Path.GetFileName(s3Object.Key);
                    var tile = SrtmFilenameParser.Parse(filename);
                    
                    // Update S3Key to use the full key path
                    var tileWithFullKey = tile with { S3Key = s3Object.Key };
                    index.Add(tileWithFullKey);
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return index;
    }
}
