using Microsoft.Extensions.Options;

// STEP 6.2 — World ↔ Earth Coordinate Mapping
//
// Implement deterministic world-to-earth coordinate mapping.
//
// REQUIREMENTS:
// - Read origin latitude/longitude from WorldConfig (IOptions<WorldConfig>)
// - Read chunk size from WorldConfig
// - 1 world unit = 1 meter
// - chunk size = ChunkSizeMeters
// - Coordinate system:
//     +X = east
//     +Z = north
// - Flat-earth approximation ONLY
// - Use:
//     1 degree latitude ≈ 111,320 meters
//     1 degree longitude ≈ 111,320 * cos(origin latitude)
//
// DO NOT:
// - Use AWS SDK
// - Use S3
// - Access environment variables
// - Add projections or globe math
// - Modify configuration values
//
// Implement:
// - A LatLon record or struct
// - A WorldCoordinateService class
// - Method:
//     LatLon GetChunkOriginLatLon(int chunkX, int chunkZ)

public readonly record struct LatLon(double Latitude, double Longitude);

public sealed class WorldCoordinateService
{
    private readonly WorldConfig _config;
    private readonly double _metersPerDegreeLongitude;

    public WorldCoordinateService(IOptions<WorldConfig> options)
    {
        _config = options.Value;
        _metersPerDegreeLongitude = 111320 * Math.Cos(DegreesToRadians(_config.Origin.Latitude));
    }

    public LatLon GetChunkOriginLatLon(int chunkX, int chunkZ)
    {
        double chunkSize = _config.ChunkSizeMeters;

        double originLatitude = _config.Origin.Latitude + (chunkZ * chunkSize) / 111320;
        double originLongitude = _config.Origin.Longitude + (chunkX * chunkSize) / _metersPerDegreeLongitude;

        return new LatLon(originLatitude, originLongitude);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180);
    }
}