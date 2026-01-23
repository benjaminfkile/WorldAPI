# DEM Lazy Fetch - Step 3 Implementation

**Date**: January 23, 2026  
**Step**: Public SRTM Client  
**Status**: ✅ Complete

---

## Objective

Introduce a **read-only HTTP client** for the public SRTM dataset.

### Responsibilities
- Build public fetch URL using tile name
- Download `.hgt.gz` from public SRTM bucket
- Decompress to raw `.hgt`
- Handle 404 for missing tiles
- No AWS credentials required

---

## Changes Made

### File: `src/WorldApi/World/Dem/PublicSrtmClient.cs` (NEW)

Created new HTTP client for anonymous access to public SRTM data:

**Key features:**
- Uses standard `HttpClient` (no AWS SDK required)
- Builds URLs in Skadi format: `https://s3.amazonaws.com/elevation-tiles-prod/skadi/{LAT_FOLDER}/{TILE}.hgt.gz`
- Decompresses gzip-compressed tiles using `GZipStream`
- Returns raw `.hgt` byte arrays ready for storage/parsing
- Throws `TileNotFoundException` for 404 responses
- Propagates other HTTP errors via `HttpRequestException`

**Method signature:**
```csharp
public async Task<byte[]> FetchAndDecompressTileAsync(string tileName)
```

**Exception handling:**
- `TileNotFoundException` - Custom exception for missing tiles (expected for oceans)
  - Properties: `TileName`, `Url`
  - Provides clear error message with tile name and URL
- `HttpRequestException` - For other HTTP errors (network issues, server errors)

### File: `src/WorldApi.Tests/Dem/PublicSrtmClientTests.cs` (NEW)

Created comprehensive integration tests:

**Test coverage:**
- ✅ Downloads and decompresses known-good tile (N27E086)
- ✅ Throws TileNotFoundException for missing tiles
- ✅ Builds correct URL format
- ✅ Handles southern hemisphere tiles (folder extraction)
- ✅ Verifies data is decompressed (not gzipped)
- ✅ Validates exception properties
- ✅ Works without AWS credentials (public bucket)

---

## What Worked

✅ **Build Success**: Application compiles without errors or warnings  
✅ **All Tests Pass**: 7/7 new tests passing  
✅ **All DEM Tests Pass**: 97/97 total DEM tests passing  
✅ **Public Access**: Successfully downloads from public bucket without credentials  
✅ **Decompression**: GZipStream correctly decompresses .hgt.gz files  
✅ **Known-Good Tile**: N27E086 downloads and validates successfully  
✅ **404 Handling**: Missing tiles properly throw TileNotFoundException  
✅ **URL Format**: Skadi folder structure correctly implemented  

---

## What Didn't Work / Discoveries

### SRTM Data Format Discovery

**Expected**: SRTM3 tiles (1201x1201 samples = 2,884,802 bytes)  
**Actual**: SRTM1 tiles (3601x3601 samples = 25,934,402 bytes)

The public dataset contains higher-resolution SRTM1 data, not SRTM3. This is actually better - more detail!

**Updated test to accept both formats:**
```csharp
int srtm3Size = 1201 * 1201 * 2;  // ~2.8 MB
int srtm1Size = 3601 * 3601 * 2;  // ~25.9 MB
Assert.True(result.Length == srtm3Size || result.Length == srtm1Size);
```

### SRTM Coverage Discovery

The SRTM dataset is surprisingly complete! Many tiles we expected to be missing (ocean regions) actually exist:
- ❌ N00W180 - Exists!
- ❌ N89W180 - Exists!
- ❌ N30W170 - Exists!
- ✅ S91E000 - Missing (beyond SRTM coverage at -60° to +60° latitude)

**Test strategy**: Use tiles beyond SRTM's latitude range for 404 testing.

---

## Implementation Details

### URL Construction

```csharp
string latFolder = tileName[..3];  // "N27", "S13", etc.
string url = $"{BaseUrl}/{latFolder}/{tileName}.hgt.gz";
// Example: https://s3.amazonaws.com/elevation-tiles-prod/skadi/N27/N27E086.hgt.gz
```

### Decompression Pipeline

```csharp
1. HttpClient.GetAsync() → compressed stream
2. GZipStream(compressedStream, CompressionMode.Decompress) → decompression
3. CopyToAsync(memoryStream) → buffering
4. Return byte array → ready for storage/parsing
```

### Exception Design

Custom `TileNotFoundException`:
- Semantic meaning: tile doesn't exist in dataset (not a generic HTTP error)
- Useful properties: `TileName`, `Url`
- Clear message: "SRTM tile 'N27E086' not found at {url}"
- Expected scenario: ocean regions, data voids

---

## Acceptance Tests Results

| Requirement | Status | Notes |
|------------|--------|-------|
| Download known-good tile (N27E086) | ✅ Pass | 25.9 MB SRTM1 data |
| Decompress .hgt.gz to .hgt | ✅ Pass | Not gzipped after decompression |
| Clear 404 result for missing tiles | ✅ Pass | TileNotFoundException with details |
| No AWS credentials required | ✅ Pass | Anonymous HTTPS access |
| Build correct Skadi URL format | ✅ Pass | Folder + filename structure |

---

## Integration Notes

**Ready for use in Step 4:**

The `PublicSrtmClient` is now available for:
- Fetching tiles on demand when not in local cache
- Populating local S3 with missing tiles
- Runtime tile resolution

**Usage example:**
```csharp
var httpClient = new HttpClient();
var client = new PublicSrtmClient(httpClient);

string tileName = SrtmTileNameCalculator.Calculate(latitude, longitude);

try
{
    byte[] tileData = await client.FetchAndDecompressTileAsync(tileName);
    // Save to local S3 (Step 4)
}
catch (TileNotFoundException)
{
    // Handle ocean/void region
}
```

---

## Performance Considerations

**SRTM1 tile size**: ~25.9 MB per tile
- Larger than expected (SRTM3 is ~2.8 MB)
- Higher resolution = better terrain quality
- Considerations:
  - Network transfer time
  - Memory allocation
  - Storage requirements

**Recommendation**: Add timeout configuration and consider streaming writes in Step 4.

---

## Next Steps

According to `DEM_Lazy_Fetch_Design.md`, the next step is:

**Step 4**: Local Tile Persistence
- Write fetched tiles to local S3
- Store in `dem/srtm3/{TILE}.hgt` (may rename to srtm1)
- Store **uncompressed** `.hgt` files
- Atomic writes
- Subsequent requests should not hit public SRTM

---

## References

- Design Document: [DEM_Lazy_Fetch_Design.md](DEM_Lazy_Fetch_Design.md)
- Previous Step: [DEM_LAZY_STEP_2.md](DEM_LAZY_STEP_2.md)
- Implementation: [src/WorldApi/World/Dem/PublicSrtmClient.cs](src/WorldApi/World/Dem/PublicSrtmClient.cs)
- Tests: [src/WorldApi.Tests/Dem/PublicSrtmClientTests.cs](src/WorldApi.Tests/Dem/PublicSrtmClientTests.cs)
- Public Dataset: https://registry.opendata.aws/terrain-tiles/

---

## Notes

- The implementation is production-ready and handles real-world edge cases
- No mocking in tests - uses actual public SRTM API for confidence
- Integration tests increase reliability but require network access
- SRTM1 format discovery improves terrain quality (happy accident!)
- No changes committed to git as requested
