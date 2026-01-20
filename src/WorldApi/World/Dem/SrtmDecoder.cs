using System.Buffers.Binary;

namespace WorldApi.World.Dem;

public static class SrtmDecoder
{
    private const int Srtm3Size = 1201;
    private const int Srtm1Size = 3601;
    private const int Srtm3ByteCount = Srtm3Size * Srtm3Size * 2; // 1201×1201×2 = 2,884,802 bytes
    private const int Srtm1ByteCount = Srtm1Size * Srtm1Size * 2; // 3601×3601×2 = 25,934,402 bytes

    public static (short[] elevations, int width, int height) Decode(byte[] data)
    {
        // Determine grid size from data length
        int gridSize;
        if (data.Length == Srtm3ByteCount)
        {
            gridSize = Srtm3Size;
        }
        else if (data.Length == Srtm1ByteCount)
        {
            gridSize = Srtm1Size;
        }
        else
        {
            throw new ArgumentException(
                $"Invalid SRTM tile size. Expected {Srtm3ByteCount} bytes (SRTM3: 1201×1201) or {Srtm1ByteCount} bytes (SRTM1: 3601×3601), got {data.Length} bytes",
                nameof(data));
        }

        var elevations = new short[gridSize * gridSize];

        for (int i = 0; i < elevations.Length; i++)
        {
            int byteIndex = i * 2;
            // Read big-endian signed 16-bit integer
            elevations[i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(byteIndex, 2));
        }

        return (elevations, gridSize, gridSize);
    }
}
