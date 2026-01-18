# Terrain Chunk Endpoint Execution Path Analysis

## Endpoint Route
```
GET /world/{version}/terrain/{resolution}/{chunkX}/{chunkZ}
```

Maps to: `TerrainChunksController.GetTerrainChunk()` [src/WorldApi/Controllers/TerrainChunksController.cs]

---

## Execution Path (Complete Flow)

### Entry Point
**File:** `TerrainChunksController.cs` | **Method:** `GetTerrainChunk()` | **Line:** 28

```csharp
public async Task<IActionResult> GetTerrainChunk(
    string worldVersion,
    int resolution,
    int chunkX,
    int chunkZ,
    CancellationToken cancellationToken = default)
```

### Decision 1: Check Chunk Status (Database)
**File:** `TerrainChunksController.cs` | **Line:** 40
```csharp
var status = await _coordinator.GetChunkStatusAsync(chunkX, chunkZ, resolution, worldVersion);
```

**Calls:** `TerrainChunkCoordinator.GetChunkStatusAsync()` | **File:** `World/TerrainChunkCoordinator.cs` | **Line:** 82

**Database Query:**
```csharp
var metadata = await _repository.GetChunkAsync(chunkX, chunkZ, layer, resolution, _worldVersion);
```

**Result:** Returns one of three statuses:
- `ChunkStatus.Ready` - Metadata exists with status='ready'
- `ChunkStatus.Pending` - Metadata exists with status='pending'
- `ChunkStatus.NotFound` - No metadata row in database

---

## Path 1: Status = Ready (S3 Stream)

**File:** `TerrainChunksController.cs` | **Line:** 42-72

```csharp
if (status == ChunkStatus.Ready)
{
    GetObjectResponse? s3Response = null;
    try
    {
        s3Response = await _reader.GetStreamAsync(chunkX, chunkZ, resolution, worldVersion);
        // Set content-type and cache headers
        Response.ContentType = "application/octet-stream";
        Response.ContentLength = s3Response.ContentLength;
        
        // THIS IS WHERE 16403-BYTE PAYLOAD COMES FROM IF S3 OBJECT HAS WRONG SIZE
        await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
        return new EmptyResult();  // ← RETURNS 200 WITH BINARY DATA
    }
    catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == NotFound)
    {
        // S3 object doesn't exist despite Ready status - metadata/S3 mismatch
        status = ChunkStatus.NotFound;  // Fall through to Path 3
    }
}
```

**[NEW LOGGING]** Added trace logs:
- `[TRACE] Initial status check: ... Status=Ready`
- `[TRACE] S3 stream acquired: ContentLength={bytes}` ← **CRITICAL: This shows the actual payload size**
- `[TRACE] Streaming S3 response to client: Bytes={bytes}`

**Key Point:** This path **streams raw bytes from S3 directly to HTTP response body without any processing**.
- If S3 object is 16403 bytes → client gets 16403 bytes
- If S3 object is 16919 bytes → client gets 16919 bytes

---

## Path 2: Status = Pending (202 Accepted)

**File:** `TerrainChunksController.cs` | **Line:** 74-81

```csharp
if (status == ChunkStatus.Pending)
{
    Response.CacheControl = "no-store";
    return Accepted();  // ← RETURNS 202 (NOT 200)
}
```

**[NEW LOGGING]** Added trace logs:
- `[TRACE] Initial status check: ... Status=Pending`
- `[TRACE] Returning 202 Accepted (status=Pending): ...`

**Key Point:** Returns 202, not 200. So this is NOT the path producing your 200 response.

---

## Path 3: Status = NotFound (Trigger Generation)

**File:** `TerrainChunksController.cs` | **Line:** 83-91

```csharp
Response.CacheControl = "no-store";
await _coordinator.TriggerGenerationAsync(chunkX, chunkZ, resolution, worldVersion);
return Accepted();  // ← RETURNS 202 (NOT 200)
```

**Calls:** `TerrainChunkCoordinator.TriggerGenerationAsync()` | **File:** `World/TerrainChunkCoordinator.cs` | **Line:** 115

```csharp
public virtual async Task TriggerGenerationAsync(...)
{
    // Insert "pending" metadata
    await _repository.InsertPendingAsync(...);
    
    // Fire-and-forget generation task (non-blocking)
    _ = Task.Run(async () =>
    {
        var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
        var uploadResult = await _writer.WriteAsync(chunk);
        await _repository.UpdateToReadyAsync(...);
    });
}
```

**[NEW LOGGING]** Added trace logs:
- `[TRACE] Initial status check: ... Status=NotFound`
- `[TRACE] Triggering generation (status=NotFound): ...`

**Key Point:** Returns 202 immediately. Generation happens in background.

---

## Generation Pipeline (When Path 3 Triggers)

### Step 1: Generate Chunk
**File:** `World/TerrainChunkGenerator.cs` | **Method:** `GenerateAsync()` | **Line:** 26

**[NEW LOGGING]**
```
[TRACE] GenerateAsync start: ChunkX={ChunkX}, ChunkZ={ChunkZ}, Resolution={Resolution}
```

### Step 2: Sample Heights
**File:** `World/TerrainChunkGenerator.cs` | **Line:** 43-49
```csharp
double[] rawHeights = ChunkHeightSampler.SampleHeights(
    chunkX, chunkZ, resolution, ...);

// Should produce: (resolution + 1)² = 4225 heights for resolution=64
```

**[NEW LOGGING]**
```
[TRACE] After SampleHeights: RawHeightsLength={RawHeightsLength}
```

### Step 3: Normalize Heights
**File:** `World/TerrainChunkGenerator.cs` | **Line:** 51-52
```csharp
var normalized = HeightNormalizer.Normalize(rawHeights);

// Must preserve array length
```

**[NEW LOGGING]**
```
[TRACE] After Normalize: NormalizedHeightsLength={NormalizedHeightsLength}
```

### Step 4: Guard Check
**File:** `World/TerrainChunkGenerator.cs` | **Line:** 55-64
```csharp
int gridSize = resolution + 1;
int expectedLength = gridSize * gridSize;
if (normalized.Heights.Length != expectedLength)
{
    throw InvalidDataException(...);  // ← UNCONDITIONAL THROW
}
```

**[NEW LOGGING]** If guard fails:
```
[GUARD] Heights length mismatch: ChunkX={ChunkX}, ChunkZ={ChunkZ}, 
        Resolution={Resolution}, Expected={ExpectedLength}, Actual={ActualLength}
```

### Step 5: Return TerrainChunk
**File:** `World/TerrainChunkGenerator.cs` | **Line:** 66-74

**[NEW LOGGING]**
```
[TRACE] GenerateAsync complete: ChunkX={ChunkX}, ChunkZ={ChunkZ}, 
        Resolution={Resolution}, FinalHeightsLength={FinalHeightsLength}
```

### Step 6: Serialize and Upload
**File:** `World/TerrainChunkWriter.cs` | **Method:** `WriteAsync()` | **Line:** 24

**[NEW LOGGING]**
```
[TRACE] WriteAsync entry: ChunkX={ChunkX}, ChunkZ={ChunkZ}, 
        Resolution={Resolution}, HeightsLength={HeightsLength}
```

Then:
```csharp
byte[] data = TerrainChunkSerializer.Serialize(chunk);
```

**[NEW LOGGING]**
```
[TRACE] Serialized chunk: ChunkX={ChunkX}, ChunkZ={ChunkZ}, 
        Resolution={Resolution}, PayloadBytes={PayloadBytes}, HeightsLength={HeightsLength}
```

**Serializer Format (Fixed Contract):**
```
1 byte    : version (=1)
2 bytes   : resolution (ushort)
8 bytes   : minElevation (double)
8 bytes   : maxElevation (double)
4×N bytes : heights array (N = (resolution+1)²)

For resolution=64:
- N = 65 × 65 = 4225
- PayloadBytes = 1 + 2 + 8 + 8 + 4×4225 = 16919 bytes ← EXPECTED
- PayloadBytes = 1 + 2 + 8 + 8 + 4×4096 = 16403 bytes ← YOU ARE HERE (WRONG!)
```

Then uploads to S3:
```csharp
await _s3Client.PutObjectAsync(request);
```

**[NEW LOGGING]**
```
[TRACE] Upload complete: S3Key={S3Key}, ETag={ETag}, ContentLength={ContentLength}
```

---

## Return Paths Summary

### Returns 200 (Success with Binary Data)
**ONLY from Path 1:**
- Status = Ready in database
- S3 object exists
- Controller streams S3 response directly
- **Payload size = whatever is in S3**

### Returns 202 (Accepted - Processing)
**From Path 2 or Path 3:**
- Status = Pending → waits for background task
- Status = NotFound → triggers background task
- No payload (just response headers)

### Returns 404 (Not Found)
**If request goes to wrong route or version mismatch**

---

## Where the 16403-byte Payload is Coming From

### Hypothesis Testing (Use Logs to Confirm)

**Hypothesis 1: S3 object has wrong size**
- Check log: `[TRACE] S3 stream acquired: ContentLength=16403`
- Fix: Delete S3 object, delete database row, re-request to trigger fresh generation

**Hypothesis 2: Fresh generation produces wrong size**
- Check log: `[TRACE] Serialized chunk: PayloadBytes=16403, HeightsLength=4096`
- Fix: This means sampler/normalizer is producing 64×64 instead of 65×65
- Root cause: Check if gridSize calculation is wrong

**Hypothesis 3: Guard not being triggered**
- Check log: Do you see `[GUARD]` error?
- If NO: The code path might not even be executing the guard
- If YES: Guard caught the problem but response still sent (impossible with current code)

---

## Code Paths That Can Return 200

### Only ONE return path produces 200 with binary data:

1. **TerrainChunksController.GetTerrainChunk() Line 70**
   ```csharp
   await s3Response.ResponseStream.CopyToAsync(Response.Body, cancellationToken);
   return new EmptyResult();  // Returns 200 OK with S3 object bytes
   ```

### All other paths return 202 or error

There is **NO** other code path in TerrainChunksController that returns 200 with binary data.

The `WorldChunksController.GetChunk()` endpoint (`GET /api/world/chunks/{x}/{z}`) is different and uses JSON serialization, not binary.

---

## Verification Checklist

- [ ] Database has chunk metadata with status='ready'
- [ ] S3 object exists at `chunks/{version}/terrain/r64/0/0.bin`
- [ ] S3 object is 16403 bytes (WRONG SIZE)
- [ ] Delete S3 object
- [ ] Delete database row
- [ ] Re-request chunk
- [ ] Watch logs for `[TRACE]` markers
- [ ] Confirm `[TRACE] Serialized chunk: PayloadBytes=16919`
- [ ] Verify subsequent request returns `ContentLength=16919`

---

## Log Markers Reference

Use `grep` to extract key information:

```bash
# Find all trace logs for a specific chunk
docker logs {container} 2>&1 | grep "ChunkX=0, ChunkZ=0"

# Find payload size
docker logs {container} 2>&1 | grep "Serialized chunk"

# Find S3 operations
docker logs {container} 2>&1 | grep "S3 stream acquired"

# Find guard violations
docker logs {container} 2>&1 | grep "\[GUARD\]"
```
