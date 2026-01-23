using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace WorldApi.World.Dem;

public sealed class DemTileIndexBuilder
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<DemTileIndexBuilder> _logger;

    public DemTileIndexBuilder(IAmazonS3 s3Client, string bucketName, ILogger<DemTileIndexBuilder> logger)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the dem/srtm/ folder structure exists in S3 by creating a marker object if needed.
    /// Logs when folders are created.
    /// </summary>
    public async Task EnsureFolderStructureAsync()
    {
        const string markerKey = "dem/.gitkeep";
        const string demSrtmMarkerKey = "dem/srtm/.gitkeep";

        try
        {
            // Check if dem/ folder marker exists
            try
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = markerKey
                };
                using var response = await _s3Client.GetObjectAsync(getRequest);
                _logger.LogInformation("✓ dem/ folder already exists in S3");
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // dem/ folder doesn't exist, create it
                _logger.LogInformation("⚠️  dem/ folder not found. Creating folder structure...");
                
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = markerKey,
                    ContentBody = ""
                };
                await _s3Client.PutObjectAsync(putRequest);
                _logger.LogInformation("✓ Created dem/ folder");
            }

            // Check if dem/srtm/ folder marker exists
            try
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = demSrtmMarkerKey
                };
                using var response = await _s3Client.GetObjectAsync(getRequest);
                _logger.LogInformation("✓ dem/srtm/ folder already exists in S3");
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // dem/srtm/ folder doesn't exist, create it
                _logger.LogInformation("⚠️  dem/srtm/ folder not found. Creating folder structure...");
                
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = demSrtmMarkerKey,
                    ContentBody = ""
                };
                await _s3Client.PutObjectAsync(putRequest);
                _logger.LogInformation("✓ Created dem/srtm/ folder");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure DEM folder structure exists in S3. The folder may need to be created manually.");
            throw;
        }
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

            // S3Objects can be null if prefix is empty, so check before iterating
            if (response.S3Objects != null)
            {
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
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return index;
    }
}
