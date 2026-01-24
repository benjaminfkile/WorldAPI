using Microsoft.AspNetCore.Mvc;
using WorldApi.World.Dem;

namespace WorldApi.Controllers;

/// <summary>
/// DEM Status API endpoints for readiness gating.
/// 
/// Clients use this endpoint to:
/// 1. Check if a DEM tile is ready for a region
/// 2. Learn when to enable chunk loading
/// 3. Poll for tile readiness during downloads
/// </summary>
[ApiController]
[Route("api/world/{worldVersion}/dem")]
public sealed class DemStatusController : ControllerBase
{
    private readonly DemStatusService _demStatusService;
    private readonly ILogger<DemStatusController> _logger;

    public DemStatusController(
        DemStatusService demStatusService,
        ILogger<DemStatusController> logger)
    {
        _demStatusService = demStatusService ?? throw new ArgumentNullException(nameof(demStatusService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get DEM tile status for a geographic region.
    /// 
    /// Query Parameters:
    /// - lat: Latitude in decimal degrees (-90 to 90)
    /// - lon: Longitude in decimal degrees (-180 to 180)
    /// 
    /// Response:
    /// {
    ///   "tileKey": "N46W113",
    ///   "status": "ready | missing | downloading | failed",
    ///   "lastError": "error message (only if status=failed)"
    /// }
    /// 
    /// Status Meanings:
    /// - "ready": DEM tile is available in S3, chunk generation can proceed
    /// - "missing": DEM tile not yet downloaded, download has been enqueued
    /// - "downloading": DEM tile is currently being downloaded
    /// - "failed": DEM tile download failed, manual intervention may be needed
    /// 
    /// Client Behavior:
    /// - If "ready": Enable chunk loading for this region
    /// - If "missing" or "downloading": Poll again after a delay (e.g., 5-10 seconds)
    /// - If "failed": Show user error and retry later or try different region
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetDemStatus(
        string worldVersion,
        [FromQuery] double lat,
        [FromQuery] double lon)
    {
        try
        {
            // Validate coordinates
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            {
                _logger.LogWarning(
                    "Invalid coordinates for DEM status: worldVersion={WorldVersion}, lat={Lat}, lon={Lon}",
                    worldVersion, lat, lon);

                return BadRequest(new
                {
                    error = "Invalid coordinates",
                    details = "Latitude must be between -90 and 90, longitude between -180 and 180"
                });
            }

            // Get DEM tile status
            var status = await _demStatusService.GetStatusAsync(worldVersion, lat, lon);

            _logger.LogInformation(
                "DEM status retrieved: worldVersion={WorldVersion}, tileKey={TileKey}, status={Status}",
                worldVersion, status.TileKey, status.Status);

            return Ok(new
            {
                tileKey = status.TileKey,
                status = status.Status,
                lastError = status.LastError // null if not failed
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "DEM status request failed: {Message}", ex.Message);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DEM status endpoint");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
