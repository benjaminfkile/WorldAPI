using System.Collections.Immutable;

namespace WorldApi.Configuration;

/// <summary>
/// In-memory cache of active world versions loaded at application startup.
/// 
/// Responsibilities:
/// - Store versions in an immutable, thread-safe data structure
/// - Provide synchronous, zero-latency access to cached versions
/// - Eliminate all per-request database queries for world version lookup
/// 
/// Design principles:
/// - PostgreSQL is queried ONCE at startup only (in Program.cs before DI finalization)
/// - All runtime access is in-memory (synchronous, no I/O)
/// - Data structure is immutable ImmutableList (thread-safe without locks)
/// - Cache is initialized before the app starts handling HTTP requests
/// </summary>
public interface IWorldVersionCache
{
    /// <summary>
    /// Get a world version by its string identifier from the in-memory cache.
    /// Returns null if not found (synchronous, zero-latency lookup).
    /// </summary>
    WorldVersionInfo? GetWorldVersion(string version);

    /// <summary>
    /// Get all active world versions from the in-memory cache.
    /// Returns immutable list (thread-safe, no copying needed).
    /// </summary>
    IReadOnlyList<WorldVersionInfo> GetActiveWorldVersions();

    /// <summary>
    /// Check if a world version exists and is active (synchronous cache lookup).
    /// </summary>
    bool IsWorldVersionActive(string version);

    /// <summary>
    /// Represents a world version record.
    /// </summary>
    public sealed class WorldVersionInfo
    {
        public required long Id { get; init; }
        public required string Version { get; init; }
        public required bool IsActive { get; init; }
    }
}

public sealed class WorldVersionCache : IWorldVersionCache
{
    private readonly ImmutableList<IWorldVersionCache.WorldVersionInfo> _versions;
    private readonly ILogger<WorldVersionCache> _logger;

    /// <summary>
    /// Constructor called during startup (in Program.cs) with pre-loaded versions from database.
    /// Immutable list ensures thread-safe access without locks or copies.
    /// </summary>
    public WorldVersionCache(IEnumerable<IWorldVersionCache.WorldVersionInfo> versions, ILogger<WorldVersionCache> logger)
    {
        _versions = ImmutableList.CreateRange(versions);
        _logger = logger;

        if (_versions.Count == 0)
        {
            _logger.LogWarning("⚠️ No active world versions found in cache. All terrain requests will fail validation.");
        }
        else
        {
            var versionStrings = string.Join(", ", _versions.Select(v => $"'{v.Version}' (id={v.Id})"));
            _logger.LogInformation(
                "✓ World version cache initialized with {Count} active version(s): {Versions}",
                _versions.Count,
                versionStrings);
        }
    }

    public IWorldVersionCache.WorldVersionInfo? GetWorldVersion(string version)
    {
        // Synchronous lookup in immutable list (no database access)
        return _versions.FirstOrDefault(v => v.Version == version);
    }

    public IReadOnlyList<IWorldVersionCache.WorldVersionInfo> GetActiveWorldVersions()
    {
        // Return the immutable list directly (no copy needed, thread-safe)
        return _versions;
    }

    public bool IsWorldVersionActive(string version)
    {
        var info = GetWorldVersion(version);
        return info?.IsActive ?? false;
    }
}
