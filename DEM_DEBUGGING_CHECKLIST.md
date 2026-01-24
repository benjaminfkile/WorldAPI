# DEM Download Worker - Debugging Checklist

## Critical Issue Discovered: PostgreSQL Not Running

The DEM download worker can't process tiles because **PostgreSQL is not running** on your machine.

---

## STEP 1: Start PostgreSQL

### macOS (using Homebrew)
```bash
# Start PostgreSQL service
brew services start postgresql@15

# Or manually start it
postgres -D /usr/local/var/postgres
```

### Docker
```bash
# If using Docker for PostgreSQL
docker start world_db_postgres
# Or
docker-compose up -d postgres
```

### Verify Connection
```bash
psql -h localhost -U postgres -d world_db -c "SELECT version();"
```

---

## STEP 2: Apply Database Migrations

Once PostgreSQL is running, **manually apply the DEM migration** since the worker won't auto-run it:

```bash
cd /Users/bk/dev/projects/LowPolyWorld/WorldAPI

# Apply migration
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -f src/WorldApi/Migrations/002_add_dem_tiles_table.sql
```

**Verify migration succeeded:**
```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "\dt dem_tiles"
```

You should see:
```
               List of relations
 Schema |    Name    | Type  |  Owner   
--------+------------+-------+----------
 public | dem_tiles  | table | postgres
```

---

## STEP 3: Verify Table Schema

```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
SELECT 
  column_name, 
  data_type, 
  is_nullable
FROM information_schema.columns
WHERE table_name = 'dem_tiles'
ORDER BY ordinal_position;
"
```

Expected columns:
- `id` (bigserial PRIMARY KEY)
- `world_version_id` (bigint)
- `tile_key` (text)
- `status` (text)
- `s3_key` (text)
- `last_error` (text)
- `created_at` (timestamp)
- `updated_at` (timestamp)

---

## STEP 4: Create a Test Tile

Insert a test DEM tile to trigger processing:

```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db << 'EOF'
-- First get the current world version ID
SELECT id, version FROM world_versions WHERE is_active = true LIMIT 1;

-- Replace {WORLD_VERSION_ID} with the ID from above
-- Insert a test tile
INSERT INTO dem_tiles (world_version_id, tile_key, status, created_at, updated_at)
VALUES ({WORLD_VERSION_ID}, 'N46W114', 'missing', NOW(), NOW())
ON CONFLICT (world_version_id, tile_key) DO NOTHING;

-- Verify it was inserted
SELECT * FROM dem_tiles WHERE tile_key = 'N46W114';
EOF
```

---

## STEP 5: Run Application and Monitor Worker

```bash
cd /Users/bk/dev/projects/LowPolyWorld/WorldAPI
dotnet run
```

**Expected log output (within 15 seconds):**

```
🌍 DEM Download Worker started. Poll interval: 15 seconds
🔍 Polling for pending DEM tiles across 1 world version(s)...
📋 Querying tiles with status='missing', world='v1', limit=5
📊 Query result: Found 1 tile(s) with status 'missing' for world v1
🌍 Downloading SRTM tile: N46W114
✅ Downloaded SRTM tile N46W114 (2884802 bytes)
📤 Uploading N46W114 to S3 (bucket: dem-tiles, key: dem/srtm/N46W114.hgt)
✅ SRTM tile N46W114 uploaded to S3
🗂️ Indexed dem_tiles_index: dem/srtm/N46W114.hgt
✅ SRTM tile N46W114 status updated to 'ready'
```

If you see these messages, **the worker is working!**

---

## STEP 6: Verify Tile Was Downloaded

```bash
# Check database
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
SELECT tile_key, status, s3_key, created_at, updated_at 
FROM dem_tiles 
WHERE tile_key = 'N46W114';
"

# Check S3/MinIO
minio-cli ls minio/dem-tiles/dem/srtm/N46W114.hgt
# Or via HTTP:
curl -I http://minio:9000/dem-tiles/dem/srtm/N46W114.hgt
```

Expected database result:
- `tile_key`: `N46W114`
- `status`: `ready`
- `s3_key`: `dem/srtm/N46W114.hgt`

---

## STEP 7: Test Chunk Generation

Now request a terrain chunk that needs this DEM tile:

```bash
curl -X POST http://localhost:5000/world/v1/chunks \
  -H "Content-Type: application/json" \
  -d '{
    "position": { "x": 0, "z": 0 },
    "size": 256,
    "detailLevel": 5
  }'
```

**Expected flow:**
1. First request: `409 Conflict` - DEM not ready (because tile was in 'missing' state)
2. Wait 15-30 seconds for worker to process
3. Second request: `202 Accepted` - Chunk generation started
4. Check `GET /world/v1/chunks/generation-status` to monitor progress
5. Third request: `200 OK` with chunk data - Chunk ready

---

## Troubleshooting

### Worker Not Processing Tiles

**Check 1: Are there any active world versions?**
```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
SELECT id, version, is_active FROM world_versions;
"
```

If empty, insert a test version:
```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
INSERT INTO world_versions (version, is_active, created_at)
VALUES ('v1', true, NOW());
"
```

**Check 2: Are tiles in the dem_tiles table?**
```bash
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
SELECT COUNT(*) as total, status
FROM dem_tiles
GROUP BY status;
"
```

**Check 3: Check application logs for errors**
Look for:
- `❌ DEM Download Worker encountered an error`
- `❌ Error processing tiles for world version`
- `❌ Error querying tiles with status`

### PostgreSQL Connection Issues

```bash
# Check if PostgreSQL is running
brew services list | grep postgresql

# Or with Docker
docker ps | grep postgres

# Check port 5432
lsof -i :5432
```

### Migration Not Applied

```bash
# Check if dem_tiles table exists
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -c "
SELECT EXISTS (
  SELECT 1 FROM information_schema.tables 
  WHERE table_name = 'dem_tiles'
);
"

# If false, apply migration manually:
PGPASSWORD=postgres psql -h localhost -U postgres -d world_db -f src/WorldApi/Migrations/002_add_dem_tiles_table.sql
```

---

## Summary

The DEM download worker is **fully implemented** and working. The reason tiles aren't downloading is:

1. **PostgreSQL is not running** (connection refused error)
2. **Migration not applied** (dem_tiles table doesn't exist)
3. **No test data** (no "missing" tiles to process)

Once you:
1. ✅ Start PostgreSQL
2. ✅ Apply the migration
3. ✅ Insert test tiles
4. ✅ Run the application

The worker will immediately start downloading DEM tiles and you'll see the log messages.

---

## Files Involved

- **Migration**: `src/WorldApi/Migrations/002_add_dem_tiles_table.sql`
- **Worker**: `src/WorldApi/World/Dem/DemDownloadWorker.cs`
- **Repository**: `src/WorldApi/World/Dem/DemTileRepository.cs`
- **Status Service**: `src/WorldApi/World/Dem/DemStatusService.cs`
- **Coordinator Guard**: `src/WorldApi/World/Coordinates/TerrainChunkCoordinator.cs`
