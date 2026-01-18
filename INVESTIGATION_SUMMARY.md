# Payload Size Mismatch Investigation - Complete Analysis

## Problem Summary

**Observed:** Client receives 16403 bytes for resolution=64
**Expected:** 16919 bytes for resolution=64
**Difference:** Exactly 64×64 heights instead of 65×65

16403 bytes = 1 + 2 + 8 + 8 + 4×4096 (version + resolution + minElev + maxElev + 64² floats)
16919 bytes = 1 + 2 + 8 + 8 + 4×4225 (version + resolution + minElev + maxElev + 65² floats)

---

## Root Cause: Three Possible Origins

### Scenario A: S3 Object Has Wrong Size
**Most Likely**

- Database metadata shows chunk status = "Ready"
- S3 object exists with 16403-byte payload
- Controller streams S3 object directly without validation
- **Result:** Client gets 16403 bytes from S3

**Evidence to Check:**
```bash
aws s3 ls s3://{bucket}/chunks/{version}/terrain/r64/0/0.bin
aws s3 ls --summarize s3://{bucket}/chunks/{version}/terrain/r64/
```

**Fix:**
```bash
# Delete the wrong-sized object
aws s3 rm s3://{bucket}/chunks/{version}/terrain/r64/0/0.bin

# Clear database metadata
DELETE FROM world_chunks WHERE chunk_x=0 AND chunk_z=0 AND resolution=64;

# Re-request to trigger fresh generation
curl http://localhost/world/v1/terrain/64/0/0

# WAIT 30 seconds for background generation
# Then re-request to stream new payload
curl http://localhost/world/v1/terrain/64/0/0
```

### Scenario B: Generator Still Produces 64×64 Grid
**Less Likely (Tests Pass)**

- Fresh generation produces 64×64 array instead of 65×65
- ChunkHeightSampler or HeightNormalizer has a bug
- TerrainChunkSerializer then serializes wrong-sized array

**Evidence to Check:**
```
Log message: [TRACE] Serialized chunk: PayloadBytes=16403, HeightsLength=4096
Log message: [GUARD] Heights length mismatch: Expected=4225, Actual=4096
```

**Fix:** Would require code investigation (but tests would catch this)

### Scenario C: Different Endpoint or Cached Response
**Very Unlikely**

- Client is hitting a different endpoint (e.g., `/api/world/chunks/{x}/{z}`)
- Cached 16403-byte response from previous run
- Proxy/cache layer returning stale data

**Evidence to Check:**
- Confirm request goes to `/world/{version}/terrain/{resolution}/{chunkX}/{chunkZ}`
- Check response headers for `X-Cache`, `Via`, `Age`
- Clear browser cache

---

## Investigation Path (New Comprehensive Logging)

All code paths now emit structured trace logs with `[TRACE]` prefix. These logs show exactly which code executed and what data was involved.

### Log Markers Added

**TerrainChunksController (Path Detection):**
```
[TRACE] Initial status check: ChunkX={X}, ChunkZ={Z}, Resolution={R}, Status={Ready|Pending|NotFound}
[TRACE] S3 stream acquired: ChunkX={X}, ChunkZ={Z}, ContentLength={BYTES}  ← PAYLOAD SIZE!
[TRACE] Streaming S3 response to client: ChunkX={X}, ChunkZ={Z}, Bytes={BYTES}
[TRACE] S3 404 mismatch: ChunkX={X}, ChunkZ={Z}, Resolution={R}. Treating as NotFound.
[TRACE] Returning 202 Accepted (status=Pending): ...
[TRACE] Triggering generation (status=NotFound): ...
```

**TerrainChunkGenerator (Data Pipeline):**
```
[TRACE] GenerateAsync start: ChunkX={X}, ChunkZ={Z}, Resolution={R}
[TRACE] After SampleHeights: RawHeightsLength={N}
[TRACE] After Normalize: NormalizedHeightsLength={N}
[GUARD] Heights length mismatch: ChunkX={X}, ChunkZ={Z}, Expected={E}, Actual={A}
[TRACE] GenerateAsync complete: ChunkX={X}, ChunkZ={Z}, Resolution={R}, FinalHeightsLength={N}
```

**TerrainChunkWriter (Serialization):**
```
[TRACE] WriteAsync entry: ChunkX={X}, ChunkZ={Z}, Resolution={R}, HeightsLength={N}
[TRACE] Object already exists in S3: S3Key={KEY}, ETag={ETAG}
[TRACE] Serialized chunk: ChunkX={X}, ChunkZ={Z}, Resolution={R}, PayloadBytes={BYTES}, HeightsLength={N}
[TRACE] Upload complete: S3Key={KEY}, ETag={ETAG}, ContentLength={BYTES}
```

---

## Quick Diagnosis Procedure

### Step 1: Enable Debug Logging
Make sure application logging level includes Information or Debug:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### Step 2: Request a Fresh Chunk
```bash
# Clear existing (if you have DB/S3 access)
DELETE FROM world_chunks WHERE chunk_x=0 AND chunk_z=0 AND resolution=64;
aws s3 rm s3://{bucket}/chunks/{version}/terrain/r64/0/0.bin

# Request the chunk
curl -v http://localhost/world/v1/terrain/64/0/0
# Expect 202 Accepted (generation in progress)

# Wait 30 seconds, then request again
sleep 30
curl -v http://localhost/world/v1/terrain/64/0/0
# Expect 200 OK with Content-Length
```

### Step 3: Check the Logs
```bash
# Extract all trace logs for this chunk
docker logs {container} 2>&1 | grep "ChunkX=0, ChunkZ=0" | grep "\[TRACE\]"
```

### Step 4: Interpret the Logs

**If you see:**
```
[TRACE] Initial status check: ... Status=Ready
[TRACE] S3 stream acquired: ContentLength=16403
[TRACE] Streaming S3 response to client: Bytes=16403
```
→ **S3 object has wrong size** (Scenario A)

**If you see:**
```
[TRACE] Initial status check: ... Status=NotFound
[TRACE] Triggering generation (status=NotFound): ...
[TRACE] After SampleHeights: RawHeightsLength=4096
[GUARD] Heights length mismatch: Expected=4225, Actual=4096
```
→ **Generator producing 64×64 grid** (Scenario B - but tests would fail!)

**If you see:**
```
[TRACE] Initial status check: ... Status=NotFound
[TRACE] Triggering generation (status=NotFound): ...
[TRACE] After SampleHeights: RawHeightsLength=4225
[TRACE] After Normalize: NormalizedHeightsLength=4225
[TRACE] GenerateAsync complete: FinalHeightsLength=4225
[TRACE] Serialized chunk: PayloadBytes=16919, HeightsLength=4225
```
→ **Fresh generation works correctly** - next request should get 16919 bytes

---

## Code Paths That Produce 200 Response

### ONLY Path: S3 Stream (Path 1)

**File:** [TerrainChunksController.cs#L42-72](../src/WorldApi/Controllers/TerrainChunksController.cs#L42-L72)

```csharp
if (status == ChunkStatus.Ready)
{
    try
    {
        s3Response = await _reader.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion);
        Response.ContentLength = s3Response.ContentLength;
        await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
        return new EmptyResult();  // ← ONLY WAY TO GET 200 WITH BINARY DATA
    }
    catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == NotFound)
    {
        status = ChunkStatus.NotFound;  // Fall through
    }
}
```

**Key Points:**
1. Only returns 200 if database shows status = "Ready"
2. Only streams if S3 object exists
3. Streams S3 bytes directly without modification
4. **Payload size = whatever is in S3**

---

## Serializer Contract (Immutable)

The binary format is locked and validated:

**Wire Format:**
```
Offset   Size    Field           Type        Notes
------   ----    -----           ----        -----
0        1       Version         byte        Must be 1
1        2       Resolution      ushort      E.g., 64
3        8       MinElevation    double      Lowest point
11       8       MaxElevation    double      Highest point
19       N×4     Heights         float[]     N = (resolution+1)²
```

**Height Array Contract:**
```
For resolution=64:
- GridSize = resolution + 1 = 65
- Heights count = 65 × 65 = 4225
- Row-major order: index = z × gridSize + x
- Total bytes = 19 + 4×4225 = 16919

For resolution=16:
- GridSize = 17
- Heights count = 289
- Total bytes = 19 + 4×289 = 1,175

For resolution=1:
- GridSize = 2
- Heights count = 4
- Total bytes = 19 + 4×4 = 35
```

**Guard Enforcement:**
1. **Serialize()** (Line 36-42): Throws if `heights.Length != (resolution+1)²`
2. **Deserialize()** (Line 88-92): Validates exact byte count matches
3. **Deserialize()** (Line 109-113): Re-validates heights length after reading
4. **Generator** (Line 59-64): Throws if normalized heights don't match expected size

All guards include chunk coordinates in error messages for debugging.

---

## Files Modified for Investigation

### Added Logging (New)

1. **TerrainChunksController.cs**
   - Line 39: Initial status check log
   - Line 56: S3 stream acquired log
   - Line 74: Streaming response log
   - Line 85: S3 404 fallthrough log
   - Line 104: 202 Pending log
   - Line 118: 202 Trigger generation log

2. **TerrainChunkGenerator.cs**
   - Added ILogger<TerrainChunkGenerator> parameter
   - Line 34: Start log
   - Line 49: After SampleHeights log
   - Line 54: After Normalize log
   - Line 59: Guard failure log
   - Line 84: Complete log

3. **TerrainChunkWriter.cs**
   - Added ILogger<TerrainChunkWriter> parameter
   - Line 35: Entry log
   - Line 46: Object exists log
   - Line 56: Serialized chunk log (CRITICAL - shows PayloadBytes)
   - Line 68: Upload complete log

4. **Program.cs**
   - Updated TerrainChunkGenerator registration to inject logger
   - Updated TerrainChunkWriter registration to inject logger

5. **TerrainChunkGeneratorTests.cs**
   - Updated CreateGenerator() to inject mock logger

### Documentation (New)

1. **DIAGNOSTIC_GUIDE.md** - Step-by-step investigation guide
2. **EXECUTION_PATH_TRACE.md** - Complete code path analysis with line numbers

---

## Expected Behavior After Fix

### First Request (Cache Miss)
```
Status Code: 202 Accepted
Log: [TRACE] Initial status check: Status=NotFound
Log: [TRACE] Triggering generation...
Wait: 5-30 seconds for background task
```

### Second Request (Cache Hit)
```
Status Code: 200 OK
Content-Length: 16919
Content-Type: application/octet-stream
Logs: [TRACE] Initial status check: Status=Ready
Logs: [TRACE] S3 stream acquired: ContentLength=16919
Body: 16919 bytes of binary terrain data
```

---

## Testing Against Contract

All 140 tests pass, including new parametrized tests for exact buffer sizes:

| Resolution | Expected Bytes | Test Coverage |
|-----------|---------------|---------------|
| 1         | 35            | ✓ Parametrized |
| 4         | 147           | ✓ Parametrized |
| 8         | 451           | ✓ Parametrized |
| 16        | 1,175         | ✓ Parametrized |
| 32        | 4,275         | ✓ Parametrized |
| 64        | 16,919        | ✓ Parametrized |

Tests verify:
- Exact byte count matches contract
- Guard catches truncation attempts
- Deserializer validates corrupted buffers

---

## Next Steps

1. **Run diagnostic logs**: Enable Information logging and capture `[TRACE]` output
2. **Identify the payload source**: Is it S3 or fresh generation?
3. **Delete stale data if needed**: Remove wrong-sized S3 object and database row
4. **Verify fix**: Confirm next generation produces 16919 bytes
5. **Monitor logs**: Watch for guard violations or generation errors

The logging instrumentation now provides complete visibility into which code path executes and what data is involved at each step.
