namespace WorldApi.World.Dem;

/// <summary>
/// Abstraction for local SRTM tile persistence.
/// Enables testing and alternative storage implementations.
/// </summary>
public interface ILocalSrtmPersistence
{
    /// <summary>
    /// Save a SRTM tile to local storage (S3).
    /// </summary>
    /// <param name="tileName">Tile key in format {N|S}{lat}{E|W}{lon}.hgt (e.g., N46W113.hgt)</param>
    /// <param name="tileData">Tile data as byte array</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>S3 key path where tile was saved</returns>
    Task<string> SaveTileAsync(string tileName, byte[] tileData, CancellationToken cancellationToken);
}
