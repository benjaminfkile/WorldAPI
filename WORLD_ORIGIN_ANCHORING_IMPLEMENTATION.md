# World Origin Anchoring via First Chunk - Implementation Summary

## Overview

This feature implements **World Origin Anchoring** for the LowPolyWorld streaming system. On application startup, if a world version has no existing terrain chunks, the server generates a single, minimal-resolution anchor chunk at world coordinates (0, 0) to deterministically lock world-space to real-world latitude/longitude.

---

## What Was Implemented

### 1. **AnchorChunkGenerator Service**
**File:** [src/WorldApi/World/Chunks/AnchorChunkGenerator.cs](src/WorldApi/World/Chunks/AnchorChunkGenerator.cs)

A new dedicated service that:
- Generates exactly **one anchor chunk** at chunk coordinates (0, 0)
- Uses **minimal resolution** (2, resulting in a 3×3 vertex grid = 9 vertices total)
- Generates **flat terrain** (all heights = 0) without requiring DEM data
- Computes chunk origin from `WorldConfig` (origin latitude/longitude)
- Produces S3 keys in the standard format: `chunks/{worldVersion}/terrain/0_0_r{resolution}.bin`

**Key Design Decisions:**
- Resolution is hardcoded to 2 (immutable choice that does not affect world-space math)
- No DEM data is required or fetched
- Flat elevation keeps chunk data minimal
- Chunk world-space size matches canonical `ChunkSizeMeters`

### 2. **WorldChunkRepository Enhancement**
**File:** [src/WorldApi/World/Chunks/WorldChunkRepository.cs](src/WorldApi/World/Chunks/WorldChunkRepository.cs)

Added method:
```csharp
public async Task<bool> AnyChunksExistAsync(string worldVersion)
```

This efficiently checks if **any** chunks (any layer, any resolution) exist for a world version by:
- Looking up the `world_version_id` from the version string
- Running a single `SELECT 1 LIMIT 1` query against the `world_chunks` table
- Returning quickly whether chunks exist (no full table scan)

### 3. **Startup Integration**
**File:** [src/WorldApi/Program.cs](src/WorldApi/Program.cs)

**Location:** After world versions are loaded from the database and registered in the cache.

**Flow:**
1. Load active world versions from PostgreSQL
2. For **each** active world version:
   - Check if any chunks exist via `AnyChunksExistAsync()`
   - If chunks exist → skip (already anchored)
   - If no chunks exist:
     - Generate anchor chunk via `AnchorChunkGenerator.GenerateAnchorChunk()`
     - Write chunk to S3 via `TerrainChunkWriter`
     - Insert chunk metadata into PostgreSQL via `WorldChunkRepository.UpsertReadyAsync()`
3. Log progress at each step
4. On error, throw exception (fails fast, preventing partial startup)

**Idempotency & Safety:**
- Startup logic checks if chunks already exist before generating
- Safe to run on every boot (does nothing if anchor already present)
- Independent of client requests (no race conditions)

### 4. **WorldVersionsController Enhancement**
**File:** [src/WorldApi/Controllers/WorldVersionsController.cs](src/WorldApi/Controllers/WorldVersionsController.cs)

Added new endpoint:
```
GET /api/world-versions/{version}/contract
```

**Response Example:**
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

**Features:**
- Validates that the version is active and exists
- Returns immutable world configuration
- Includes origin coordinates (lat/lon) for world-space anchoring
- Exposes `ChunkSizeMeters` and `MetersPerDegreeLatitude` for client coordinate conversion
- Flags contract as immutable per deployment

---

## API Contract

### New Endpoint

**GET** `/api/world-versions/{version}/contract`

**Path Parameters:**
- `{version}` - World version string (e.g., "v1.0")

**Response (200 OK):**
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

**Response (404 Not Found):**
- Version does not exist or is not active

**Response (500 Internal Server Error):**
- Configuration or database error

---

## Resolution Rules

1. **Only one chunk** is generated at startup.
2. **Only one resolution** is generated at startup (fixed at 2).
3. The resolution choice is **intentionally tiny** (minimal vertex density).
4. World-space size **still matches canonical chunk size** (determined by `ChunkSizeMeters`).
5. Resolution choice **does not affect**:
   - World-space math or coordinate conversion
   - Chunk indexing (still at x=0, z=0)
   - Geographic anchoring (origin is immutable per config)
6. Higher resolutions and additional chunks are generated **lazily on demand** by client requests.

---

## Startup Behavior

### Success Scenario

```
🚀 Loading active world versions from PostgreSQL at startup...
✓ Successfully loaded 1 active world version(s) at startup
🔧 Checking if anchor chunks need to be generated...
📍 Generating anchor chunk for world version 'v1.0'...
Generating anchor chunk at (0, 0) with resolution 2
Anchor chunk origin: Latitude=46.8721, Longitude=-113.994
✓ Anchor chunk generated: 9 vertices, all elevations = 0
✓ Anchor chunk persisted for world version 'v1.0': S3Key=chunks/v1.0/terrain/0_0_r2.bin
✓ Anchor chunk initialization complete
```

### Idempotent Scenario (Anchor Already Exists)

```
🚀 Loading active world versions from PostgreSQL at startup...
✓ Successfully loaded 1 active world version(s) at startup
🔧 Checking if anchor chunks need to be generated...
✓ World version 'v1.0' already has chunks, skipping anchor generation
✓ Anchor chunk initialization complete
```

### Error Scenario

```
⚠ Anchor chunk generation failed - this may indicate a configuration issue
[Exception details logged]
[Startup terminated]
```

---

## Files Modified/Created

### Created
- [src/WorldApi/World/Chunks/AnchorChunkGenerator.cs](src/WorldApi/World/Chunks/AnchorChunkGenerator.cs) - New service

### Modified
- [src/WorldApi/Program.cs](src/WorldApi/Program.cs)
  - Added `AnchorChunkGenerator` service registration
  - Added anchor chunk initialization logic after world version loading
- [src/WorldApi/World/Chunks/WorldChunkRepository.cs](src/WorldApi/World/Chunks/WorldChunkRepository.cs)
  - Added `AnyChunksExistAsync()` method
- [src/WorldApi/Controllers/WorldVersionsController.cs](src/WorldApi/Controllers/WorldVersionsController.cs)
  - Added `GetWorldContract()` endpoint method
  - Added `WorldConfig` dependency injection

---

## Configuration

No new configuration required. The feature uses existing configuration from `appsettings.json`:

```json
{
  "World": {
    "Origin": {
      "Latitude": 46.8721,
      "Longitude": -113.994
    },
    "ChunkSizeMeters": 100,
    "MetersPerDegreeLatitude": 111320
  }
}
```

---

## Testing Checklist

- [ ] Build the project without errors
- [ ] Start the application with an empty `world_chunks` table
- [ ] Verify anchor chunk is generated at startup
- [ ] Verify anchor chunk metadata is inserted into PostgreSQL
- [ ] Verify chunk file exists in S3 at expected key
- [ ] Verify second startup skips generation (idempotent)
- [ ] Call `GET /api/world-versions/active` to confirm version is active
- [ ] Call `GET /api/world-versions/{version}/contract` to confirm contract is returned
- [ ] Verify world contract contains correct origin and chunk size
- [ ] Test that lazy chunk generation still works for higher resolutions and adjacent chunks

---

## Future Enhancements

1. **Configurable Anchor Resolution**: Make resolution configurable per deployment if needed.
2. **Anchor Chunk Metrics**: Track anchor generation performance in monitoring/observability.
3. **Cache Validation**: Periodically validate that anchor chunks remain in S3.
4. **Health Endpoint**: Expose world anchoring status in health checks.
5. **Multi-Version Anchor Generation**: Optimize batch generation for multiple world versions.

---

## Notes

- **Immutability**: Once an anchor chunk is generated for a version, it should not be deleted. The world origin is locked.
- **S3 Upload**: The anchor chunk uses the standard `TerrainChunkWriter` for S3 persistence, ensuring consistency with regular chunk uploads.
- **Database**: Anchor chunks are stored with `status='ready'` in the database, indicating immediate availability.
- **World Contract Immutability**: The world contract (origin, chunk size, conversion parameters) is immutable per deployment. Clients can cache this securely.

