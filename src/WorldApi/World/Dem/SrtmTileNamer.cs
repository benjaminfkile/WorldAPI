namespace WorldApi.World.Dem;

/// <summary>
/// Computes deterministic SRTM tile filenames from coordinates.
/// Inverse operation of SrtmFilenameParser.
/// 
/// SRTM naming convention: {N|S}{lat}{E|W}{lon}.hgt
/// - N/S: North/South (latitude)
/// - lat: Truncate latitude towards zero (0-90)
/// - E/W: East/West (longitude)
/// - lon: Truncate longitude towards zero (0-180)
/// 
/// Note: Uses truncation (casting to int) not floor, so -113.2 → W113, not W114
/// </summary>
public static class SrtmTileNamer
{
    /// <summary>
    /// Compute the SRTM tile filename for a given coordinate.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (-90 to 90)</param>
    /// <param name="longitude">Longitude in decimal degrees (-180 to 180)</param>
    /// <returns>SRTM tile filename (e.g., "N46W113.hgt")</returns>
    /// <exception cref="ArgumentOutOfRangeException">If coordinates are outside valid ranges</exception>
    public static string ComputeTileName(double latitude, double longitude)
    {
        // Validate ranges
        if (latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90");
        
        if (longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180");

        // Floor the coordinates (round down towards negative infinity)
        // This matches SRTM tile convention
        int latFloor = (int)Math.Floor(latitude);
        int lonFloor = (int)Math.Floor(longitude);

        // Determine N/S and E/W directions
        char latDir = latFloor >= 0 ? 'N' : 'S';
        char lonDir = lonFloor >= 0 ? 'E' : 'W';

        // Convert to absolute values for filename
        int latAbs = Math.Abs(latFloor);
        int lonAbs = Math.Abs(lonFloor);

        // Format: {N|S}{lat}{E|W}{lon}.hgt
        // Examples: N46W114.hgt, S13E044.hgt, N00E000.hgt
        return $"{latDir}{latAbs:D2}{lonDir}{lonAbs:D3}.hgt";
    }

    /// <summary>
    /// Compute the S3 key path for a DEM tile.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees</param>
    /// <param name="longitude">Longitude in decimal degrees</param>
    /// <returns>S3 key path (e.g., "dem/srtm/N46W113.hgt")</returns>
    public static string ComputeS3Key(double latitude, double longitude)
    {
        var tileName = ComputeTileName(latitude, longitude);
        return $"dem/srtm/{tileName}";
    }
}
