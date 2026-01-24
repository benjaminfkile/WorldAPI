using Microsoft.AspNetCore.Mvc;
using WorldApi.Configuration;
using Microsoft.Extensions.Options;
using WorldApi.World.Config;

namespace WorldApi.Controllers;

[ApiController]
[Route("api/world-versions")]
public class WorldVersionsController : ControllerBase
{
    private readonly IWorldVersionCache _versionCache;
    private readonly ILogger<WorldVersionsController> _logger;
    private readonly IOptions<WorldConfig> _worldConfig;

    public WorldVersionsController(
        IWorldVersionCache versionCache,
        ILogger<WorldVersionsController> logger,
        IOptions<WorldConfig> worldConfig)
    {
        _versionCache = versionCache;
        _logger = logger;
        _worldConfig = worldConfig;
    }

    /// <summary>
    /// Get all active world versions.
    /// Returns a list of active versions that clients can use for terrain chunk requests,
    /// plus the immutable world contract (anchor point, chunk size, conversion parameters).
    /// Data is loaded from in-memory cache (not from database).
    /// </summary>
    [HttpGet("active")]
    public IActionResult GetActiveVersions()
    {
        try
        {
            // Cache lookup is synchronous and zero-latency (no database access)
            var versions = _versionCache.GetActiveWorldVersions();
            
            if (versions.Count == 0)
            {
                _logger.LogWarning("No active world versions found in cache");
                return StatusCode(503, new { error = "No active world versions available" });
            }

            _logger.LogInformation("Retrieved {VersionCount} active world versions from cache", versions.Count);
            
            var config = _worldConfig.Value;
            
            return Ok(new
            {
                versions = versions.Select(v => new
                {
                    v.Id,
                    v.Version,
                    v.IsActive
                }).ToList(),
                worldContract = new
                {
                    origin = new
                    {
                        latitude = config.Origin.Latitude,
                        longitude = config.Origin.Longitude
                    },
                    chunkSizeMeters = config.ChunkSizeMeters,
                    metersPerDegreeLatitude = config.MetersPerDegreeLatitude,
                    immutable = true,
                    description = "This world contract defines the immutable geographic anchoring and spatial parameters for all worlds in this deployment."
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active world versions from cache");
            return StatusCode(500, new { error = "Failed to retrieve active world versions" });
        }
    }

    /// <summary>
    /// Get the world contract for a specific version.
    /// Returns immutable world-space configuration including origin coordinates,
    /// chunk size, and geographic conversion parameters.
    /// 
    /// This contract is the same for all versions and is immutable per deployment.
    /// Clients use this to anchor world-space to real-world latitude/longitude
    /// and to understand the chunk size and geographic resolution.
    /// </summary>
    [HttpGet("{version}/contract")]
    public IActionResult GetWorldContract(string version)
    {
        try
        {
            var versions = _versionCache.GetActiveWorldVersions();
            
            if (!versions.Any(v => v.Version == version))
            {
                _logger.LogWarning("World contract requested for inactive or non-existent version: {Version}", version);
                return NotFound(new { error = $"World version '{version}' not found or is not active" });
            }

            var config = _worldConfig.Value;
            
            _logger.LogInformation("Returned world contract for version: {Version}", version);

            return Ok(new
            {
                version = version,
                origin = new
                {
                    latitude = config.Origin.Latitude,
                    longitude = config.Origin.Longitude
                },
                chunkSizeMeters = config.ChunkSizeMeters,
                metersPerDegreeLatitude = config.MetersPerDegreeLatitude,
                immutable = true,
                description = "This world contract defines the immutable geographic anchoring and spatial parameters for the world."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving world contract for version: {Version}", version);
            return StatusCode(500, new { error = "Failed to retrieve world contract" });
        }
    }
}
