# World Origin Anchoring - Testing Guide

## Unit Test Status

All existing unit tests pass:
```
Passed: 187, Failed: 0, Skipped: 0
```

No regressions introduced by this feature.

---

## Integration Testing

### Prerequisites

Before testing, ensure:
- PostgreSQL is running with WorldAPI database
- S3 (AWS or MinIO) is configured and accessible
- `appsettings.json` has valid `World` config with origin coordinates
- Database is initialized (migrations applied)

### Test 1: Anchor Chunk Generation on First Startup

**Objective:** Verify anchor chunk is generated when no chunks exist.

**Setup:**
1. Ensure `world_chunks` table is empty:
   ```sql
   DELETE FROM world_chunks;
   ```
2. Ensure at least one active world version:
   ```sql
   SELECT * FROM world_versions WHERE is_active = true;
   ```

**Execute:**
1. Start the application:
   ```bash
   dotnet run
   ```
2. Observe startup logs for:
   ```
   🚀 Loading active world versions from PostgreSQL at startup...
   ✓ Successfully loaded 1 active world version(s) at startup
   🔧 Checking if anchor chunks need to be generated...
   📍 Generating anchor chunk for world version '{version}'...
   ✓ Anchor chunk generated: 9 vertices, all elevations = 0
   ✓ Anchor chunk persisted for world version '{version}': S3Key=...
   ✓ Anchor chunk initialization complete
   ```

**Verification:**
- [ ] Logs show anchor generation
- [ ] No errors in startup sequence
- [ ] Application is running (port listening)

**Expected Result:** ✓ PASS - Anchor chunk generated and persisted

---

### Test 2: Idempotent Anchor Generation (Second Startup)

**Objective:** Verify anchor chunk is NOT generated on second startup (idempotent).

**Setup:**
- Application successfully started (Test 1 complete)
- Anchor chunk exists in database and S3

**Execute:**
1. Stop the application (Ctrl+C)
2. Restart the application:
   ```bash
   dotnet run
   ```
3. Observe startup logs for:
   ```
   🚀 Loading active world versions from PostgreSQL at startup...
   ✓ Successfully loaded 1 active world version(s) at startup
   🔧 Checking if anchor chunks need to be generated...
   ✓ World version '{version}' already has chunks, skipping anchor generation
   ✓ Anchor chunk initialization complete
   ```

**Verification:**
- [ ] Logs show "already has chunks" message
- [ ] No new S3 upload occurs (same ETag)
- [ ] Database still has exactly 1 chunk (no duplicates)

**Expected Result:** ✓ PASS - Anchor generation skipped (idempotent)

---

### Test 3: Verify Anchor Chunk in S3

**Objective:** Verify anchor chunk file exists in S3 with correct content.

**Setup:**
- Anchor chunk has been generated (Test 1)
- S3 access configured

**Execute:**
1. List S3 objects in chunks directory:
   ```bash
   # AWS S3
   aws s3 ls s3://your-bucket/chunks/ --recursive
   
   # MinIO (if using local S3)
   mc ls minio/bucket-name/chunks/
   ```
2. Download and inspect anchor chunk:
   ```bash
   # AWS S3
   aws s3 cp s3://your-bucket/chunks/{version}/terrain/0_0_r2.bin ./anchor.bin
   
   # MinIO
   mc cp minio/bucket-name/chunks/{version}/terrain/0_0_r2.bin ./anchor.bin
   ```
3. Verify file size:
   ```bash
   ls -lh anchor.bin
   # Expected: 1,175 bytes (1 + 2 + 8 + 8 + 1156 bytes)
   ```

**Verification:**
- [ ] S3 object exists at `chunks/{version}/terrain/0_0_r2.bin`
- [ ] File size is exactly 1,175 bytes
- [ ] Cache-Control header is set: `public, max-age=31536000, immutable`

**Expected Result:** ✓ PASS - Anchor chunk present in S3 with correct size

---

### Test 4: Verify Anchor Chunk in Database

**Objective:** Verify anchor chunk metadata exists in database with correct status.

**Setup:**
- Anchor chunk has been generated (Test 1)
- PostgreSQL access configured

**Execute:**
1. Query anchor chunk metadata:
   ```sql
   SELECT chunk_x, chunk_z, layer, resolution, status, s3_key 
   FROM world_chunks 
   WHERE chunk_x = 0 AND chunk_z = 0;
   ```
2. Verify result:

**Expected Result:**
```
 chunk_x | chunk_z | layer   | resolution | status | s3_key
---------+---------+---------+------------+--------+---------------------------------------------------
 0       | 0       | terrain | 2          | ready  | chunks/{version}/terrain/0_0_r2.bin
```

**Verification:**
- [ ] Row exists with `chunk_x = 0, chunk_z = 0`
- [ ] `layer = 'terrain'`
- [ ] `resolution = 2`
- [ ] `status = 'ready'`
- [ ] `s3_key` matches expected format

**Expected Result:** ✓ PASS - Metadata correct in database

---

### Test 5: API Endpoint - Get World Contract

**Objective:** Verify world contract endpoint returns correct configuration.

**Setup:**
- Application is running
- Anchor chunk has been generated

**Execute:**
1. Query world contract endpoint:
   ```bash
   curl -X GET http://localhost:5000/api/world-versions/{version}/contract
   ```
   
   Example:
   ```bash
   curl -X GET http://localhost:5000/api/world-versions/v1.0/contract
   ```

2. Verify response structure

**Expected Response:**
```json
{
  "version": "v1.0",
  "origin": {
    "latitude": 46.8721,
    "longitude": -113.994
  },
  "chunkSizeMeters": 100,
  "metersPerDegreeLatitude": 111320,
  "immutable": true,
  "description": "This world contract defines the immutable geographic anchoring and spatial parameters for the world."
}
```

**Verification:**
- [ ] HTTP 200 response
- [ ] `version` matches request parameter
- [ ] `origin.latitude` matches config
- [ ] `origin.longitude` matches config
- [ ] `chunkSizeMeters` matches config
- [ ] `metersPerDegreeLatitude` matches config
- [ ] `immutable = true`

**Expected Result:** ✓ PASS - Contract endpoint works correctly

---

### Test 6: World Contract Caching

**Objective:** Verify clients can cache world contract.

**Setup:**
- Application is running
- Contract endpoint is working (Test 5)

**Execute:**
1. Call endpoint multiple times:
   ```bash
   for i in {1..10}; do
     curl -X GET http://localhost:5000/api/world-versions/v1.0/contract
   done
   ```
2. Measure response time
3. Observe no database queries in logs

**Verification:**
- [ ] All requests succeed with 200
- [ ] Response times are consistent (< 1ms for cached response)
- [ ] No database queries occur (in-memory cache)

**Expected Result:** ✓ PASS - Responses are fast and repeatable

---

### Test 7: Lazy Chunk Generation After Anchor

**Objective:** Verify lazy chunk generation works after anchor is created.

**Setup:**
- Application running with anchor chunk already created
- Application is ready for chunk requests

**Execute:**
1. Request terrain chunk with higher resolution:
   ```bash
   curl -X GET http://localhost:5000/api/terrain-chunks/{version}/1/0?resolution=16
   ```
2. Observe server logs for chunk generation
3. Query database for new chunk:
   ```sql
   SELECT COUNT(*) FROM world_chunks WHERE resolution = 16;
   ```

**Verification:**
- [ ] HTTP 200 response with chunk data
- [ ] New chunk created at (1, 0) with resolution 16
- [ ] No conflicts with anchor chunk at (0, 0)

**Expected Result:** ✓ PASS - Lazy generation works alongside anchor

---

### Test 8: Error Handling - Invalid Version

**Objective:** Verify error handling for non-existent world version.

**Setup:**
- Application is running

**Execute:**
1. Query world contract for non-existent version:
   ```bash
   curl -X GET http://localhost:5000/api/world-versions/invalid-version/contract
   ```

**Expected Response:**
```
HTTP 404
{
  "error": "World version 'invalid-version' not found or is not active"
}
```

**Verification:**
- [ ] HTTP 404 response
- [ ] Error message is clear and accurate

**Expected Result:** ✓ PASS - Correct error handling

---

### Test 9: Coordinate Conversion Verification

**Objective:** Verify world-to-geographic coordinate conversion using world contract.

**Setup:**
- Application is running with valid world contract

**Execute:**
1. Get world contract:
   ```bash
   curl -X GET http://localhost:5000/api/world-versions/v1.0/contract
   ```
2. Calculate expected coordinates for chunk (1, 0):
   ```
   Origin: Lat = 46.8721, Lon = -113.994
   Chunk Size = 100 meters
   
   ChunkLat = 46.8721 + (0 * 100) / 111320 = 46.8721
   ChunkLon = -113.994 + (1 * 100) / (111320 * cos(46.8721° * π/180))
            = -113.994 + 100 / 75,486.52
            = -113.994 + 0.001325
            = -113.992675
   ```
3. Verify chunk is within expected geographic bounds

**Verification:**
- [ ] Calculated coordinates are reasonable
- [ ] Chunk is in expected geographic location

**Expected Result:** ✓ PASS - Coordinate conversion is correct

---

### Test 10: Multiple World Versions

**Objective:** Verify anchor generation works for multiple active world versions.

**Setup:**
1. Create multiple active world versions in database:
   ```sql
   INSERT INTO world_versions (version, is_active) VALUES ('v1.0', true);
   INSERT INTO world_versions (version, is_active) VALUES ('v2.0', true);
   ```
2. Clear all chunks:
   ```sql
   DELETE FROM world_chunks;
   ```

**Execute:**
1. Restart application
2. Observe logs for multiple anchor generations:
   ```
   📍 Generating anchor chunk for world version 'v1.0'...
   ✓ Anchor chunk persisted for world version 'v1.0': S3Key=...
   📍 Generating anchor chunk for world version 'v2.0'...
   ✓ Anchor chunk persisted for world version 'v2.0': S3Key=...
   ```

**Verification:**
- [ ] Anchor generated for both versions
- [ ] Each has separate S3 key and database entry

**Expected Result:** ✓ PASS - Multiple versions handled correctly

---

## Regression Testing

Run full test suite to ensure no regressions:

```bash
dotnet test
```

**Expected Result:**
```
Passed: 187, Failed: 0, Skipped: 0
```

---

## Performance Testing

### Startup Time

**Objective:** Verify startup time with anchor generation is acceptable.

**Execute:**
1. Delete all chunks
2. Measure startup time:
   ```bash
   time dotnet run
   ```

**Expected Result:** < 5 seconds (including S3 upload)

### Memory Usage

**Objective:** Verify no memory leaks from anchor generation.

**Execute:**
1. Monitor memory during multiple startup/shutdown cycles
2. Check for stable memory footprint

**Expected Result:** Memory stabilizes, no continuous growth

---

## Cleanup After Testing

```bash
# Delete test chunks from S3
aws s3 rm s3://your-bucket/chunks/ --recursive

# Delete test chunks from database
DELETE FROM world_chunks;

# Restart application to regenerate if needed
dotnet run
```

---

## Test Summary Checklist

- [ ] Test 1: Anchor generation on first startup
- [ ] Test 2: Idempotent generation on subsequent startups
- [ ] Test 3: Anchor chunk file exists in S3
- [ ] Test 4: Anchor chunk metadata in database
- [ ] Test 5: World contract API endpoint
- [ ] Test 6: World contract caching
- [ ] Test 7: Lazy chunk generation after anchor
- [ ] Test 8: Error handling for invalid version
- [ ] Test 9: Coordinate conversion verification
- [ ] Test 10: Multiple world versions
- [ ] Regression: Full unit test suite passes
- [ ] Performance: Startup time acceptable
- [ ] Memory: No leaks detected

---

## Troubleshooting During Testing

### S3 Upload Fails
- Verify S3 credentials in `appsettings.json`
- Check bucket name is correct
- Verify bucket permissions allow PutObject

### Database Insert Fails
- Verify PostgreSQL is running
- Check connection string
- Verify `world_versions` table exists
- Verify `world_chunks` table exists

### Endpoint Returns 404
- Verify version string matches exactly (case-sensitive)
- Check `is_active = true` in database
- Restart application if needed

### Startup Crashes
- Check logs for specific error message
- Verify all configuration is present
- Verify database and S3 connectivity

---

