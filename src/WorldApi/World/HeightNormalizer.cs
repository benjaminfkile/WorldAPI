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

        // Normalize heights
        var normalizedHeights = new float[rawHeights.Length];
        for (int i = 0; i < rawHeights.Length; i++)
        {
            if (rawHeights[i] == MissingDataValue)
            {
                normalizedHeights[i] = 0.0f;
            }
            else
            {
                normalizedHeights[i] = (float)(rawHeights[i] - minElevation);
            }
        }

        return new NormalizedHeights(normalizedHeights, minElevation, maxElevation);
    }
}
