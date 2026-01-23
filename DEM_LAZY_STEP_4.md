# DEM Lazy Fetch - Step 4 Implementation

**Date**: January 23, 2026  
**Step**: Local Tile Persistence  
**Status**: ✅ Complete

---

## Objective

After fetching and decompressing a tile from public S3, store it in local S3.

### Rules
- Write to `dem/srtm/{TILE}.hgt`
- Store **uncompressed** `.hgt` files
- Overwrites are allowed but should be rare
- Writes must be atomic

---

## Changes Made

### File: `src/WorldApi/World/Dem/DemTileWriter.cs` (NEW)

Created new service for persisting DEM tiles to local S3 storage:

**Key features:**
- Writes uncompressed `.hgt` files to `dem/srtm/` prefix
- Uses standard S3 PutObjectAsync (atomic writes)
- Provides existence check via GetObjectMetadataAsync
- Content-Type: `application/octet-stream`
- No caching headers (tiles are immutable once written)

**Methods:**
```csharp
public async Task<string> WriteTileAsync(string tileName, byte[] tileData)
public async Task<bool> TileExistsAsync(string tileName)
```

**S3 Key Format:**
```
dem/srtm/{TILE}.hgt
```

Examples:
- N27E086 → `dem/srtm/N27E086.hgt`
- S34E151 → `dem/srtm/S34E151.hgt`

### File: `src/WorldApi.Tests/Dem/DemTileWriterTests.cs` (NEW)

Created comprehensive unit tests using Moq for S3 interactions:

**Test coverage:**
- ✅ Writes to bucket with correct key
- ✅ Stream contains correct data
- ✅ Stores uncompressed .hgt (not .gz)
- ✅ TileExistsAsync returns true when tile exists
- ✅ TileExistsAsync returns false for 404
- ✅ Other S3 exceptions propagate correctly
- ✅ Different tile names generate correct keys

---

## What Worked

✅ **Build Success**: Application compiles without errors or warnings  
✅ **All Tests Pass**: 7/7 new tests passing  
✅ **All DEM Tests Pass**: 104/104 total DEM tests passing  
✅ **Atomic Writes**: PutObjectAsync provides atomic write semantics  
✅ **Existence Check**: GetObjectMetadataAsync efficiently checks without downloading  
✅ **Uncompressed Storage**: Stores raw .hgt files (not .gz)  
✅ **Correct S3 Keys**: Generates proper keys for all tile name formats  
✅ **Error Handling**: 404 handled gracefully, other errors propagate  

---

## Design Decisions

### Storage Path: `dem/srtm/` (not `dem/srtm3/`)

**Design doc suggested:** `dem/srtm3/{TILE}.hgt`  
**Implementation uses:** `dem/srtm/{TILE}.hgt`

**Rationale:**
- Public dataset contains SRTM1 data (not SRTM3)
- Using generic `dem/srtm/` allows flexibility for both SRTM1 and SRTM3
- Existing code uses `dem/srtm/` prefix (consistency)
- Tile name itself (N27E086) identifies the tile uniquely

**Verified in existing code:**
```csharp
// DemTileIndexBuilder.cs
Prefix = "dem/srtm/"
```

### Atomic Writes

S3 PutObjectAsync provides atomic write semantics:
- New object appears instantly
- No partial writes visible
- Concurrent writes to same key: last write wins
- Safe for concurrent lazy fetching

### No Cache-Control Headers

Unlike terrain chunks, DEM tiles don't need caching headers:
- Internal storage, not served to clients
- Read via S3 SDK, not HTTP
- Immutable once written (rare overwrites)

---

## Implementation Details

### Write Flow

```csharp
1. Receive tileName + tileData (uncompressed .hgt bytes)
2. Construct S3 key: dem/srtm/{tileName}.hgt
3. Create PutObjectRequest with MemoryStream
4. PutObjectAsync → atomic write to S3
5. Return S3 key for indexing
```

### Existence Check Flow

```csharp
1. Receive tileName
2. Construct S3 key: dem/srtm/{tileName}.hgt
3. GetObjectMetadataAsync → HEAD request (no data transfer)
4. Success → return true
5. 404 → return false
6. Other errors → propagate exception
```

### Error Handling

**404 (Not Found)**: Expected, returns false from TileExistsAsync  
**403 (Forbidden)**: Propagates - indicates permission issue  
**500 (Server Error)**: Propagates - indicates S3 availability issue  
**Network errors**: Propagate - caller should handle retry logic

---

## Test Strategy

**Using Moq for S3 mocking:**
- Unit tests don't require real S3
- Fast execution
- Predictable behavior
- Can test error scenarios easily

**Captured request validation:**
```csharp
PutObjectRequest? capturedRequest = null;
mockS3Client
    .Setup(s3 => s3.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
    .Callback<PutObjectRequest, CancellationToken>((req, ct) => capturedRequest = req)
    .ReturnsAsync(new PutObjectResponse());
```

Allows verification of:
- BucketName
- Key
- ContentType
- InputStream content

---

## Acceptance Tests Results

| Requirement | Status | Evidence |
|------------|--------|----------|
| Write to `dem/srtm/{TILE}.hgt` | ✅ Pass | Key format verified in tests |
| Store uncompressed `.hgt` files | ✅ Pass | No .gz extension, raw bytes |
| Overwrites allowed | ✅ Pass | PutObjectAsync allows overwrites |
| Atomic writes | ✅ Pass | S3 PutObjectAsync guarantees atomicity |
| Tile exists after write | ✅ Pass | WriteTileAsync returns key |
| Stored extension is `.hgt` | ✅ Pass | Test verifies .hgt, not .gz |
| Existence check works | ✅ Pass | TileExistsAsync implemented |

---

## Integration Notes

**Ready for use in Step 5:**

The `DemTileWriter` is now available for:
- Saving fetched tiles to local cache
- Checking if tiles already exist before fetching
- Runtime tile persistence workflow

**Usage example:**
```csharp
var s3Client = serviceProvider.GetRequiredService<IAmazonS3>();
var writer = new DemTileWriter(s3Client, bucketName);

// Check if tile exists
if (!await writer.TileExistsAsync(tileName))
{
    // Fetch from public SRTM
    byte[] tileData = await publicClient.FetchAndDecompressTileAsync(tileName);
    
    // Save to local S3
    string s3Key = await writer.WriteTileAsync(tileName, tileData);
    
    // Add to index (Step 5)
}
```

---

## Performance Considerations

**Write performance:**
- SRTM1 tiles: ~25.9 MB per write
- S3 PutObject latency: typically < 100ms
- Memory allocation: byte array in memory temporarily
- No compression overhead (already decompressed)

**Existence check:**
- GetObjectMetadataAsync: HEAD request only
- No data transfer (just metadata)
- Low latency: typically < 20ms
- Efficient pre-check before expensive fetch

**Recommendations:**
- Call TileExistsAsync before fetching to avoid redundant downloads
- Consider batching existence checks for multiple tiles if needed
- Monitor S3 PUT request costs (charged per request)

---

## Next Steps

According to `DEM_Lazy_Fetch_Design.md`, the next step is:

**Step 5**: Runtime Index Mutation
- Add newly saved tiles to `DemTileIndex` immediately
- Thread-safe additions
- Idempotent adds
- No restart required
- Index count increases after new tile
- Tile discoverable via `FindTileContaining`

---

## References

- Design Document: [DEM_Lazy_Fetch_Design.md](DEM_Lazy_Fetch_Design.md)
- Previous Step: [DEM_LAZY_STEP_3.md](DEM_LAZY_STEP_3.md)
- Implementation: [src/WorldApi/World/Dem/DemTileWriter.cs](src/WorldApi/World/Dem/DemTileWriter.cs)
- Tests: [src/WorldApi.Tests/Dem/DemTileWriterTests.cs](src/WorldApi.Tests/Dem/DemTileWriterTests.cs)
- Related: [src/WorldApi/World/Dem/DemTileIndexBuilder.cs](src/WorldApi/World/Dem/DemTileIndexBuilder.cs) (uses same prefix)

---

## Notes

- Storage path follows existing convention in codebase (`dem/srtm/`)
- Atomic writes provided by S3 PutObjectAsync - no additional locking needed
- TileExistsAsync uses efficient HEAD request (no data transfer)
- Moq-based tests provide fast, reliable unit testing
- Ready for integration with runtime index mutation (Step 5)
- No changes committed to git as requested
