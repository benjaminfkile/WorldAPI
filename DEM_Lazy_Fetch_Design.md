# Lazy DEM Fetching & Incremental Indexing (WorldAPI)

## Goal

Enable global terrain coverage **without pre-downloading the entire planet** by lazily fetching SRTM DEM tiles on demand, caching them locally, and updating the DEM index at runtime.

Storage should grow **proportionally to how much users explore**.

---

## Non-Goals

- Preloading all global DEM data
- Streaming DEM data directly from public S3 on every request
- Supporting non-SRTM elevation datasets
- Perfect bathymetry or ocean modeling

---

## Current State (Baseline)

- Startup scans local S3 bucket under `dem/srtm/`
- Builds an in-memory `DemTileIndex` from existing `.hgt` files
- Runtime terrain generation:
  - Looks up tile in index
  - Loads tile from S3
  - Samples elevation
- If tile does not exist locally, request fails

---

## Target State

If a requested coordinate does not have a local DEM tile:

1. Compute the SRTM tile name from latitude/longitude
2. Fetch the tile from the **public SRTM S3 bucket**
3. Save the tile into **local S3**
4. Add the tile to `DemTileIndex` at runtime
5. Continue terrain generation normally

Application startup must succeed with **zero tiles present**.

---

## High-Level Architecture

```
TerrainChunkGenerator
  ↓
DemTileResolver (NEW)
  ↓
DemTileIndex (runtime mutable)
  ↓
Local S3 (cache) ←→ Public SRTM S3 (authoritative)
```

---

## Step 1 — Allow Empty DEM Index at Startup

### Changes
- Modify `DemTileIndexInitializer` so startup does **not** fail when no tiles exist
- Startup should only fail if:
  - S3 is unreachable
  - Configuration is invalid

### Acceptance Tests
- Application starts successfully with an empty `dem/` folder
- `DemTileIndex.Count == 0` after startup

---

## Step 2 — Deterministic SRTM Tile Naming

### Description
Create a utility to compute the expected SRTM tile filename directly from coordinates.

### Rules
- Latitude uses floor of value
- Longitude uses floor of value
- Prefix with N/S and E/W
- Format: `{N|S}{lat}{E|W}{lon}.hgt`

### Acceptance Tests
- `(46.5, -113.2)` → `N46W113.hgt`
- `(-12.1, 44.9)` → `S13E044.hgt`
- `(0.1, 0.1)` → `N00E000.hgt`

---

## Step 3 — Public SRTM Client

### Description
Introduce a read-only client for the public SRTM dataset.

### Responsibilities
- Fetch a tile by key from public S3
- Do not list buckets
- Do not write data

### Acceptance Tests
- Successfully downloads a known SRTM tile
- Returns a clear error for missing tiles (404)

---

## Step 4 — Local Tile Persistence

### Description
After fetching a tile from public S3, store it in local S3.

### Rules
- Write to `dem/srtm3/{tile}.hgt`
- Overwrites are allowed but should be rare
- Writes must be atomic

### Acceptance Tests
- Tile exists in local S3 after first request
- Subsequent requests do not hit public S3

---

## Step 5 — Runtime Index Mutation

### Description
After saving a tile locally, add it to `DemTileIndex` immediately.

### Rules
- Thread-safe
- Idempotent adds
- No restart required

### Acceptance Tests
- Index count increases by one after new tile
- Tile is discoverable via `FindTileContaining`

---

## Step 6 — DemTileResolver Integration

### Description
Add a new `DemTileResolver` that guarantees a tile exists locally.

### Responsibilities
- Check index
- Fetch + store + index if missing
- Return resolved `DemTile`

### Acceptance Tests
- Missing tile triggers fetch path
- Existing tile does not trigger fetch
- Concurrent requests only fetch once

---

## Step 7 — Terrain Pipeline Integration

### Description
Replace direct index access in `TerrainChunkGenerator` with `DemTileResolver`.

### Acceptance Tests
- Terrain generation succeeds for new coordinates
- Cached tiles load instantly on repeat requests

---

## Step 8 — Ocean / Missing Tile Fallback (Optional)

### Description
If a tile does not exist in public SRTM:
- Generate a synthetic flat tile at elevation 0
- Cache and index it like a normal tile

### Acceptance Tests
- Ocean coordinates return flat terrain
- Synthetic tiles are cached and reused

---

## Step 9 — Observability

### Metrics
- Tiles fetched from public SRTM
- Tiles stored locally
- Cache hit ratio

### Logs
- First-time tile fetch
- Public SRTM fetch failure
- Index mutation events

---

## Completion Criteria

- WorldAPI can serve terrain for any coordinate on Earth
- Local storage grows only as tiles are requested
- No startup dependency on preloaded DEM data
- No repeated public SRTM fetches for the same tile
