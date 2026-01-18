using Microsoft.Extensions.Options;

namespace WorldApi.World;

public sealed class TerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly TerrainChunkReader _reader;
    private readonly TerrainChunkGenerator _generator;
    private readonly TerrainChunkWriter _writer;
    private readonly string _worldVersion;

    public TerrainChunkCoordinator(
        WorldChunkRepository repository,
        TerrainChunkReader reader,
        TerrainChunkGenerator generator,
        TerrainChunkWriter writer,
        IOptions<WorldConfig> config)
    {
        _repository = repository;
        _reader = reader;
        _generator = generator;
        _writer = writer;
        _worldVersion = config.Value.Version;
    }

    public async Task<TerrainChunk> GetOrGenerateChunkAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string layer = "terrain")
    {
        // Step 1: Check if chunk exists and is ready
        var existingChunk = await _repository.GetChunkAsync(
            chunkX, chunkZ, layer, resolution, _worldVersion);

        if (existingChunk != null && existingChunk.Status == ChunkStatus.Ready)
        {
            // Load from S3
            return await _reader.ReadAsync(chunkX, chunkZ, resolution, _worldVersion);
        }

        // Step 2: Chunk doesn't exist or not ready, generate it
        return await GenerateAndUploadChunkAsync(chunkX, chunkZ, resolution, layer);
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
}
