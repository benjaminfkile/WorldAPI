namespace WorldApi.World.Dem;

/// <summary>
/// Abstraction for public SRTM tile retrieval.
/// Enables testing and alternative implementations.
/// </summary>
public interface IPublicSrtmClient
{
    /// <summary>
    /// Fetch a single SRTM tile from the public bucket by tile name.
    /// </summary>
    /// <param name="tileName">Tile key in format {N|S}{lat}{E|W}{lon}.hgt (e.g., N46W113.hgt)</param>
    /// <returns>Tile data as byte array</returns>
    /// <exception cref="TileNotFoundException">If the tile does not exist in public SRTM (404)</exception>
    Task<byte[]> FetchTileAsync(string tileName);
}
