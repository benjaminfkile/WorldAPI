using Microsoft.AspNetCore.Mvc;
using WorldApi.World;

namespace WorldApi.Controllers;

[ApiController]
[Route("api/world/chunks")]
public class WorldChunksController : ControllerBase
{
    [HttpGet("{chunkX:int}/{chunkZ:int}")]
    public IActionResult GetChunk(int chunkX, int chunkZ)
    {
        var chunk = ChunkGenerator.Generate(chunkX, chunkZ);
        return Ok(chunk);
    }
}
