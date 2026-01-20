using WorldApi.World.Coordinates;
using WorldApi.World.Dem;

namespace WorldApi.World.Chunks;

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
        // Generate (resolution + 1) × (resolution + 1) grid for overlapping edges
        int gridSize = resolution + 1;
        System.Diagnostics.Debug.WriteLine(
            $"[ChunkHeightSampler] START: chunk=({chunkX},{chunkZ}) resolution={resolution} gridSize={gridSize} expected={gridSize * gridSize}");
        var heights = new double[gridSize * gridSize];
        
        // cellSize = chunkSizeMeters / resolution ensures edges align
        // Last column of chunk (x,z) == first column of chunk (x+1,z)
        double cellSize = chunkSizeMeters / resolution;

        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                // CRITICAL: Calculate world coordinates in terms of cell indices from world origin
                // This ensures bit-exact identical coordinates for shared edges between adjacent chunks
                // 
                // Instead of: worldX = chunkX * chunkSizeMeters + x * cellSize
                // We use:     worldX = (chunkX * resolution + x) * cellSize
                //
                // This way, the right edge of chunk (0,0) at x=resolution gives:
                //   worldX = (0 * resolution + resolution) * cellSize = resolution * cellSize
                // And the left edge of chunk (1,0) at x=0 gives:
                //   worldX = (1 * resolution + 0) * cellSize = resolution * cellSize
                // Which are EXACTLY the same calculation, avoiding floating-point drift
                
                int globalCellX = chunkX * resolution + x;
                int globalCellZ = chunkZ * resolution + z;
                
                double worldX = globalCellX * cellSize;
                double worldZ = globalCellZ * cellSize;

                // Convert world meters to lat/lon using consistent meters-per-degree conversion
                var latLon = coordinateService.WorldMetersToLatLon(worldX, worldZ);

                // Sample elevation from DEM tile
                double elevation = DemSampler.SampleElevation(latLon.Latitude, latLon.Longitude, tile);

                // Store in row-major order
                int index = z * gridSize + x;
                heights[index] = elevation;
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[ChunkHeightSampler] END: chunk=({chunkX},{chunkZ}) resolution={resolution} returned_length={heights.Length}");
        return heights;
    }
}
