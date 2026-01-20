namespace WorldApi.World.Chunks;

public sealed record TerrainChunk
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required int Resolution { get; init; }
    public required float[] Heights { get; init; }
    public required double MinElevation { get; init; }
    public required double MaxElevation { get; init; }
}
