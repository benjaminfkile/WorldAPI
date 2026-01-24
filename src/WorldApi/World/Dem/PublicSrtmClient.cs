using System.IO.Compression;

namespace WorldApi.World.Dem;

/// <summary>
/// Read-only HTTP client for fetching SRTM tiles from the public AWS Open Data bucket.
/// No AWS credentials required - uses anonymous HTTPS GET requests.
/// </summary>
public class PublicSrtmClient
{
    private const string BaseUrl = "https://s3.amazonaws.com/elevation-tiles-prod/skadi";
    private readonly HttpClient _httpClient;

    public PublicSrtmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Fetches and decompresses an SRTM tile from the public dataset.
    /// </summary>
    /// <param name="tileName">SRTM tile name without extension (e.g., "N27E086")</param>
    /// <returns>Decompressed .hgt file content as a byte array</returns>
    /// <exception cref="TileNotFoundException">Thrown when tile does not exist (404)</exception>
    /// <exception cref="HttpRequestException">Thrown for other HTTP errors</exception>
    public virtual async Task<byte[]> FetchAndDecompressTileAsync(string tileName)
    {
        string latFolder = tileName[..3]; // e.g., "N27" or "S13"
        string url = $"{BaseUrl}/{latFolder}/{tileName}.hgt.gz";

        HttpResponseMessage response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new TileNotFoundException(tileName, url);
        }

        response.EnsureSuccessStatusCode();

        // Download compressed content to memory first
        byte[] compressedData = await response.Content.ReadAsByteArrayAsync();
        
        // Log first 4 bytes to debug format (gzip magic is 0x1f 0x8b)
        string dataHex = compressedData.Length >= 4 
            ? $"{compressedData[0]:X2}{compressedData[1]:X2}{compressedData[2]:X2}{compressedData[3]:X2}" 
            : "TOO_SHORT";
        
        // Decompress gzip to raw .hgt
        using var compressedStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        
        try
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to decompress SRTM tile {tileName}: {ex.Message}", ex);
        }

        byte[] decompressed = decompressedStream.ToArray();
        
        if (decompressed.Length == 0)
        {
            throw new InvalidOperationException(
                $"SRTM tile {tileName} decompressed to empty data (compressed size was {compressedData.Length} bytes, first 4 bytes: {dataHex})");
        }

        return decompressed;
    }
}

/// <summary>
/// Exception thrown when an SRTM tile is not found in the public dataset.
/// This is expected for ocean regions and data voids.
/// </summary>
public sealed class TileNotFoundException : Exception
{
    public string TileName { get; }
    public string Url { get; }

    public TileNotFoundException(string tileName, string url)
        : base($"SRTM tile '{tileName}' not found at {url}")
    {
        TileName = tileName;
        Url = url;
    }
}
