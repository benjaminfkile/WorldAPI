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

        // Download compressed content
        await using Stream compressedStream = await response.Content.ReadAsStreamAsync();
        
        // Decompress gzip to raw .hgt
        await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        await gzipStream.CopyToAsync(memoryStream);

        return memoryStream.ToArray();
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
