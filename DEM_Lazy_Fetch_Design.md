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

## Public SRTM Data Source (IMPORTANT)

### Bucket

```
elevation-tiles-prod
```

This is an AWS Open Data, **public, read-only** bucket.  
No AWS credentials are required. Access is via anonymous HTTPS GET requests.

### Dataset Layout (Verified)

SRTM tiles are stored in **Skadi layout**, organized by latitude folder:

```
skadi/{LAT_FOLDER}/{TILE}.hgt.gz
```

Where:
- `LAT_FOLDER` = first 3 characters of the tile name (e.g. `N27`, `S13`)
- `TILE` = full SRTM tile name without extension (e.g. `N27E086`)

### Example (Known-Good Tile)

```
https://s3.amazonaws.com/elevation-tiles-prod/skadi/N27/N27E086.hgt.gz
```

Notes:
- Files are **gzip-compressed**
- Missing tiles (404) are expected for oceans and void regions

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
- Latitude uses `floor(latitude)`
- Longitude uses `floor(longitude)`
- Prefix with N/S and E/W
- Zero-pad longitude to 3 digits
- Format: `{N|S}{lat}{E|W}{lon}`

### Acceptance Tests
- `(46.5, -113.2)` → `N46W113`
- `(-12.1, 44.9)` → `S13E044`
- `(0.1, 0.1)` → `N00E000`

---

## Step 3 — Public SRTM Client

### Description
Introduce a **read-only HTTP client** for the public SRTM dataset.

### Responsibilities
- Build public fetch URL using:
  ```
  https://s3.amazonaws.com/elevation-tiles-prod/skadi/{LAT_FOLDER}/{TILE}.hgt.gz
  ```
- Download `.hgt.gz`
- Decompress to raw `.hgt`
- Do not list buckets
- Do not write directly to public S3

### Acceptance Tests
- Successfully downloads and decompresses a known-good tile (`N27E086`)
- Returns a clear 404 result for missing tiles
- Does not require AWS credentials

---

## Step 4 — Local Tile Persistence

### Description
After fetching and decompressing a tile from public S3, store it in local S3.

### Rules
- Write to `dem/srtm3/{TILE}.hgt`
- Store **uncompressed** `.hgt` files
- Overwrites are allowed but should be rare
- Writes must be atomic

### Acceptance Tests
- Tile exists in local S3 after first request
- Stored file extension is `.hgt`
- Subsequent requests do not hit public SRTM

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
- Check `DemTileIndex`
- Fetch + store + index if missing
- Return resolved `DemTile`
- Prevent duplicate concurrent fetches for the same tile

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
If a tile does not exist in public SRTM (404):
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
