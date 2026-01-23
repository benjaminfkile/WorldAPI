using Npgsql;

namespace WorldApi.Configuration;

/// <summary>
/// Service for managing world version lookups and validation.
/// 
/// Responsibilities:
/// - Look up world version from database by string identifier
/// - Validate if a world is active
/// - Convert world version string to internal world_version_id
/// - Provide world metadata (id, version, is_active status)
/// 
/// Uses shared NpgsqlDataSource for connection pooling to prevent connection storms.
/// All database operations go through the pool, which enforces MaxPoolSize limits.
/// </summary>
public interface IWorldVersionService
{
    /// <summary>
    /// Look up a world version by its string identifier.
    /// Returns null if not found.
    /// </summary>
    Task<WorldVersionInfo?> GetWorldVersionAsync(string version);

    /// <summary>
    /// Get all active world versions.
    /// Multiple versions can be active simultaneously to support parallel worlds.
    /// </summary>
    Task<IReadOnlyList<WorldVersionInfo>> GetActiveWorldVersionsAsync();

    /// <summary>
    /// Check if a world version exists and is active.
    /// Used for API validation before processing requests.
    /// </summary>
    Task<bool> IsWorldVersionActiveAsync(string version);

    /// <summary>
    /// Represents a world version record from the database.
    /// </summary>
    public sealed class WorldVersionInfo
    {
        public required long Id { get; init; }
        public required string Version { get; init; }
        public required bool IsActive { get; init; }
    }
}

public sealed class WorldVersionService : IWorldVersionService
{
    private readonly NpgsqlDataSource _dataSource;

    public WorldVersionService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<IWorldVersionService.WorldVersionInfo?> GetWorldVersionAsync(string version)
    {
        const string sql = @"
            SELECT id, version, is_active 
            FROM world_versions 
            WHERE version = @version
            LIMIT 1";

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            };
        }

        return null;
    }

    public async Task<IReadOnlyList<IWorldVersionService.WorldVersionInfo>> GetActiveWorldVersionsAsync()
    {
        const string sql = @"
            SELECT id, version, is_active 
            FROM world_versions 
            WHERE is_active = true
            ORDER BY version ASC";

        var versions = new List<IWorldVersionService.WorldVersionInfo>();

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }

        return versions;
    }

    public async Task<bool> IsWorldVersionActiveAsync(string version)
    {
        var info = await GetWorldVersionAsync(version);
        return info?.IsActive ?? false;
    }
}
