namespace WorldApi.World;

public static class TerrainChunkSerializer
{
    private const byte CurrentVersion = 1;

    public static byte[] Serialize(TerrainChunk chunk)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Write version
        writer.Write(CurrentVersion);

        // Write resolution
        writer.Write((ushort)chunk.Resolution);

        // Write min/max elevation
        writer.Write(chunk.MinElevation);
        writer.Write(chunk.MaxElevation);

        // Write heights array
        foreach (float height in chunk.Heights)
        {
            writer.Write(height);
        }

        return stream.ToArray();
    }

    public static TerrainChunk Deserialize(byte[] data, int chunkX, int chunkZ)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        // Read and validate version
        byte version = reader.ReadByte();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported terrain chunk version: {version}. Expected: {CurrentVersion}");
        }

        // Read resolution
        ushort resolution = reader.ReadUInt16();

        // Read min/max elevation
        double minElevation = reader.ReadDouble();
        double maxElevation = reader.ReadDouble();

        // Read heights array
        int expectedCount = resolution * resolution;
        var heights = new float[expectedCount];
        for (int i = 0; i < expectedCount; i++)
        {
            heights[i] = reader.ReadSingle();
        }

        return new TerrainChunk
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Resolution = resolution,
            Heights = heights,
            MinElevation = minElevation,
            MaxElevation = maxElevation
        };
    }
}
