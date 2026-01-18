namespace WorldApi.World;

public static class ChunkHeightSampler
{
    public static double[] SampleHeights(
        int chunkX,
        int chunkZ,
        int resolution,
        WorldCoordinateService coordinateService,
        SrtmTileData tile,
        double chunkSizeMeters)
    {
        var heights = new double[resolution * resolution];
        double cellSize = chunkSizeMeters / (resolution - 1);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Convert local cell position to world meters
                // x=0, z=0 is the southwest corner of the chunk
                double worldX = chunkX * chunkSizeMeters + x * cellSize;
                double worldZ = chunkZ * chunkSizeMeters + z * cellSize;

                // Convert world meters to chunk origin lat/lon, then offset by cell position
                var chunkOrigin = coordinateService.GetChunkOriginLatLon(chunkX, chunkZ);
                
                // Calculate cell offset in degrees
                // worldX is east offset, worldZ is north offset from world origin
                // We need to calculate offset from chunk origin
                double eastOffsetMeters = x * cellSize;
                double northOffsetMeters = z * cellSize;

                // Convert offset to degrees using the same logic as WorldCoordinateService
                double latitude = chunkOrigin.Latitude + northOffsetMeters / 111320.0;
                double longitude = chunkOrigin.Longitude + eastOffsetMeters / (111320.0 * Math.Cos(chunkOrigin.Latitude * Math.PI / 180.0));

                // Sample elevation from DEM tile
                double elevation = DemSampler.SampleElevation(latitude, longitude, tile);

                // Store in row-major order
                int index = z * resolution + x;
                heights[index] = elevation;
            }
        }

        return heights;
    }
}
