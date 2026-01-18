namespace WorldApi.World;

public readonly record struct NormalizedHeights(
    float[] Heights,
    double MinElevation,
    double MaxElevation);

public static class HeightNormalizer
{
    private const double MissingDataValue = -32768.0;

    public static NormalizedHeights Normalize(double[] rawHeights)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[HeightNormalizer] START: input_length={rawHeights.Length}");
        // Find min and max elevation, excluding missing data
        double minElevation = double.MaxValue;
        double maxElevation = double.MinValue;

        foreach (double height in rawHeights)
        {
            if (height != MissingDataValue)
            {
                if (height < minElevation)
                    minElevation = height;
                if (height > maxElevation)
                    maxElevation = height;
            }
        }

        // Handle edge case where all values are missing
        if (minElevation == double.MaxValue)
        {
            minElevation = 0.0;
            maxElevation = 0.0;
        }

        // Store absolute elevation values (do NOT subtract minElevation)
        // Client can normalize for rendering with consistent global scale
        var normalizedHeights = new float[rawHeights.Length];
        for (int i = 0; i < rawHeights.Length; i++)
        {
            if (rawHeights[i] == MissingDataValue)
            {
                normalizedHeights[i] = 0.0f;
            }
            else
            {
                // Store absolute elevation in meters
                normalizedHeights[i] = (float)rawHeights[i];
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[HeightNormalizer] END: output_length={normalizedHeights.Length}");
        return new NormalizedHeights(normalizedHeights, minElevation, maxElevation);
    }
}
