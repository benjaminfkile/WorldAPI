using Amazon.S3.Model;

namespace WorldApi.World;

public interface ITerrainChunkReader
{
    Task<TerrainChunk> ReadAsync(int chunkX, int chunkZ, int resolution, string worldVersion);
    
    /// <summary>
    /// Gets S3 object response for streaming directly to HTTP response.
    /// Caller is responsible for disposing the response.
    /// </summary>
    Task<GetObjectResponse> GetStreamAsync(int chunkX, int chunkZ, int resolution, string worldVersion);
}
