using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Dem;

/// <summary>
/// Read-only client for downloading SRTM DEM tiles from the public USGS/LPDAAC S3 bucket.
/// 
/// This client is responsible for:
/// - Fetching tiles by key from the public SRTM dataset
/// - Returning clear errors for missing tiles (404)
/// - NOT writing data
/// - NOT listing buckets
/// 
/// Public SRTM bucket: usgs-eros-dem-srtm1.s3.amazonaws.com
/// Tile format: {N|S}{lat}{E|W}{lon}.hgt (e.g., N46W113.hgt)
/// 
/// Reference: https://lpdaac.usgs.gov/products/srtmgl1v003/
/// </summary>
public sealed class PublicSrtmClient
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _publicBucketName;
    private readonly ILogger<PublicSrtmClient> _logger;

    /// <summary>
    /// Public USGS SRTM bucket name for SRTM 1 Arc-second Global DEM
    /// </summary>
    private const string PublicSrtmBucketName = "usgs-eros-dem-srtm1";

    public PublicSrtmClient(IAmazonS3 s3Client, ILogger<PublicSrtmClient> logger)
    {
        _s3Client = s3Client;
        _publicBucketName = PublicSrtmBucketName;
        _logger = logger;
    }

    /// <summary>
    /// Fetch a single SRTM tile from the public bucket by tile name.
    /// </summary>
    /// <param name="tileName">SRTM tile filename (e.g., "N46W113.hgt")</param>
    /// <returns>Tile data as bytes</returns>
    /// <exception cref="TileNotFoundException">If the tile does not exist in public SRTM (404)</exception>
    /// <exception cref="InvalidOperationException">If there's a configuration or access error</exception>
    public async Task<byte[]> FetchTileAsync(string tileName)
    {
        if (string.IsNullOrWhiteSpace(tileName))
            throw new ArgumentException("Tile name cannot be null or empty", nameof(tileName));

        try
        {
            _logger.LogInformation("📡 Fetching SRTM tile from public bucket: {TileName}", tileName);

            var request = new GetObjectRequest
            {
                BucketName = _publicBucketName,
                Key = tileName  // Flat structure - just the tile name in root
            };

            using var response = await _s3Client.GetObjectAsync(request);
            using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);

            var data = memoryStream.ToArray();
            _logger.LogInformation("✓ Successfully fetched SRTM tile: {TileName} ({SizeBytes} bytes)", tileName, data.Length);

            return data;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("⚠️  SRTM tile not found in public bucket: {TileName}", tileName);
            throw new TileNotFoundException($"SRTM tile '{tileName}' not found in public bucket", tileName, ex);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "❌ AWS S3 error fetching SRTM tile {TileName}: {ErrorCode} - {Message}", 
                tileName, ex.ErrorCode, ex.Message);
            throw new InvalidOperationException($"Failed to fetch SRTM tile '{tileName}' from public bucket: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Unexpected error fetching SRTM tile {TileName}", tileName);
            throw new InvalidOperationException($"Unexpected error fetching SRTM tile '{tileName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Fetch a tile by latitude/longitude coordinates.
    /// Computes the tile name automatically using SrtmTileNamer.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees</param>
    /// <param name="longitude">Longitude in decimal degrees</param>
    /// <returns>Tile data as bytes</returns>
    /// <exception cref="TileNotFoundException">If the tile does not exist in public SRTM</exception>
    public async Task<byte[]> FetchTileByCoordinateAsync(double latitude, double longitude)
    {
        var tileName = SrtmTileNamer.ComputeTileName(latitude, longitude);
        return await FetchTileAsync(tileName);
    }
}

/// <summary>
/// Exception thrown when a requested SRTM tile is not found in the public bucket (404 error).
/// </summary>
public sealed class TileNotFoundException : Exception
{
    public string TileName { get; }

    public TileNotFoundException(string message, string tileName, Exception? innerException = null)
        : base(message, innerException)
    {
        TileName = tileName;
    }
}
