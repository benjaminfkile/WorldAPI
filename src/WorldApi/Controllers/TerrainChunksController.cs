using Microsoft.AspNetCore.Mvc;
using WorldApi.World.Coordinates;
using WorldApi.World.Chunks;
using WorldApi.Configuration;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace WorldApi.Controllers;

[ApiController]
public sealed class TerrainChunksController : ControllerBase
{
    private readonly ITerrainChunkCoordinator _coordinator;
    private readonly ITerrainChunkReader _reader;
    private readonly ILogger<TerrainChunksController> _logger;
    private readonly string? _cloudfrontUrl;

    public TerrainChunksController(
        ITerrainChunkCoordinator coordinator,
        ITerrainChunkReader reader,
        IOptions<WorldAppSecrets> appSecrets,
        ILogger<TerrainChunksController> logger)
    {
        _coordinator = coordinator;
        _reader = reader;
        _logger = logger;
        _cloudfrontUrl = appSecrets.Value.CloudfrontUrl;
    }

    /// <summary>
    /// Get or generate a terrain chunk. Returns binary chunk data if ready, or 202 Accepted if still generating.
    /// </summary>
    [HttpGet("/world/{worldVersion}/terrain/{resolution}/{chunkX}/{chunkZ}")]
    public async Task<IActionResult> GetTerrainChunk(
        string worldVersion,
        int resolution,
        int chunkX,
        int chunkZ,
        CancellationToken cancellationToken = default)
    {
        // _logger.LogInformation("[DEBUG] CloudFront URL configured: {CloudfrontUrl}", _cloudfrontUrl ?? "(null)");
        
        // Check chunk status via coordinator
        var status = await _coordinator.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion);

        // _logger.LogInformation(
        //     "[TRACE] Initial status check: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, Status={Status}",
        //     chunkX, chunkZ, resolution, status);

        if (status == ChunkStatus.Ready)
        {
            // Log cache hit
            // _logger.LogInformation(
            //     "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
            //     chunkX, chunkZ, resolution, worldVersion, "hit");

            // Get chunk metadata for S3 key
            var metadata = await _coordinator.GetChunkMetadataAsync(chunkX, chunkZ, resolution);
            if (metadata == null)
            {
                _logger.LogWarning(
                    "[TRACE] Metadata disappeared after status check: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
                    chunkX, chunkZ, resolution);
                status = ChunkStatus.NotFound;
            }
            else if (!string.IsNullOrEmpty(_cloudfrontUrl))
            {
                // CloudFront configured - redirect to CDN edge
                string cloudfrontChunkUrl = $"{_cloudfrontUrl.TrimEnd('/')}/{metadata.S3Key}";
                
                // _logger.LogInformation(
                //     "[TRACE] Redirecting to CloudFront: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Url={Url}",
                //     chunkX, chunkZ, cloudfrontChunkUrl);

                // 302 redirect - client fetches directly from CloudFront
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Redirect(cloudfrontChunkUrl);
            }
            else
            {
                // No CloudFront - stream directly from S3 (legacy mode)
                GetObjectResponse? s3Response = null;
                try
                {
                    s3Response = await _reader.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion);

                    // _logger.LogInformation(
                    //     "[TRACE] S3 stream acquired: ChunkX={ChunkX}, ChunkZ={ChunkZ}, ContentLength={ContentLength}",
                    //     chunkX, chunkZ, s3Response.ContentLength);

                    // Stream binary data directly from S3 to HTTP response
                    Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                    Response.Headers.ContentType = "application/octet-stream";
                    
                    if (!string.IsNullOrEmpty(s3Response.ETag))
                    {
                        Response.Headers.ETag = s3Response.ETag;
                    }

                    Response.ContentLength = s3Response.ContentLength;

                    // _logger.LogInformation(
                    //     "[TRACE] Streaming S3 response to client: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Bytes={ContentLength}",
                    //     chunkX, chunkZ, s3Response.ContentLength);

                    await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
                    return new EmptyResult();
                }
                catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "[TRACE] S3 404 mismatch: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}. Treating as NotFound.",
                        chunkX, chunkZ, resolution);
                    status = ChunkStatus.NotFound;
                }
                finally
                {
                    s3Response?.Dispose();
                }
            }
        }

        if (status == ChunkStatus.Pending)
        {
            // Log pending chunk
            // _logger.LogInformation(
            //     "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
            //     chunkX, chunkZ, resolution, worldVersion, "pending");

            // _logger.LogInformation(
            //     "[TRACE] Returning 202 Accepted (status=Pending): ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
            //     chunkX, chunkZ, resolution);

            // Chunk is being generated - do not cache, do not regenerate
            Response.Headers.CacheControl = "no-store";
            return Accepted();
        }

        // Log generation trigger
        // _logger.LogInformation(
        //     "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
        //     chunkX, chunkZ, resolution, worldVersion, "generated");

        // _logger.LogInformation(
        //     "[TRACE] Triggering generation (status=NotFound): ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
        //     chunkX, chunkZ, resolution);

        // Chunk doesn't exist - trigger generation and return 202
        // TriggerGenerationAsync will check if already pending/ready to avoid duplicate work
        Response.Headers.CacheControl = "no-store";
        await _coordinator.TriggerGenerationAsync(chunkX, chunkZ, resolution, worldVersion);
        return Accepted();
    }
}
