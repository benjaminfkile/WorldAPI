using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace WorldApi.World;

public class TerrainChunkCoordinator : ITerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly TerrainChunkGenerator _generator;
    private readonly TerrainChunkWriter _writer;
    private readonly string _worldVersion;
    private readonly ILogger<TerrainChunkCoordinator> _logger;

    public TerrainChunkCoordinator(
        WorldChunkRepository repository,
        TerrainChunkGenerator generator,
        TerrainChunkWriter writer,
        IOptions<WorldConfig> config,
        ILogger<TerrainChunkCoordinator> logger)
    {
        _repository = repository;
        _generator = generator;
        _writer = writer;
        _worldVersion = config.Value.Version;
        _logger = logger;
    }

    private async Task<TerrainChunk> GenerateAndUploadChunkAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string layer)
    {
        // Build S3 key for metadata
        string s3Key = $"chunks/{_worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";

        // Step 1: Insert pending metadata row
        // Use UPSERT to handle concurrent requests - if another request already inserted, this will update
        await _repository.InsertPendingAsync(
            chunkX, chunkZ, layer, resolution, _worldVersion, s3Key);

        try
        {
            // Step 2: Generate chunk
            var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);

            // Step 3: Serialize and upload to S3
            // Writer handles not overwriting existing objects
            var uploadResult = await _writer.WriteAsync(chunk);

            // Step 4: Update metadata to ready
            await _repository.UpdateToReadyAsync(
                chunkX, chunkZ, layer, resolution, _worldVersion, uploadResult.Checksum);

            return chunk;
        }
        catch (Exception ex)
        {
            // Note: In production, you might want to update status to 'failed'
            // For now, we'll leave it in pending and let the caller handle the exception
            throw new InvalidOperationException(
                $"Failed to generate chunk ({chunkX}, {chunkZ}) resolution={resolution}: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsChunkReadyAsync(int chunkX, int chunkZ, int resolution, string layer = "terrain")
    {
        return await _repository.IsChunkReadyAsync(chunkX, chunkZ, layer, resolution, _worldVersion);
    }

    public async Task<WorldChunkMetadata?> GetChunkMetadataAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string layer = "terrain")
    {
        return await _repository.GetChunkAsync(chunkX, chunkZ, layer, resolution, _worldVersion);
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
        // Validate world version matches
        if (worldVersion != _worldVersion)
        {
            return ChunkStatus.NotFound;
        }

        // Check metadata
        var metadata = await _repository.GetChunkAsync(chunkX, chunkZ, layer, resolution, _worldVersion);

        if (metadata == null)
        {
            return ChunkStatus.NotFound;
        }

        return metadata.Status;
    }

    /// <summary>
    /// Triggers chunk generation without waiting for completion.
    /// Only starts generation if chunk is not already pending or ready.
    /// Uses fire-and-forget pattern for async generation.
    /// </summary>
    public virtual async Task TriggerGenerationAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain")
    {
        // Validate world version
        if (worldVersion != _worldVersion)
        {
            throw new ArgumentException($"Invalid world version: {worldVersion}. Expected: {_worldVersion}");
        }

        // Check if chunk already exists or is pending
        var existingMetadata = await _repository.GetChunkAsync(
            chunkX, chunkZ, layer, resolution, _worldVersion);

        if (existingMetadata != null)
        {
            // Chunk already exists (either pending or ready) - do not regenerate
            return;
        }

        // Insert pending metadata
        string s3Key = $"chunks/{_worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
        await _repository.InsertPendingAsync(chunkX, chunkZ, layer, resolution, _worldVersion, s3Key);

        // Fire-and-forget generation
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation(
                    "Starting background terrain generation: chunk ({ChunkX}, {ChunkZ}), resolution {Resolution}",
                    chunkX, chunkZ, resolution);

                var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
                var uploadResult = await _writer.WriteAsync(chunk);
                await _repository.UpdateToReadyAsync(
                    chunkX, chunkZ, layer, resolution, _worldVersion, uploadResult.Checksum);

                _logger.LogInformation(
                    "Completed background terrain generation: chunk ({ChunkX}, {ChunkZ}), resolution {Resolution}, checksum {Checksum}",
                    chunkX, chunkZ, resolution, uploadResult.Checksum);
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
