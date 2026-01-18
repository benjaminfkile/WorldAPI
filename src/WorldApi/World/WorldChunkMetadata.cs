namespace WorldApi.World;

public enum ChunkStatus
{
    NotFound,
    Pending,
    Ready,
    Failed
}

public sealed class WorldChunkMetadata
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required string Layer { get; init; }
    public required int Resolution { get; init; }
    public required string WorldVersion { get; init; }
    public required string S3Key { get; init; }
    public required string Checksum { get; init; }
    public required ChunkStatus Status { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}
