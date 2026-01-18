using Microsoft.AspNetCore.Mvc;
using WorldApi.World;
using Amazon.S3.Model;

namespace WorldApi.Controllers;

[ApiController]
public sealed class TerrainChunksController : ControllerBase
{
    private readonly ITerrainChunkCoordinator _coordinator;
    private readonly ITerrainChunkReader _reader;
    private readonly ILogger<TerrainChunksController> _logger;

    public TerrainChunksController(
        ITerrainChunkCoordinator coordinator,
        ITerrainChunkReader reader,
        ILogger<TerrainChunksController> logger)
    {
        _coordinator = coordinator;
        _reader = reader;
        _logger = logger;
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
        // Check chunk status via coordinator
        var status = await _coordinator.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion);

        _logger.LogInformation(
            "[TRACE] Initial status check: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}, Status={Status}",
            chunkX, chunkZ, resolution, status);

        if (status == ChunkStatus.Ready)
        {
            // Log cache hit
            _logger.LogInformation(
                "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
                chunkX, chunkZ, resolution, worldVersion, "hit");

            // Get S3 stream from reader
            GetObjectResponse? s3Response = null;
            try
            {
                s3Response = await _reader.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion);

                _logger.LogInformation(
                    "[TRACE] S3 stream acquired: ChunkX={ChunkX}, ChunkZ={ChunkZ}, ContentLength={ContentLength}",
                    chunkX, chunkZ, s3Response.ContentLength);

                // Stream binary data directly from S3 to HTTP response
                // Immutable content - cache forever
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                Response.Headers.ContentType = "application/octet-stream";
                
                // Preserve ETag from S3
                if (!string.IsNullOrEmpty(s3Response.ETag))
                {
                    Response.Headers.ETag = s3Response.ETag;
                }

                // Set content length for proper streaming
                Response.ContentLength = s3Response.ContentLength;

                _logger.LogInformation(
                    "[TRACE] Streaming S3 response to client: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Bytes={ContentLength}",
                    chunkX, chunkZ, s3Response.ContentLength);

                // Stream directly from S3 to HTTP response body
                await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
                return new EmptyResult();
            }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Metadata says ready but S3 object missing - data inconsistency
                _logger.LogWarning(
                    "[TRACE] S3 404 mismatch: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}. Treating as NotFound.",
                    chunkX, chunkZ, resolution);
                // Fall through to trigger regeneration
                status = ChunkStatus.NotFound;
            }
            finally
            {
                s3Response?.Dispose();
            }
        }

        if (status == ChunkStatus.Pending)
        {
            // Log pending chunk
            _logger.LogInformation(
                "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
                chunkX, chunkZ, resolution, worldVersion, "pending");

            _logger.LogInformation(
                "[TRACE] Returning 202 Accepted (status=Pending): ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
                chunkX, chunkZ, resolution);

            // Chunk is being generated - do not cache, do not regenerate
            Response.Headers.CacheControl = "no-store";
            return Accepted();
        }

        // Log generation trigger
        _logger.LogInformation(
            "Terrain chunk request: {ChunkX}, {ChunkZ}, resolution {Resolution}, world {WorldVersion}, status {Status}",
            chunkX, chunkZ, resolution, worldVersion, "generated");

        _logger.LogInformation(
            "[TRACE] Triggering generation (status=NotFound): ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
            chunkX, chunkZ, resolution);

        // Chunk doesn't exist - trigger generation and return 202
        // TriggerGenerationAsync will check if already pending/ready to avoid duplicate work
        Response.Headers.CacheControl = "no-store";
        await _coordinator.TriggerGenerationAsync(chunkX, chunkZ, resolution, worldVersion);
        return Accepted();
    }
}
