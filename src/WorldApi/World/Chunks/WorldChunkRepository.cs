using Npgsql;
using System.Data;

namespace WorldApi.World.Chunks;

/// <summary>
/// Repository for world chunk metadata (stored in PostgreSQL).
/// 
/// Uses shared NpgsqlDataSource for connection pooling to prevent connection storms.
/// All database operations go through the pool, which enforces MaxPoolSize limits.
/// </summary>
public sealed class WorldChunkRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public WorldChunkRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    private static string StatusToString(ChunkStatus status) => status switch
    {
        ChunkStatus.Pending => "pending",
        ChunkStatus.Ready => "ready",
        ChunkStatus.Failed => "failed",
        _ => throw new ArgumentException($"Unknown status: {status}")
    };

    private static ChunkStatus StringToStatus(string status) => status switch
    {
        "pending" => ChunkStatus.Pending,
        "ready" => ChunkStatus.Ready,
        "failed" => ChunkStatus.Failed,
        _ => throw new ArgumentException($"Unknown status string: {status}")
    };

    /// <summary>
    /// Look up world_version_id from world_versions table by version string.
    /// Uses the data source pool to acquire a connection.
    /// </summary>
    private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
    {
        const string sql = @"
            SELECT id FROM world_versions 
            WHERE ""version"" = @version 
            LIMIT 1";

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", worldVersion);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return reader.GetInt64(0);
        }

        return null;
    }

    public async Task<WorldChunkMetadata> InsertPendingAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion,
        string s3Key)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        const string sql = @"
            INSERT INTO world_chunks (
                chunk_x, chunk_z, layer, resolution, world_version_id,
                s3_key, checksum, status, generated_at
            )
            VALUES (
                @chunkX, @chunkZ, @layer, @resolution, @worldVersionId,
                @s3Key, '', @status, @generatedAt
            )
            RETURNING chunk_x, chunk_z, layer, resolution, 
                      s3_key, checksum, status, generated_at";

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@s3Key", s3Key);
        command.Parameters.AddWithValue("@status", StatusToString(ChunkStatus.Pending));
        command.Parameters.AddWithValue("@generatedAt", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new WorldChunkMetadata
        {
            ChunkX = reader.GetInt32(0),
            ChunkZ = reader.GetInt32(1),
            Layer = reader.GetString(2),
            Resolution = reader.GetInt32(3),
            WorldVersion = worldVersion,
            S3Key = reader.GetString(4),
            Checksum = reader.GetString(5),
            Status = StringToStatus(reader.GetString(6)),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(7)
        };
    }

    /// <summary>
    /// Upsert chunk metadata: Insert as ready or update if already exists.
    /// Single operation optimized for performance - no pending state.
    /// Uses data source pool for connection acquisition.
    /// </summary>
    public async Task<WorldChunkMetadata> UpsertReadyAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion,
        string s3Key,
        string checksum)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        const string sql = @"
            INSERT INTO world_chunks (
                chunk_x, chunk_z, layer, resolution, world_version_id, 
                s3_key, checksum, status, generated_at
            )
            VALUES (
                @chunkX, @chunkZ, @layer, @resolution, @worldVersionId,
                @s3Key, @checksum, @status, @generatedAt
            )
            RETURNING chunk_x, chunk_z, layer, resolution, 
                      s3_key, checksum, status, generated_at";

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@s3Key", s3Key);
        command.Parameters.AddWithValue("@checksum", checksum);
        command.Parameters.AddWithValue("@status", StatusToString(ChunkStatus.Ready));
        command.Parameters.AddWithValue("@generatedAt", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new WorldChunkMetadata
        {
            ChunkX = reader.GetInt32(0),
            ChunkZ = reader.GetInt32(1),
            Layer = reader.GetString(2),
            Resolution = reader.GetInt32(3),
            WorldVersion = worldVersion,
            S3Key = reader.GetString(4),
            Checksum = reader.GetString(5),
            Status = StringToStatus(reader.GetString(6)),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(7)
        };
    }

    public async Task<WorldChunkMetadata?> GetChunkAsync(
        int chunkX,
        int chunkZ,
        string layer,
        int resolution,
        string worldVersion)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            return null;
        }

        const string sql = @"
            SELECT chunk_x, chunk_z, layer, resolution, 
                   s3_key, checksum, status, generated_at
            FROM world_chunks
            WHERE chunk_x = @chunkX 
              AND chunk_z = @chunkZ 
              AND layer = @layer
              AND resolution = @resolution
              AND world_version_id = @worldVersionId";

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@chunkX", chunkX);
        command.Parameters.AddWithValue("@chunkZ", chunkZ);
        command.Parameters.AddWithValue("@layer", layer);
        command.Parameters.AddWithValue("@resolution", resolution);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);

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
            WorldVersion = worldVersion,
            S3Key = reader.GetString(4),
            Checksum = reader.GetString(5),
            Status = StringToStatus(reader.GetString(6)),
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(7)
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
