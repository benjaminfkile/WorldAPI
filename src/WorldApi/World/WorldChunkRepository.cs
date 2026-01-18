using Npgsql;
using System.Data;

namespace WorldApi.World;

public sealed class WorldChunkRepository
{
    private readonly string _connectionString;

    public WorldChunkRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<WorldChunkMetadata> InsertPendingAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion,
        string s3Key)
    {
        const string sql = @"
            INSERT INTO world_chunks (
                chunk_x, chunk_z, layer, resolution, world_version, 
                s3_key, checksum, status, generated_at
            )
            VALUES (
                @chunkX, @chunkZ, @layer, @resolution, @worldVersion,
                @s3Key, '', @status, @generatedAt
            )
            ON CONFLICT (chunk_x, chunk_z, layer, resolution, world_version) 
            DO UPDATE SET 
                status = EXCLUDED.status,
                generated_at = EXCLUDED.generated_at
            RETURNING chunk_x, chunk_z, layer, resolution, world_version, 
                      s3_key, checksum, status, generated_at";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersion", worldVersion);
        command.Parameters.AddWithValue("@s3Key", s3Key);
        command.Parameters.AddWithValue("@status", ChunkStatus.Pending);
        command.Parameters.AddWithValue("@generatedAt", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new WorldChunkMetadata
        {
            ChunkX = reader.GetInt32(0),
            ChunkZ = reader.GetInt32(1),
            Layer = reader.GetString(2),
            Resolution = reader.GetInt32(3),
            WorldVersion = reader.GetString(4),
            S3Key = reader.GetString(5),
            Checksum = reader.GetString(6),
            Status = reader.GetString(7),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(8)
        };
    }

    public async Task<WorldChunkMetadata> UpdateToReadyAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion,
        string checksum)
    {
        const string sql = @"
            UPDATE world_chunks
            SET status = @status, checksum = @checksum
            WHERE chunk_x = @chunkX 
              AND chunk_z = @chunkZ 
              AND layer = @layer
              AND resolution = @resolution
              AND world_version = @worldVersion
            RETURNING chunk_x, chunk_z, layer, resolution, world_version, 
                      s3_key, checksum, status, generated_at";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersion", worldVersion);
        command.Parameters.AddWithValue("@checksum", checksum);
        command.Parameters.AddWithValue("@status", ChunkStatus.Ready);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"Chunk ({chunkX}, {chunkZ}) layer={layer} resolution={resolution} version={worldVersion} not found");
        }

        return new WorldChunkMetadata
        {
            ChunkX = reader.GetInt32(0),
            ChunkZ = reader.GetInt32(1),
            Layer = reader.GetString(2),
            Resolution = reader.GetInt32(3),
            WorldVersion = reader.GetString(4),
            S3Key = reader.GetString(5),
            Checksum = reader.GetString(6),
            Status = reader.GetString(7),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(8)
        };
    }

    public async Task<WorldChunkMetadata?> GetChunkAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion)
    {
        const string sql = @"
            SELECT chunk_x, chunk_z, layer, resolution, world_version, 
                   s3_key, checksum, status, generated_at
            FROM world_chunks
            WHERE chunk_x = @chunkX 
              AND chunk_z = @chunkZ 
              AND layer = @layer
              AND resolution = @resolution
              AND world_version = @worldVersion";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersion", worldVersion);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new WorldChunkMetadata
        {
            ChunkX = reader.GetInt32(0),
            ChunkZ = reader.GetInt32(1),
            Layer = reader.GetString(2),
            Resolution = reader.GetInt32(3),
            WorldVersion = reader.GetString(4),
            S3Key = reader.GetString(5),
            Checksum = reader.GetString(6),
            Status = reader.GetString(7),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(8)
        };
    }

    public async Task<bool> IsChunkReadyAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion)
    {
        var chunk = await GetChunkAsync(chunkX, chunkZ, layer, resolution, worldVersion);
        return chunk != null && chunk.Status == ChunkStatus.Ready;
    }
}
