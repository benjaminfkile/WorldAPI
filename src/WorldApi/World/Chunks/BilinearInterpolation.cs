namespace WorldApi.World.Chunks;

public static class BilinearInterpolation
{
    public static double Interpolate(short z00, short z10, short z01, short z11, double fx, double fy)
    {
        // Interpolate along x-axis for top and bottom rows
        double zTop = z00 * (1.0 - fx) + z10 * fx;
        double zBottom = z01 * (1.0 - fx) + z11 * fx;

        // Interpolate along y-axis
        return zTop * (1.0 - fy) + zBottom * fy;
    }
}
