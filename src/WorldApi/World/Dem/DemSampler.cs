using WorldApi.World.Chunks;

namespace WorldApi.World.Dem;

public static class DemSampler
{
    private const short MissingDataValue = -32768;

    public static double SampleElevation(double latitude, double longitude, SrtmTileData tile)
    {
        // Convert lat/lon to fractional grid coordinates
        var gridCoords = SrtmGridMapper.ToGridCoordinates(tile, latitude, longitude);

        // Clamp to valid grid range
        double clampedX = Math.Clamp(gridCoords.X, 0, tile.Width - 1);
        double clampedY = Math.Clamp(gridCoords.Y, 0, tile.Height - 1);

        // Get integer and fractional parts
        int x0 = (int)Math.Floor(clampedX);
        int y0 = (int)Math.Floor(clampedY);
        int x1 = Math.Min(x0 + 1, tile.Width - 1);
        int y1 = Math.Min(y0 + 1, tile.Height - 1);

        double fx = clampedX - x0;
        double fy = clampedY - y0;

        // Locate the surrounding four samples (row-major order)
        int index00 = y0 * tile.Width + x0;
        int index10 = y0 * tile.Width + x1;
        int index01 = y1 * tile.Width + x0;
        int index11 = y1 * tile.Width + x1;

        short z00 = tile.Elevations[index00];
        short z10 = tile.Elevations[index10];
        short z01 = tile.Elevations[index01];
        short z11 = tile.Elevations[index11];

        // If any sample is missing data, return missing
        if (z00 == MissingDataValue || z10 == MissingDataValue ||
            z01 == MissingDataValue || z11 == MissingDataValue)
        {
            return MissingDataValue;
        }

        // Perform bilinear interpolation
        return BilinearInterpolation.Interpolate(z00, z10, z01, z11, fx, fy);
    }
}
