# DEM Readiness Gating – API Feature Design

## Goal

Prevent terrain chunk requests and generation in regions where the required DEM tile
has not yet been downloaded and stored in S3.

The API must expose a lightweight, authoritative way for the client to determine DEM
readiness and must strictly block chunk generation until DEM availability is confirmed.

---

## Non-Goals

- Do not change existing `world_chunks` semantics
- Do not infer DEM readiness from chunk rows
- Do not generate placeholder geometry
- Do not perform DEM downloads synchronously in request handlers

---

## Current State

- `world_versions` identifies active worlds
- `world_chunks` tracks chunk generation readiness
- `dem_tiles` tracks DEM availability per tile per world version
- Clients request chunks independently and at multiple resolutions

---

## Core Principle

**DEM readiness is a prerequisite for chunk generation.**

If a DEM tile is not `READY`, no terrain chunk logic may execute.

---

## DEM Tile Model

- DEM tiles are identified by a stable `tile_key` (example: `N46W113`)
- A DEM tile covers many chunk coordinates
- DEM readiness is independent of chunk resolution or layer
- DEM tiles must be downloaded, validated, and stored in S3 before chunks exist

---

## DEM Status Endpoint

### Endpoint

```
GET /world/{worldVersion}/dem/status?lat={lat}&lon={lon}
```

### Responsibilities

1. Resolve `worldVersion` to `world_version_id`
2. Compute `tile_key` from latitude and longitude
3. Query `dem_tiles` for `(world_version_id, tile_key)`

### Behavior

- **No row exists**
  - Insert row with `status = 'missing'`
  - Fire-and-forget enqueue of DEM download
  - Return `status = 'missing'`

- **Status = `missing`**
  - Ensure download is enqueued (idempotent)
  - Return `status = 'missing'`

- **Status = `downloading`**
  - Return `status = 'downloading'`

- **Status = `ready`**
  - Return `status = 'ready'`

- **Status = `failed`**
  - Return `status = 'failed'` with error metadata if available

### Response Shape

```json
{
  "tileKey": "N46W113",
  "status": "ready | missing | downloading | failed"
}
```

---

## Fire-and-Forget DEM Download

### Definition

Fire-and-forget means:
- Request threads never wait for DEM downloads
- All progress and locking is tracked via the database
- Background workers perform all heavy work

### Request Thread Rules

- Never download DEM files
- Never block on DEM availability
- Only insert, read, or enqueue based on database state

---

## Background DEM Worker

### Trigger Conditions

- First insertion of a `missing` DEM tile
- Observation of `missing` state during polling

### Worker Flow

1. Atomically claim tile:
   ```
   missing → downloading
   ```
2. Download DEM source (e.g. SRTM)
3. Validate file integrity and expected size
4. Upload DEM to S3
5. Update `dem_tiles`:
   - `status = 'ready'`
   - Set `s3_key`
   - Clear `last_error`

### Failure Handling

- On failure:
  - Set `status = 'failed'`
  - Populate `last_error`
- No tight retry loops
- Retries are manual or scheduled

---

## Chunk Generation Guard (Critical)

### Rule

**Chunk generation must not proceed unless DEM status is `ready`.**

### Enforcement

Before any chunk generation logic:

1. Resolve chunk coordinates to `tile_key`
2. Query `dem_tiles` for `(world_version_id, tile_key)`
3. If no row exists or `status != 'ready'`:
   - Return immediately
   - Do not generate geometry
   - Do not write `world_chunks` rows
   - Do not touch S3 chunk paths

### API Response When Blocked

- `204 No Content` - DEM is still downloading, client should retry
- No retries or partial data

---

## Client Integration (High Level)

1. Client spawns or moves into a new region
2. Client calls DEM status endpoint
3. If status is not `ready`:
   - ChunkManager is disabled
   - Client polls DEM status periodically
4. Once status becomes `ready`:
   - Client enables chunk loading
   - Normal chunk logic resumes

---

## Idempotency Guarantees

- Only one DEM download per `(world_version_id, tile_key)`
- Database row is the lock and job descriptor
- Concurrent requests must not trigger duplicate downloads

---

## Acceptance Criteria

- No chunk requests occur before DEM readiness
- Only one DEM download occurs per tile
- Chunk generation never runs without DEM availability
- Existing chunk logic remains unchanged when DEM is ready
- System behaves correctly under concurrent requests

---

## Implementation Notes for Copilot

- Do not refactor existing chunk logic
- Add DEM guards at the highest possible entry points
- Keep DEM status checks lightweight and synchronous
- Background DEM downloads must be asynchronous
- Prefer explicit, readable code over abstractions
