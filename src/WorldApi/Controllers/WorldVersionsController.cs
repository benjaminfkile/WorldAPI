using Microsoft.AspNetCore.Mvc;
using WorldApi.Configuration;

namespace WorldApi.Controllers;

[ApiController]
[Route("api/world-versions")]
public class WorldVersionsController : ControllerBase
{
    private readonly IWorldVersionService _worldVersionService;
    private readonly ILogger<WorldVersionsController> _logger;

    public WorldVersionsController(
        IWorldVersionService worldVersionService,
        ILogger<WorldVersionsController> logger)
    {
        _worldVersionService = worldVersionService;
        _logger = logger;
    }

    /// <summary>
    /// Get all active world versions.
    /// Returns a list of active versions that clients can use for terrain chunk requests.
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveVersions()
    {
        try
        {
            var versions = await _worldVersionService.GetActiveWorldVersionsAsync();
            
            if (versions.Count == 0)
            {
                _logger.LogWarning("No active world versions found");
                return StatusCode(503, new { error = "No active world versions available" });
            }

            _logger.LogInformation("Retrieved {VersionCount} active world versions", versions.Count);
            
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
            _logger.LogError(ex, "Error retrieving active world versions");
            return StatusCode(500, new { error = "Failed to retrieve active world versions" });
        }
    }
}
