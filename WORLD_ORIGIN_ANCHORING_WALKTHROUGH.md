# World Origin Anchoring - Feature Walkthrough

## Overview

This document walks through the **World Origin Anchoring** feature from startup to client usage.

---

## Startup Sequence

### Phase 1: Load World Versions from Database

**Location:** [src/WorldApi/Program.cs](src/WorldApi/Program.cs) - Lines ~120-130

```csharp
var activeVersions = await LoadActiveWorldVersionsFromDatabaseAsync(dataSource, logger);
```

**What happens:**
- Query PostgreSQL `world_versions` table for all rows where `is_active = true`
- Load version string, ID, and active status into memory
- Store in `activeVersions` list
- Log count of active versions

**Example output:**
```
🚀 Loading active world versions from PostgreSQL at startup...
✓ Successfully loaded 1 active world version(s) at startup
```

---

### Phase 2: Generate Anchor Chunks (if needed)

**Location:** [src/WorldApi/Program.cs](src/WorldApi/Program.cs) - Lines ~148-184

**For each active world version:**

#### Step 1: Check if Chunks Already Exist

```csharp
if (await repository.AnyChunksExistAsync(version.Version))
{
    logger.LogInformation("✓ World version '{Version}' already has chunks, skipping anchor generation", version.Version);
    continue;
}
```

**What happens:**
- Call [WorldChunkRepository.AnyChunksExistAsync()](src/WorldApi/World/Chunks/WorldChunkRepository.cs#L280)
- Query `world_chunks` table: `SELECT 1 FROM world_chunks WHERE world_version_id = @id LIMIT 1`
- Returns immediately if any chunk (any layer, any resolution) is found
- Skip generation if chunks already exist (idempotent)

**Outcomes:**
- ✓ Skip: `✓ World version 'v1.0' already has chunks, skipping anchor generation`
- ✗ Continue: Proceed to generation

#### Step 2: Generate Anchor Chunk

```csharp
var anchorChunk = anchorGenerator.GenerateAnchorChunk();
```

**What happens:** [AnchorChunkGenerator.GenerateAnchorChunk()](src/WorldApi/World/Chunks/AnchorChunkGenerator.cs#L44)

1. Create chunk at world coordinates (0, 0)
2. Set resolution to 2 (immutable)
3. Calculate grid size: (resolution + 1) = 3 → 3×3 = 9 vertices
4. Fill height array with flat terrain (all zeros)
5. Set `MinElevation = 0`, `MaxElevation = 0`
6. Return `TerrainChunk` object ready for serialization

**Log output:**
```
📍 Generating anchor chunk for world version 'v1.0'...
Generating anchor chunk at (0, 0) with resolution 2
Anchor chunk origin: Latitude=46.8721, Longitude=-113.994
✓ Anchor chunk generated: 9 vertices, all elevations = 0
```

#### Step 3: Write Chunk to S3

```csharp
var uploadResult = await chunkWriter.WriteAsync(anchorChunk, s3Key);
```

**What happens:**
- Serialize chunk to binary format (1,175 bytes for resolution 2)
- Check if object already exists in S3 (optimization: idempotent write)
- Upload to S3 with cache headers: `public, max-age=31536000, immutable`
- Return S3 key and ETag (checksum)

**S3 Key Format:** `chunks/{worldVersion}/terrain/0_0_r2.bin`

**Example:** `chunks/v1.0/terrain/0_0_r2.bin`

#### Step 4: Insert Metadata into PostgreSQL

```csharp
await repository.UpsertReadyAsync(
    anchorChunk.ChunkX,      // 0
    anchorChunk.ChunkZ,      // 0
    "terrain",               // layer
    anchorChunk.Resolution,  // 2
    version.Version,         // "v1.0"
    s3Key,                   // "chunks/v1.0/terrain/0_0_r2.bin"
    uploadResult.Checksum    // ETag
);
```

**What happens:**
- Insert row into `world_chunks` table
- Set `status = 'ready'` (chunk is immediately available)
- Set `generated_at = NOW(UTC)`
- Return inserted metadata

**Database Row:**
| Field | Value |
|-------|-------|
| `chunk_x` | 0 |
| `chunk_z` | 0 |
| `layer` | terrain |
| `resolution` | 2 |
| `world_version_id` | (lookup from version string) |
| `s3_key` | chunks/v1.0/terrain/0_0_r2.bin |
| `status` | ready |
| `generated_at` | 2024-01-23 10:45:32 UTC |

**Log output:**
```
✓ Anchor chunk persisted for world version 'v1.0': S3Key=chunks/v1.0/terrain/0_0_r2.bin
```

#### Step 5: Complete

```csharp
logger.LogInformation("✓ Anchor chunk initialization complete");
```

**Outcome:**
- Anchor chunk is now available in S3 and registered in database
- World version is anchored to geographic origin
- World is ready for client requests and lazy chunk generation

---

## Client Usage

### Step 1: Query Active Versions

```bash
curl http://localhost:5000/api/world-versions/active
```

**Response:**
```json
{
  "versions": [
    {
      "id": 1,
      "version": "v1.0",
      "isActive": true
    }
  ]
}
```

**What happens:**
- Retrieve from in-memory cache (zero-latency, no database access)
- Return list of versions clients can use

---

### Step 2: Get World Contract

```bash
curl http://localhost:5000/api/world-versions/v1.0/contract
```

**Response:**
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

**What happens:**
- Verify version is active
- Return immutable world configuration from `WorldConfig`
- Client can now anchor world-space to real-world coordinates

**Client-Side Usage:**
```csharp
// Parse response
var contract = JsonSerializer.Deserialize<WorldContract>(responseBody);

// Use origin to anchor world
double originLat = contract.Origin.Latitude;
double originLon = contract.Origin.Longitude;
double chunkSize = contract.ChunkSizeMeters;

// Convert chunk (1, 1) to geographic coordinates
double chunkX = 1, chunkZ = 1;
double latOffset = (chunkZ * chunkSize) / 111320;
double lonOffset = (chunkX * chunkSize) / contract.MetersPerDegreeLatitude;

double chunkLat = originLat + latOffset;
double chunkLon = originLon + lonOffset;

Console.WriteLine($"Chunk (1, 1) is at {chunkLat:F6}, {chunkLon:F6}");
// Output: Chunk (1, 1) is at 46.872999, -113.993101
```

---

### Step 3: Request Terrain Chunk

```bash
curl http://localhost:5000/api/terrain-chunks/v1.0/0/0?resolution=2
```

**Response:**
- Binary chunk data with 9 vertices (from anchor chunk)
- All heights = 0

**Later requests:**
```bash
curl http://localhost:5000/api/terrain-chunks/v1.0/1/0?resolution=16
```

**Response:**
- New chunk generated on-demand with higher resolution
- Lazy generation triggered by client request
- Cached in S3 and database for future requests

---

## Key Properties

### Idempotency

**On First Boot:**
```
✓ World version 'v1.0' already has chunks, skipping anchor generation
```
→ Generates anchor chunk

**On Second Boot:**
```
✓ World version 'v1.0' already has chunks, skipping anchor generation
```
→ Skips generation (anchor already exists)

**Result:** Startup is safe to run repeatedly without duplicate chunks.

### World Contract Immutability

The world contract is **immutable per deployment** because:
- Origin coordinates are fixed in `appsettings.json`
- Chunk size is fixed per deployment
- Geographic conversion parameters are fixed

Clients can safely cache this data for the application lifetime without worrying about invalidation.

### Resolution Independence

The anchor chunk uses resolution 2, but this choice:
- **Does NOT** affect world-space coordinates
- **Does NOT** affect chunk indexing (still at 0, 0)
- **Does NOT** affect geographic anchoring
- Only affects the *detail level* of the anchor chunk
- Higher resolutions are generated lazily on demand

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      APPLICATION STARTUP                     │
└────────────────────────────────────────┬──────────────────────┘
                                         │
                    ┌────────────────────┴────────────────────┐
                    │                                        │
        ┌───────────▼──────────────┐        ┌────────────────▼───────┐
        │ Load Active Versions     │        │ Register Version Cache │
        │ from PostgreSQL          │        │                        │
        └───────────┬──────────────┘        └────────────────────────┘
                    │
                    │ ┌────────────────────────────────────┐
                    │ │ For Each Active Version:           │
                    │ │ - Check if chunks exist            │
                    │ │ - If yes: skip                     │
                    │ │ - If no: generate anchor chunk     │
                    │ └────────────────┬───────────────────┘
                    │                  │
         ┌──────────┴──────────────┬───┴────────┬──────────────┐
         │                         │            │              │
    ┌────▼────────┐    ┌──────────▼──┐    ┌────▼────┐    ┌────▼────────┐
    │  Generate   │    │   Write to   │    │ Insert  │    │    Database │
    │  Flat 3x3   │    │      S3      │    │ Metadata│    │   Persisted │
    │  Heightmap  │    │              │    │         │    │             │
    └─────────────┘    └──────────────┘    └─────────┘    └─────────────┘
         │                    │                    │              │
         └────────────────────┴────────────────────┴──────────────┘
                              │
                    ┌─────────▼────────────┐
                    │  WORLD ANCHORED ✓   │
                    │ Ready for Clients    │
                    └─────────────────────┘
                              │
        ┌─────────────────────┴──────────────────────┐
        │                                            │
    ┌───▼────────────────┐            ┌─────────────▼────────┐
    │ Clients Query      │            │ Clients Request      │
    │ /contract Endpoint │            │ Terrain Chunks       │
    │ Get Origin & Size  │            │ Lazy-Generated       │
    └────────────────────┘            └──────────────────────┘
```

---

## Troubleshooting

### Issue: Anchor chunk is not generated on startup

**Possible Causes:**
1. No active world versions (check `is_active` in database)
2. Chunks already exist (check `world_chunks` table)
3. S3 connection issue (check credentials and bucket)
4. Database connection issue (check PostgreSQL)

**Diagnostics:**
```bash
# Check active versions
SELECT id, version, is_active FROM world_versions;

# Check existing chunks
SELECT COUNT(*) FROM world_chunks;

# Check logs for errors during startup
```

### Issue: Contract endpoint returns 404

**Possible Causes:**
1. Version string is incorrect (check spelling)
2. Version is not active (check `is_active = true`)
3. World config not loaded (check `appsettings.json`)

**Diagnostics:**
```bash
# List active versions
curl http://localhost:5000/api/world-versions/active

# Check appsettings.json
cat src/WorldApi/appsettings.json | grep -A 5 "World"
```

---

## Summary

The **World Origin Anchoring** feature:

1. ✅ **Locks world-space to real-world coordinates** via configurable origin
2. ✅ **Generates anchor chunk on startup** if needed (idempotent)
3. ✅ **Exposes world contract to clients** via API endpoint
4. ✅ **Enables lazy chunk generation** starting from anchored (0, 0)
5. ✅ **Persists anchor to S3 and database** for durability
6. ✅ **Requires no additional configuration** (uses existing `WorldConfig`)

Clients can now deterministically map world-space to real-world geography and request chunks starting from the anchored origin.

