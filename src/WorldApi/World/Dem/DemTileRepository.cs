using Npgsql;

namespace WorldApi.World.Dem;

/// <summary>
/// Repository for DEM tile status tracking in PostgreSQL.
/// Ensures only one DEM download occurs per (world_version_id, tile_key).
/// Uses shared NpgsqlDataSource for connection pooling.
/// </summary>
public sealed class DemTileRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public DemTileRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <summary>
    /// Look up world_version_id from world_versions table by version string.
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

    /// <summary>
    /// Get DEM tile status. If no row exists, create one with status='missing' and return it.
    /// This is idempotent - concurrent calls will result in only one row existing.
    /// </summary>
    public async Task<DemTileStatus> GetOrCreateMissingAsync(string worldVersion, string tileKey)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        // Try to insert as 'missing' (idempotent via unique constraint)
        const string insertSql = @"
            INSERT INTO dem_tiles (world_version_id, tile_key, status)
            VALUES (@worldVersionId, @tileKey, 'missing')
            ON CONFLICT (world_version_id, tile_key) DO UPDATE SET updated_at = NOW()
            RETURNING id, world_version_id, tile_key, status, s3_key, last_error, created_at, updated_at";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var insertCommand = new NpgsqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        insertCommand.Parameters.AddWithValue("@tileKey", tileKey);

        await using var insertReader = await insertCommand.ExecuteReaderAsync();
        
        if (await insertReader.ReadAsync())
        {
            return ReadDemTileStatus(insertReader);
        }

        throw new InvalidOperationException($"Failed to insert or retrieve DEM tile status for {tileKey}");
    }

    /// <summary>
    /// Get current DEM tile status by tile_key and world_version.
    /// Returns null if no row exists.
    /// </summary>
    public async Task<DemTileStatus?> GetStatusAsync(string worldVersion, string tileKey)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            return null;
        }

        const string sql = @"
            SELECT id, world_version_id, tile_key, status, s3_key, last_error, created_at, updated_at
            FROM dem_tiles
            WHERE world_version_id = @worldVersionId AND tile_key = @tileKey
            LIMIT 1";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@tileKey", tileKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadDemTileStatus(reader);
        }

        return null;
    }

    /// <summary>
    /// Atomically transition a DEM tile from 'missing' to 'downloading'.
    /// Returns true if transition succeeded, false if status was not 'missing'.
    /// This ensures only one worker claims a tile for download.
    /// </summary>
    public async Task<bool> TryClaimForDownloadAsync(string worldVersion, string tileKey)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        const string sql = @"
            UPDATE dem_tiles
            SET status = 'downloading'
            WHERE world_version_id = @worldVersionId 
              AND tile_key = @tileKey 
              AND status = 'missing'
            RETURNING id";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@tileKey", tileKey);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync(); // True if 1 row was updated, false if 0 rows
    }

    /// <summary>
    /// Mark DEM tile as ready. Sets status='ready' and s3_key, clears last_error.
    /// </summary>
    public async Task MarkReadyAsync(string worldVersion, string tileKey, string s3Key)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        const string sql = @"
            UPDATE dem_tiles
            SET status = 'ready', s3_key = @s3Key, last_error = NULL
            WHERE world_version_id = @worldVersionId AND tile_key = @tileKey";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@tileKey", tileKey);
        command.Parameters.AddWithValue("@s3Key", s3Key);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mark DEM tile as failed. Sets status='failed' and last_error message.
    /// </summary>
    public async Task MarkFailedAsync(string worldVersion, string tileKey, string errorMessage)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database");
        }

        const string sql = @"
            UPDATE dem_tiles
            SET status = 'failed', last_error = @errorMessage
            WHERE world_version_id = @worldVersionId AND tile_key = @tileKey";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@tileKey", tileKey);
        command.Parameters.AddWithValue("@errorMessage", errorMessage);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get all tiles with a specific status for a world version.
    /// Used by workers to find tiles to process.
    /// </summary>
    public async Task<List<DemTileStatus>> GetAllByStatusAsync(string worldVersion, string status, int limit = 100)
    {
        // Resolve world_version_id from string
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null)
        {
            throw new InvalidOperationException($"World version '{worldVersion}' not found in database. Cannot query tiles.");
        }

        const string sql = @"
            SELECT id, world_version_id, tile_key, status, s3_key, last_error, created_at, updated_at
            FROM dem_tiles
            WHERE world_version_id = @worldVersionId AND status = @status
            ORDER BY created_at ASC
            LIMIT @limit";

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@worldVersionId", worldVersionId.Value);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<DemTileStatus>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadDemTileStatus(reader));
        }

        return results;
    }

    /// <summary>
    /// Parse a reader row into DemTileStatus record.
    /// </summary>
    private static DemTileStatus ReadDemTileStatus(NpgsqlDataReader reader)
    {
        return new DemTileStatus
        {
            Id = reader.GetInt64(0),
            WorldVersionId = reader.GetInt64(1),
            TileKey = reader.GetString(2),
            Status = reader.GetString(3),
            S3Key = reader.IsDBNull(4) ? null : reader.GetString(4),
            LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(6),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(7)
        };
    }
}

/// <summary>
/// DEM tile status record from database.
/// </summary>
public record DemTileStatus
{
    public long Id { get; init; }
    public long WorldVersionId { get; init; }
    public string TileKey { get; init; } = string.Empty;
    public string Status { get; init; } = "missing"; // missing, downloading, ready, failed
    public string? S3Key { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
