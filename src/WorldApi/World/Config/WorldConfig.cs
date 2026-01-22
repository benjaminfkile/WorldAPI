namespace WorldApi.World.Config;

public sealed class WorldConfig
{
    public OriginConfig Origin { get; init; } = default!;
    public int ChunkSizeMeters { get; init; }
    public double MetersPerDegreeLatitude { get; init; }
}

public sealed class OriginConfig
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
