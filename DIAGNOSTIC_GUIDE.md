# Terrain Payload Size Diagnostic Guide

## Problem Statement
The client is receiving a 16403-byte payload for resolution=64, which is exactly 64×64 heights + 19 bytes overhead.
Expected payload: 16919 bytes (65×65 heights + 19 bytes overhead).

## Root Cause Investigation Steps

### 1. **Check Database Metadata** (FIRST)
If the database has a chunk marked as "Ready", it means the endpoint will return 200 from S3 without regenerating.

```sql
-- Check if chunk is in database
SELECT * FROM world_chunks 
WHERE chunk_x = 0 AND chunk_z = 0 AND resolution = 64;
```

**If found:** Database has stale "Ready" metadata pointing to S3 object.
**Action:** Either delete the row or check if S3 object exists.

### 2. **Check S3 Object** (SECOND)
If database shows "Ready" but S3 returns 404, the controller will catch the exception and fall through to trigger regeneration. But if S3 object **exists**, it will stream that 16403-byte payload directly to the client.

```bash
# Check if S3 object exists
aws s3 ls s3://{bucket}/chunks/{version}/terrain/r64/0/0.bin
```

**If found:** This is the source of the 16403-byte payload.
**Action:** Delete this object and re-request the chunk to trigger regeneration.

### 3. **Trace the Execution Path** (THIRD - New Logging)
The following log messages now indicate which code path is executing:

```
[TRACE] Initial status check: ... Status=Ready|Pending|NotFound
[TRACE] S3 stream acquired: ... ContentLength={ContentLength}   ← THIS IS THE PAYLOAD SIZE!
[TRACE] Streaming S3 response to client: ... Bytes={ContentLength}
```

**Expected logs for fresh generation:**
```
[TRACE] Initial status check: ... Status=NotFound
[TRACE] Triggering generation (status=NotFound): ...
[TRACE] GenerateAsync start: ...
[TRACE] After SampleHeights: RawHeightsLength={gridSize²}
[TRACE] After Normalize: NormalizedHeightsLength={gridSize²}
[TRACE] GenerateAsync complete: FinalHeightsLength={gridSize²}
[TRACE] WriteAsync entry: HeightsLength={gridSize²}
[TRACE] Serialized chunk: PayloadBytes={expected bytes}, HeightsLength={gridSize²}
```

### 4. **Verify Guard Enforcement**
If generation happens, these guards ensure the contract:

- **Line 1 (SampleHeights):** Must produce `(resolution+1)² = 4225` heights
- **Line 2 (Normalize):** Must preserve length
- **Line 3 (Generator guard):** Throws if length ≠ expected
- **Line 4 (Serializer guard):** Throws if height array doesn't match expected size

```
[GUARD] Heights length mismatch: ChunkX=..., ChunkZ=..., Expected=4225, Actual=4096
```

**If this appears:** Generator or sampler is still producing 64×64 instead of 65×65.

### 5. **Check Payload Contract**
Once the payload is generated and logged:

```
[TRACE] Serialized chunk: PayloadBytes={X}
```

**Expected for resolution=64:**
- GridSize = 65
- Heights count = 4225
- Payload = 1 (version) + 2 (resolution) + 8 (minElev) + 8 (maxElev) + 4×4225 = **16919 bytes**

**Actual (problem case):**
- GridSize = 64 (WRONG!)
- Heights count = 4096
- Payload = 1 + 2 + 8 + 8 + 4×4096 = **16403 bytes** ← YOU ARE HERE

## Quick Diagnostic Script

Run this after requesting a terrain chunk:

```bash
# 1. Check database
psql -U {user} -d {db} -c "SELECT chunk_x, chunk_z, status FROM world_chunks WHERE resolution=64 LIMIT 5;"

# 2. List S3 objects
aws s3 ls s3://{bucket}/chunks/{version}/terrain/r64/ --recursive

# 3. Check application logs for [TRACE] markers
docker logs {container} 2>&1 | grep "\[TRACE\]"
```

## Expected Output for Fresh Generation (resolution=64)

```
[TRACE] Initial status check: Status=NotFound
[TRACE] Triggering generation: ChunkX=0, ChunkZ=0, Resolution=64
[TRACE] GenerateAsync start: ChunkX=0, ChunkZ=0, Resolution=64
[TRACE] After SampleHeights: RawHeightsLength=4225
[TRACE] After Normalize: NormalizedHeightsLength=4225
[TRACE] GenerateAsync complete: FinalHeightsLength=4225
[TRACE] WriteAsync entry: HeightsLength=4225
[TRACE] Serialized chunk: PayloadBytes=16919, HeightsLength=4225   ← MUST SEE THIS
[TRACE] Upload complete: S3Key=..., ContentLength=16919
```

## Expected Output for S3 Cache Hit

```
[TRACE] Initial status check: Status=Ready
[TRACE] S3 stream acquired: ContentLength=16919   ← MUST BE 16919, NOT 16403
[TRACE] Streaming S3 response to client: Bytes=16919
```

## Immediate Actions

1. **Delete the 16403-byte S3 object:**
   ```bash
   aws s3 rm s3://{bucket}/chunks/{version}/terrain/r64/0/0.bin
   ```

2. **Delete the database row (if it exists):**
   ```sql
   DELETE FROM world_chunks WHERE chunk_x=0 AND chunk_z=0 AND resolution=64;
   ```

3. **Restart the API and re-request the chunk**

4. **Watch the logs for [TRACE] markers** to confirm it regenerates with correct 16919-byte payload

## If the Problem Persists

If even fresh generation produces 16403 bytes:
1. Check logs for `[GUARD] Heights length mismatch` error
2. This indicates the generator itself is producing 64×64 instead of 65×65
3. Verify ChunkHeightSampler.cs line 17: `int gridSize = resolution + 1;`
4. Verify HeightNormalizer preserves array length
