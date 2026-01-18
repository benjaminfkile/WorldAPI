namespace WorldApi.World;

public sealed class WorldChunkMetadata
{
    public required int ChunkX { get; init; }
    public required int ChunkZ { get; init; }
    public required string Layer { get; init; }
    public required int Resolution { get; init; }
    public required string WorldVersion { get; init; }
    public required string S3Key { get; init; }
    public required string Checksum { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}

public static class ChunkStatus
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
