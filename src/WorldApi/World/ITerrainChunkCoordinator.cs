namespace WorldApi.World;

public interface ITerrainChunkCoordinator
{
    Task<ChunkStatus> GetChunkStatusAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain");

    Task TriggerGenerationAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,
        string layer = "terrain");
}
