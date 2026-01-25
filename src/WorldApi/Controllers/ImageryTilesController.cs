using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using WorldApi.Configuration;

namespace WorldApi.Controllers;

/// <summary>
/// Serves map imagery tiles in XYZ format.
/// 
/// Behavior:
/// - Cache-first: Check S3 for tile
/// - On hit: Redirect to CloudFront (if enabled) or stream from S3
/// - On miss: Fetch from MapTiler, stream to client, async S3 storage
/// - Never blocks client on S3 write
/// - Tiles are immutable once stored
/// 
/// Endpoint: GET /world/imagery/{provider}/{z}/{x}/{y}
/// Example: /world/imagery/maptiler/10/341/612
/// </summary>
[ApiController]
public sealed class ImageryTilesController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageryTilesController> _logger;
    private readonly string? _cloudfrontUrl;
    private readonly bool _useCloudfront;
    private readonly bool _useLocalS3;
    private readonly string? _s3BucketName;
    private readonly string? _mapTilerApiKey;

    public ImageryTilesController(
        IAmazonS3 s3Client,
        IHttpClientFactory httpClientFactory,
        IOptions<WorldAppSecrets> appSecrets,
        ILogger<ImageryTilesController> logger)
    {
        _s3Client = s3Client;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WorldApi/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
        _s3BucketName = appSecrets.Value.S3BucketName;
        _mapTilerApiKey = appSecrets.Value.MapTilerApiKey;
        _cloudfrontUrl = appSecrets.Value.CloudfrontUrl;

        // Parse UseLocalS3 string to bool (accepts "true"/"false", case-insensitive)
        _useLocalS3 = bool.TryParse(appSecrets.Value.UseLocalS3, out var localS3Parsed) && localS3Parsed;

        // Use CloudFront if:
        // 1. useLocalS3 is false/missing AND
        // 2. (cloudfrontUrl is set OR useCloudfront is explicitly "true")
        var useCloudfrontRaw = appSecrets.Value.UseCloudfront;
        var cloudfrontExplicitlyDisabled = bool.TryParse(useCloudfrontRaw, out var parsed) && !parsed;
        _useCloudfront = !_useLocalS3 && !cloudfrontExplicitlyDisabled && !string.IsNullOrEmpty(_cloudfrontUrl);
    }

    /// <summary>
    /// Get or fetch a map imagery tile. Returns tile data on hit, or fetches from upstream on miss.
    /// </summary>
    [HttpGet("/world/imagery/{provider}/{map}/{z}/{x}/{y}")]
    public async Task<IActionResult> GetImageryTile(
        string provider,
        string map,
        int z,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        // Validate provider (currently only "maptiler" supported)
        if (!provider.Equals("maptiler", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unsupported imagery provider: {Provider}", provider);
            return BadRequest(new { error = $"Provider '{provider}' is not supported" });
        }

        // Validate map name (alphanumeric, hyphens, underscores only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(map, @"^[a-zA-Z0-9_-]+$"))
        {
            _logger.LogWarning("Invalid map name: {Map}", map);
            return BadRequest(new { error = "Invalid map name (alphanumeric, hyphens, underscores only)" });
        }

        // Validate tile coordinates
        if (!ValidateTileCoordinates(z, x, y))
        {
            _logger.LogWarning("Invalid tile coordinates: z={Z}, x={X}, y={Y}", z, x, y);
            return BadRequest(new { error = "Invalid tile coordinates" });
        }

        // Tile format (PNG for MapTiler)
        const string tileFormat = "png";
        string s3Key = BuildS3Key(provider, map, z, x, y, tileFormat);

        // Try to fetch from cache (S3)
        var cachedTile = await TryGetFromCacheAsync(s3Key, cancellationToken);
        
        if (cachedTile != null)
        {
            _logger.LogInformation(
                "Imagery tile cache hit: {Provider}/{Map}/{Z}/{X}/{Y}",
                provider, map, z, x, y);
            
            return cachedTile;
        }

        _logger.LogInformation(
            "Imagery tile cache miss: {Provider}/{Map}/{Z}/{X}/{Y}, fetching from upstream",
            provider, map, z, x, y);

        // Cache miss - fetch from MapTiler
        return await FetchAndStreamFromUpstreamAsync(
            provider, map, z, x, y, s3Key, tileFormat, cancellationToken);
    }

    /// <summary>
    /// Attempts to fetch tile from S3 cache.
    /// Returns IActionResult if found (either CloudFront redirect or stream).
    /// Returns null if tile not found in cache.
    /// </summary>
    private async Task<IActionResult?> TryGetFromCacheAsync(string s3Key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_s3BucketName))
        {
            _logger.LogError("S3 bucket name not configured");
            return null;
        }

        try
        {
            // Check if object exists in S3
            var request = new GetObjectRequest
            {
                BucketName = _s3BucketName,
                Key = s3Key
            };

            GetObjectResponse? s3Response = null;
            try
            {
                s3Response = await _s3Client.GetObjectAsync(request, cancellationToken);
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Tile not found in cache
                return null;
            }

            // Tile found - decide whether to redirect or stream
            if (_useCloudfront && !string.IsNullOrEmpty(_cloudfrontUrl))
            {
                // CloudFront enabled - redirect to CDN edge
                string cloudfrontTileUrl = $"{_cloudfrontUrl.TrimEnd('/')}/{s3Key}";
                
                _logger.LogInformation(
                    "Imagery tile CloudFront redirect: {S3Key} -> {CloudfrontUrl}",
                    s3Key, cloudfrontTileUrl);

                // 302 redirect - client fetches directly from CloudFront
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                s3Response?.Dispose();
                return Redirect(cloudfrontTileUrl);
            }
            else
            {
                // No CloudFront - stream directly from S3
                _logger.LogInformation(
                    "Imagery tile S3 stream: {S3Key}, ContentLength={ContentLength}",
                    s3Key, s3Response.ContentLength);

                // Stream binary data directly from S3 to HTTP response
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                Response.Headers.ContentType = "image/webp";
                
                if (!string.IsNullOrEmpty(s3Response.ETag))
                {
                    Response.Headers.ETag = s3Response.ETag;
                }

                Response.ContentLength = s3Response.ContentLength;

                try
                {
                    await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
                    return new EmptyResult();
                }
                finally
                {
                    s3Response.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking S3 cache for {S3Key}", s3Key);
            return null;
        }
    }

    /// <summary>
    /// Fetches tile from MapTiler upstream, streams to client, and persists to S3 asynchronously.
    /// </summary>
    private async Task<IActionResult> FetchAndStreamFromUpstreamAsync(
        string provider,
        string map,
        int z,
        int x,
        int y,
        string s3Key,
        string tileFormat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_mapTilerApiKey))
        {
            _logger.LogError("MapTiler API key not configured");
            return StatusCode(500, new { error = "Upstream provider not configured" });
        }

        string upstreamUrl = BuildMapTilerUrl(map, z, x, y, _mapTilerApiKey);

        try
        {
            _logger.LogInformation(
                "Fetching imagery tile from MapTiler: {Provider}/{Map}/{Z}/{X}/{Y}",
                provider, map, z, x, y);

            using var upstreamResponse = await _httpClient.GetAsync(upstreamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MapTiler upstream error: {Provider}/{Map}/{Z}/{X}/{Y}, Status={StatusCode}",
                    provider, map, z, x, y, upstreamResponse.StatusCode);

                return StatusCode((int)upstreamResponse.StatusCode, 
                    new { error = $"Upstream provider returned {upstreamResponse.StatusCode}" });
            }

            // Set response headers from upstream
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            Response.Headers.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "image/webp";
            
            if (upstreamResponse.Content.Headers.ContentLength.HasValue)
            {
                Response.ContentLength = upstreamResponse.Content.Headers.ContentLength.Value;
            }

            // Copy ETag if present
            if (upstreamResponse.Headers.ETag != null)
            {
                Response.Headers.ETag = upstreamResponse.Headers.ETag.ToString();
            }

            // Stream response to client
            using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            
            // Create a tee stream to simultaneously write to client and buffer for S3
            var s3Buffer = new MemoryStream();
            var teeStream = new TeeStream(upstreamStream, s3Buffer);

            try
            {
                await teeStream.CopyToAsync(Response.Body, 81920, cancellationToken); // 80KB buffer
            }
            finally
            {
                teeStream.Dispose();
            }

            // Fire-and-forget: persist to S3 asynchronously
            // Do NOT await this - client should not wait for S3 write
            _ = PersistToS3Async(s3Key, s3Buffer.ToArray());

            _logger.LogInformation(
                "Imagery tile streaming complete: {Provider}/{Map}/{Z}/{X}/{Y}, triggering async S3 persistence",
                provider, map, z, x, y);

            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Imagery tile request cancelled: {Provider}/{Map}/{Z}/{X}/{Y}",
                provider, map, z, x, y);
            return StatusCode(408);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error fetching imagery tile from upstream: {Provider}/{Map}/{Z}/{X}/{Y}",
                provider, map, z, x, y);
            return StatusCode(502, new { error = "Error fetching from upstream provider" });
        }
    }

    /// <summary>
    /// Persists tile data to S3 asynchronously.
    /// Logs errors but does NOT fail the request if this fails.
    /// </summary>
    private async Task PersistToS3Async(string s3Key, byte[] tileData)
    {
        if (string.IsNullOrEmpty(_s3BucketName))
        {
            _logger.LogError("S3 bucket name not configured - cannot persist tile");
            return;
        }

        try
        {
            // Check if tile already exists (immutability guarantee)
            // Try to get object metadata - if it exists, skip write
            try
            {
                using var response = await _s3Client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _s3BucketName,
                    Key = s3Key
                });

                // Tile already exists - skip write
                _logger.LogInformation(
                    "Imagery tile already exists in S3: {S3Key}, skipping write",
                    s3Key);
                return;
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Tile doesn't exist - proceed with write
            }

            // Store tile with immutable headers
            var putRequest = new PutObjectRequest
            {
                BucketName = _s3BucketName,
                Key = s3Key,
                InputStream = new MemoryStream(tileData),
                ContentType = "image/webp"
            };

            // Add metadata to S3 object
            putRequest.Metadata.Add("immutable", "true");
            putRequest.Metadata.Add("cached-at", DateTime.UtcNow.ToString("O"));

            // Set cache headers for S3 object metadata
            putRequest.Headers.CacheControl = "public, max-age=31536000, immutable";

            await _s3Client.PutObjectAsync(putRequest);

            _logger.LogInformation(
                "Imagery tile persisted to S3: {S3Key}, Size={Size} bytes",
                s3Key, tileData.Length);
        }
        catch (Exception ex)
        {
            // Log but don't fail the request
            _logger.LogError(ex,
                "Error persisting imagery tile to S3: {S3Key}",
                s3Key);
        }
    }

    /// <summary>
    /// Constructs S3 key for imagery tile storage.
    /// Format: imagery/{provider}/{z}/{x}/{y}.{format}
    /// </summary>
    private string BuildS3Key(string provider, string map, int z, int x, int y, string format)
    {
        return $"imagery/{provider}/{map}/{z}/{x}/{y}.{format}";
    }

    /// <summary>
    /// Constructs MapTiler upstream URL for tile.
    /// </summary>
    private string BuildMapTilerUrl(string map, int z, int x, int y, string apiKey)
    {
        // Dynamic MapTiler map endpoint
        // Raster tiles format: https://api.maptiler.com/maps/{map}/{z}/{x}/{y}.png
        return $"https://api.maptiler.com/maps/{map}/{z}/{x}/{y}.png?key={apiKey}";
    }

    /// <summary>
    /// Validates tile coordinates are within acceptable range.
    /// </summary>
    private bool ValidateTileCoordinates(int z, int x, int y)
    {
        // Zoom level 0-28 (Web Mercator standard)
        if (z < 0 || z > 28)
            return false;

        // X and Y must be in range [0, 2^z)
        int maxCoord = 1 << z; // 2^z
        if (x < 0 || x >= maxCoord)
            return false;
        if (y < 0 || y >= maxCoord)
            return false;

        return true;
    }
}

/// <summary>
/// Helper stream that writes to two destinations simultaneously.
/// Used to stream upstream response to client while buffering for S3.
/// </summary>
internal sealed class TeeStream : Stream
{
    private readonly Stream _upstream;
    private readonly Stream _buffer;

    public TeeStream(Stream upstream, Stream buffer)
    {
        _upstream = upstream;
        _buffer = buffer;
    }

    public override bool CanRead => _upstream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _upstream.Flush();
        _buffer.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _upstream.FlushAsync(cancellationToken);
        await _buffer.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _upstream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _buffer.Write(buffer, offset, bytesRead);
        }
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRead = await _upstream.ReadAsync(buffer, offset, count, cancellationToken);
        if (bytesRead > 0)
        {
            await _buffer.WriteAsync(buffer, offset, bytesRead, cancellationToken);
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
