namespace WorldApi.World.Dem;

/// <summary>
/// Computes the expected SRTM tile filename from geographic coordinates.
/// Used for deterministic tile naming in lazy-fetch scenarios.
/// </summary>
public static class SrtmTileNameCalculator
{
    /// <summary>
    /// Computes the SRTM tile name for a given latitude and longitude.
    /// Format: {N|S}{lat}{E|W}{lon}
    /// Example: (46.5, -113.2) → "N46W113"
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (-90 to 90)</param>
    /// <param name="longitude">Longitude in decimal degrees (-180 to 180)</param>
    /// <returns>SRTM tile name without extension (e.g., "N46W113")</returns>
    public static string Calculate(double latitude, double longitude)
    {
        // Use floor to get the southwest corner of the tile
        int latFloor = (int)Math.Floor(latitude);
        int lonFloor = (int)Math.Floor(longitude);

        // Determine N/S prefix
        char latPrefix = latFloor >= 0 ? 'N' : 'S';
        int latValue = Math.Abs(latFloor);

        // Determine E/W prefix
        char lonPrefix = lonFloor >= 0 ? 'E' : 'W';
        int lonValue = Math.Abs(lonFloor);

        // Format: {N|S}{lat}{E|W}{lon} with longitude zero-padded to 3 digits
        return $"{latPrefix}{latValue:D2}{lonPrefix}{lonValue:D3}";
    }
}
