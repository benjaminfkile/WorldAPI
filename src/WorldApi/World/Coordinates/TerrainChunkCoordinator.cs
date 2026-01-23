using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using WorldApi.World.Config;
using WorldApi.World.Chunks;

namespace WorldApi.World.Coordinates;

/// <summary>
/// Orchestrates terrain chunk generation, upload, and metadata storage.
/// 
/// Applies backpressure via SemaphoreSlim to limit concurrent database writes
/// and prevent connection exhaustion under heavy load.
/// </summary>
public class TerrainChunkCoordinator : ITerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly TerrainChunkGenerator _generator;
    private readonly TerrainChunkWriter _writer;
    private readonly ILogger<TerrainChunkCoordinator> _logger;
    private readonly SemaphoreSlim _dbWriteSemaphore;

    public TerrainChunkCoordinator(
        WorldChunkRepository repository,
        TerrainChunkGenerator generator,
        TerrainChunkWriter writer,
        ILogger<TerrainChunkCoordinator> logger,
        SemaphoreSlim dbWriteSemaphore)
    {
        _repository = repository;
        _generator = generator;
        _writer = writer;
        _logger = logger;
        _dbWriteSemaphore = dbWriteSemaphore ?? throw new ArgumentNullException(nameof(dbWriteSemaphore));
    }

    private async Task<TerrainChunk> GenerateAndUploadChunkAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string layer,
        string worldVersion)
    {
        // Build S3 key using world version string (for cache busting and multi-world support)
        string s3Key = $"chunks/{worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";

        try
        {
            // Step 1: Generate chunk in memory
            var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);

            // Step 2: Serialize chunk for S3 (do this sync to capture any errors early)
            byte[] serializedData = TerrainChunkSerializer.Serialize(chunk);

            // Step 3: Upsert DB metadata with backpressure guard
            // This is FAST and response-blocking (good)
            // SemaphoreSlim ensures max N concurrent DB writes to prevent connection storms
            string contentHash = Convert.ToHexString(SHA256.HashData(serializedData));
            await _dbWriteSemaphore.WaitAsync();
            try
            {
                await _repository.UpsertReadyAsync(
                    chunkX, chunkZ, layer, resolution, worldVersion, s3Key, contentHash);
            }
            finally
            {
                _dbWriteSemaphore.Release();
            }

            // Step 4: Upload to S3 in background (fire-and-forget)
            // Response returns immediately after DB upsert
            // S3 upload completes async without blocking the request thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await _writer.WriteAsync(chunk, s3Key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Background S3 upload failed (chunk already in DB): ChunkX={ChunkX}, ChunkZ={ChunkZ}",
                        chunkX, chunkZ);
                    // Don't throw - chunk is already marked ready in DB
                    // S3 upload will be retried on next cache miss
                }
            });

            return chunk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate chunk: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}",
                chunkX, chunkZ, resolution);
            throw new InvalidOperationException(
                $"Failed to generate chunk ({chunkX}, {chunkZ}) resolution={resolution}: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsChunkReadyAsync(int chunkX, int chunkZ, int resolution, string worldVersion, string layer = "terrain")
    {
        return await _repository.IsChunkReadyAsync(chunkX, chunkZ, layer, resolution, worldVersion);
    }

    public async Task<WorldChunkMetadata?> GetChunkMetadataAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain")
    {
        return await _repository.GetChunkAsync(chunkX, chunkZ, layer, resolution, worldVersion);
    }

    /// <summary>
    /// Gets chunk status from metadata repository only.
    /// Does NOT access S3 - that's the controller's responsibility.
    /// </summary>
    public virtual async Task<ChunkStatus> GetChunkStatusAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain")
    {
        // Check metadata
        var metadata = await _repository.GetChunkAsync(chunkX, chunkZ, layer, resolution, worldVersion);

        if (metadata == null)
        {
            return ChunkStatus.NotFound;
        }

        return metadata.Status;
    }

    /// <summary>
    /// Triggers chunk generation without waiting for completion.
    /// Only starts generation if chunk is not already ready.
    /// Uses fire-and-forget pattern for async generation.
    /// Database writes are guarded by SemaphoreSlim to prevent connection storms.
    /// </summary>
    public virtual async Task TriggerGenerationAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain")
    {
        // Check if chunk already exists and is ready
        var existingMetadata = await _repository.GetChunkAsync(
            chunkX, chunkZ, layer, resolution, worldVersion);

        if (existingMetadata != null && existingMetadata.Status == ChunkStatus.Ready)
        {
            // Chunk already ready - do not regenerate
            return;
        }

        // Fire-and-forget generation (with S3-first ordering)
        _ = Task.Run(async () =>
        {
            try
            {
                // _logger.LogInformation(
                //     "Starting background terrain generation: chunk ({ChunkX}, {ChunkZ}), resolution {Resolution}",
                //     chunkX, chunkZ, resolution);

                string s3Key = $"chunks/{worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
                var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
                var uploadResult = await _writer.WriteAsync(chunk, s3Key);
                
                // Guard DB write with SemaphoreSlim for backpressure
                await _dbWriteSemaphore.WaitAsync();
                try
                {
                    await _repository.UpsertReadyAsync(
                        chunkX, chunkZ, layer, resolution, worldVersion, s3Key, uploadResult.Checksum);
                }
                finally
                {
                    _dbWriteSemaphore.Release();
                }

                // _logger.LogInformation(
                //     "Completed background terrain generation: chunk ({ChunkX}, {ChunkZ}), resolution {Resolution}, checksum {Checksum}",
                //     chunkX, chunkZ, resolution, uploadResult.Checksum);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Background terrain generation failed: chunk ({ChunkX}, {ChunkZ}), resolution {Resolution}",
                    chunkX, chunkZ, resolution);
            }
        });
    }
}
