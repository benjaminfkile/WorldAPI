namespace WorldApi.World;

public sealed record DemTile
{
    public required double MinLatitude { get; init; }
    public required double MaxLatitude { get; init; }
    public required double MinLongitude { get; init; }
    public required double MaxLongitude { get; init; }
    public required string S3Key { get; init; }
}
