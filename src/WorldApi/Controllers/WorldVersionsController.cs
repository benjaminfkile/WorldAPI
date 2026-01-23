using Microsoft.AspNetCore.Mvc;
using WorldApi.Configuration;

namespace WorldApi.Controllers;

[ApiController]
[Route("api/world-versions")]
public class WorldVersionsController : ControllerBase
{
    private readonly IWorldVersionCache _versionCache;
    private readonly ILogger<WorldVersionsController> _logger;

    public WorldVersionsController(
        IWorldVersionCache versionCache,
        ILogger<WorldVersionsController> logger)
    {
        _versionCache = versionCache;
        _logger = logger;
    }

    /// <summary>
    /// Get all active world versions.
    /// Returns a list of active versions that clients can use for terrain chunk requests.
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
            
            return Ok(new
            {
                versions = versions.Select(v => new
                {
                    v.Id,
                    v.Version,
                    v.IsActive
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active world versions from cache");
            return StatusCode(500, new { error = "Failed to retrieve active world versions" });
        }
    }
}
