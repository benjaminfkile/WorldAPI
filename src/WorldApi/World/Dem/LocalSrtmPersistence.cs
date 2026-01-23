using Amazon.S3;
using Amazon.S3.Model;

namespace WorldApi.World.Dem;

/// <summary>
/// Service for persisting SRTM DEM tiles to local S3 storage.
/// 
/// Responsibilities:
/// - Write fetched tiles from public SRTM to local S3 cache
/// - Store tiles at: dem/srtm3/{tile}.hgt
/// - Handle overwrites (allowed but rare)
/// - Ensure atomic writes (using S3 PutObject)
/// 
/// Design Notes:
/// - Folder path is dem/srtm3/ (srtm3 = SRTM 3 arc-second data, future expansion)
/// - Uses S3 PutObject for atomicity (single-shot operation, no multipart)
/// - Logs all operations for observability
/// </summary>
public sealed class LocalSrtmPersistence
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<LocalSrtmPersistence> _logger;

    private const string TilesFolderPath = "dem/srtm3";

    public LocalSrtmPersistence(IAmazonS3 s3Client, string bucketName, ILogger<LocalSrtmPersistence> logger)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Save a tile to local S3 at dem/srtm3/{tileName}.
    /// </summary>
    /// <param name="tileName">SRTM tile name (e.g., "N46W113.hgt")</param>
    /// <param name="tileData">Raw tile data (typically 25,934,402 bytes for SRTM 1-arc tiles)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>S3 key where tile was saved</returns>
    /// <exception cref="ArgumentException">If tile name is null/empty or data is null/empty</exception>
    /// <exception cref="InvalidOperationException">If S3 write fails</exception>
    public async Task<string> SaveTileAsync(string tileName, byte[] tileData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tileName))
            throw new ArgumentException("Tile name cannot be null or empty", nameof(tileName));

        if (tileData == null || tileData.Length == 0)
            throw new ArgumentException("Tile data cannot be null or empty", nameof(tileData));

        var s3Key = ComputeS3Key(tileName);

        try
        {
            _logger.LogInformation("Saving tile {TileName} to local S3 at {S3Key} ({SizeBytes} bytes)", 
                tileName, s3Key, tileData.Length);

            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                InputStream = new MemoryStream(tileData),
                ContentType = "application/octet-stream"
            };

            var response = await _s3Client.PutObjectAsync(putRequest, cancellationToken);

            _logger.LogInformation("✓ Tile {TileName} successfully saved to {S3Key} (ETag: {ETag})", 
                tileName, s3Key, response.ETag);

            return s3Key;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Tile save operation for {TileName} was cancelled", tileName);
            throw;
        }
        catch (Amazon.S3.AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Failed to save tile {TileName} to local S3: {StatusCode} {Message}", 
                tileName, ex.StatusCode, ex.Message);
            throw new InvalidOperationException($"Failed to persist tile {tileName} to local S3", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while saving tile {TileName}", tileName);
            throw new InvalidOperationException($"Unexpected error persisting tile {tileName}", ex);
        }
    }

    /// <summary>
    /// Compute the S3 key (full path) for a tile.
    /// </summary>
    /// <param name="tileName">SRTM tile name (e.g., "N46W113.hgt")</param>
    /// <returns>S3 key path (e.g., "dem/srtm3/N46W113.hgt")</returns>
    private static string ComputeS3Key(string tileName)
    {
        return $"{TilesFolderPath}/{tileName}";
    }
}
