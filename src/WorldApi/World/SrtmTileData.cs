namespace WorldApi.World;

public sealed record SrtmTileData
{
    public required double MinLatitude { get; init; }
    public required double MaxLatitude { get; init; }
    public required double MinLongitude { get; init; }
    public required double MaxLongitude { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required short[] Elevations { get; init; }
}
