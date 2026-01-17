using System.Buffers.Binary;

namespace WorldApi.World;

public static class SrtmDecoder
{
    private const int SrtmSize = 1201;
    private const int ExpectedByteCount = SrtmSize * SrtmSize * 2; // 2 bytes per sample

    public static short[] Decode(byte[] data)
    {
        if (data.Length != ExpectedByteCount)
        {
            throw new ArgumentException(
                $"Expected {ExpectedByteCount} bytes for a 1201x1201 SRTM tile, got {data.Length}",
                nameof(data));
        }

        var elevations = new short[SrtmSize * SrtmSize];

        for (int i = 0; i < elevations.Length; i++)
        {
            int byteIndex = i * 2;
            // Read big-endian signed 16-bit integer
            elevations[i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(byteIndex, 2));
        }

        return elevations;
    }
}
