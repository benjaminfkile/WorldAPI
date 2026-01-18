# Quick Reference: Diagnostic Commands

## Payload Size Verification

```bash
# Check S3 object size
aws s3 ls --summarize s3://{BUCKET}/chunks/{VERSION}/terrain/r64/ | grep "Total Size"

# List specific chunk objects
aws s3 ls s3://{BUCKET}/chunks/{VERSION}/terrain/r64/{CHUNKX}/{CHUNKZ}/

# Check exact file size
aws s3api head-object \
  --bucket {BUCKET} \
  --key "chunks/{VERSION}/terrain/r64/0/0.bin" \
  --query 'ContentLength'
```

**Expected output:** `16919` (if correct)
**Current problem:** `16403` (if old object)

---

## Database Investigation

```bash
# Check metadata for specific chunk
SELECT * FROM world_chunks 
WHERE chunk_x = 0 AND chunk_z = 0 AND resolution = 64 AND layer = 'terrain';

# Check all Ready chunks for a resolution
SELECT chunk_x, chunk_z, status, checksum 
FROM world_chunks 
WHERE resolution = 64 AND status = 'ready';

# Delete stale metadata
DELETE FROM world_chunks 
WHERE chunk_x = 0 AND chunk_z = 0 AND resolution = 64;

# Delete all stale metadata for a resolution
DELETE FROM world_chunks 
WHERE resolution = 64;
```

---

## Log Extraction

```bash
# All trace logs for a specific chunk
docker logs {CONTAINER} 2>&1 | grep "ChunkX=0, ChunkZ=0"

# Payload size confirmation
docker logs {CONTAINER} 2>&1 | grep "Serialized chunk"

# S3 stream size confirmation
docker logs {CONTAINER} 2>&1 | grep "S3 stream acquired"

# Guard violations
docker logs {CONTAINER} 2>&1 | grep "\[GUARD\]"

# Full trace of a request
docker logs {CONTAINER} 2>&1 | grep "\[TRACE\]" | tail -50
```

---

## Fix Procedure (Complete)

### Step 1: Identify the Problem
```bash
# Check which chunks have wrong size
aws s3 ls s3://{BUCKET}/chunks/{VERSION}/terrain/r64/ --recursive | \
  while read -r line; do
    KEY=$(echo "$line" | awk '{print $NF}')
    SIZE=$(aws s3api head-object --bucket {BUCKET} --key "$KEY" --query 'ContentLength')
    if [ "$SIZE" != "16919" ]; then
      echo "$KEY: $SIZE (WRONG!)"
    fi
  done
```

### Step 2: Delete Old Objects
```bash
# Delete S3 objects for a specific resolution
aws s3 rm s3://{BUCKET}/chunks/{VERSION}/terrain/r64/ --recursive

# Or delete specific chunk
aws s3 rm s3://{BUCKET}/chunks/{VERSION}/terrain/r64/0/0.bin
```

### Step 3: Clear Database
```bash
# Delete metadata for specific resolution
DELETE FROM world_chunks WHERE resolution = 64;

# Verify deletion
SELECT COUNT(*) FROM world_chunks WHERE resolution = 64;
```

### Step 4: Verify Logs Enabled
```bash
# Check appsettings.json
cat appsettings.Development.json | jq '.Logging.LogLevel'

# Should show:
# {
#   "Default": "Information",
#   "Microsoft": "Warning"
# }
```

### Step 5: Trigger Fresh Generation
```bash
# First request - returns 202 Accepted
curl -i http://localhost:5100/world/v1/terrain/64/0/0

# Wait 30 seconds for background task
sleep 30

# Second request - returns 200 OK with correct payload
curl -i http://localhost:5100/world/v1/terrain/64/0/0
```

### Step 6: Verify Fix
```bash
# Extract the Content-Length from response
curl -I http://localhost:5100/world/v1/terrain/64/0/0 | grep Content-Length

# Should show: Content-Length: 16919
```

### Step 7: Check Logs
```bash
# Watch for successful serialization
docker logs {CONTAINER} 2>&1 | grep "Serialized chunk.*Resolution=64" | tail -1

# Should show: PayloadBytes=16919, HeightsLength=4225
```

---

## Map Resolution to Expected Bytes

| Resolution | GridSize | Heights Count | Expected Bytes |
|-----------|----------|---------------|----------------|
| 1         | 2        | 4             | 35             |
| 2         | 3        | 9             | 51             |
| 4         | 5        | 25            | 147            |
| 8         | 9        | 81            | 451            |
| 16        | 17       | 289           | 1,175          |
| 32        | 33       | 1,089         | 4,275          |
| 64        | 65       | 4,225         | 16,919         |
| 128       | 129      | 16,641        | 66,819         |

**Formula:** `1 + 2 + 8 + 8 + 4×((resolution+1)²)`

---

## Monitoring Checklist

- [ ] S3 object size is 16919 bytes (not 16403)
- [ ] Database shows `status = 'ready'` with checksum
- [ ] First request returns 202 Accepted
- [ ] Second request returns 200 OK
- [ ] Response header `Content-Length: 16919`
- [ ] Response header `Content-Type: application/octet-stream`
- [ ] Log shows `[TRACE] Serialized chunk: PayloadBytes=16919`
- [ ] Log shows `[TRACE] S3 stream acquired: ContentLength=16919`
- [ ] No `[GUARD]` error messages in logs
- [ ] Payload received by client is exactly 16919 bytes

---

## Debug: Binary File Inspection

If you have the binary files downloaded:

```bash
# Check file size
ls -lh terrain_chunk.bin

# Extract header (first 19 bytes)
xxd -l 19 -g 1 terrain_chunk.bin

# Should show pattern like:
# 01 40 00 (version=1, resolution=64 in little-endian ushort)
# 8 bytes of double (minElev)
# 8 bytes of double (maxElev)
```

**For resolution=64:**
- Byte 0: `01` (version)
- Bytes 1-2: `40 00` (ushort 64 in little-endian)
- Bytes 3-10: 8 bytes (min elevation)
- Bytes 11-18: 8 bytes (max elevation)
- Bytes 19+: 16,900 bytes of float array (4225 floats × 4 bytes)
- **Total: 16,919 bytes**

**If file is 16,403 bytes:**
- Bytes 19+: 16,384 bytes (4096 floats × 4 bytes = 64×64 grid)
- **This is the wrong format from old code**

---

## Confirmation Test

After fix, run this test:

```bash
#!/bin/bash
echo "Testing resolution=64 chunk..."
curl -s http://localhost:5100/world/v1/terrain/64/0/0 -w "\nStatus: %{http_code}\nContent-Length: %{content_length_download}\n" > /tmp/chunk.bin
SIZE=$(wc -c < /tmp/chunk.bin)
if [ "$SIZE" -eq 16919 ]; then
  echo "✓ PASS: Received correct 16919-byte payload"
  exit 0
else
  echo "✗ FAIL: Received $SIZE bytes (expected 16919)"
  exit 1
fi
```

---

## Production Rollout After Fix

1. Deploy updated code with new logging
2. Delete old S3 objects at known bad resolutions
3. Clear database metadata for affected chunks
4. Monitor logs for new generations
5. Verify first batch of regenerated chunks are 16919 bytes
6. Confirm client-side gets correct payload sizes
7. Remove old S3 object checking from monitoring (they're gone)
8. Add new S3 object size validation (optional safety feature)
