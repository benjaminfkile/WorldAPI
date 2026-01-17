using Microsoft.AspNetCore.Mvc;

namespace WorldApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "world-api",
            timestampUtc = DateTime.UtcNow
        });
    }
}
