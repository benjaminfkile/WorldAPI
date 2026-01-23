# DEM Fetching and Indexing at Startup

## Overview

The DEM (Digital Elevation Model) system in WorldAPI uses SRTM (Shuttle Radar Topography Mission) data stored as HGT tiles in S3. At application startup, the system performs a critical initialization sequence to:

1. **Scan S3 for available DEM tiles** - Lists all `.hgt` files in the S3 bucket
2. **Parse tile metadata** - Extracts geographic bounds from SRTM filename convention
3. **Build a searchable index** - Creates an in-memory index for fast tile lookup by coordinates
4. **Prepare for terrain generation** - Makes the index available for chunk generation requests

---

## Startup Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                   Application Startup                        │
│                  (Program.cs)                                │
└─────────────────────────────────────────────────────────────┘
         │
         ├─→ DemTileIndex (Singleton)
         │   └─ In-memory searchable index
         │
         ├─→ DemTileIndexBuilder (Singleton)
         │   └─ Fetches tiles from S3
         │
         ├─→ DemTileIndexInitializer (HostedService)
         │   └─ Triggered at app startup
         │       ├─ Calls DemTileIndexBuilder.BuildAsync()
         │       └─ Populates DemTileIndex with tiles
         │
         ├─→ HgtTileCache (Singleton)
         │   └─ Runtime cache for decoded tile data
         │
         └─→ HgtTileLoader (Singleton)
             └─ Loads and decodes HGT tiles on-demand
```

---

## Startup Flow

### 1. **Program.cs Registration** (Lines 179-212)

When the ASP.NET Core application starts, `Program.cs` registers the DEM-related services:

```csharp
// Register singleton DemTileIndex
builder.Services.AddSingleton<DemTileIndex>();

// Register DemTileIndexBuilder factory
builder.Services.AddSingleton<DemTileIndexBuilder>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName 
        ?? throw new InvalidOperationException("S3 bucket name not configured");
    return new DemTileIndexBuilder(s3Client, bucketName);
});

// Register DemTileIndexInitializer as hosted service
builder.Services.AddHostedService<DemTileIndexInitializer>();
```

**Key Points:**
- `DemTileIndex` is registered as a **singleton** to maintain state across requests
- `DemTileIndexBuilder` requires the S3 bucket name from app secrets
- `DemTileIndexInitializer` is a **hosted service** that runs when the app starts

### 2. **HostedService Initialization** (DemTileIndexInitializer.StartAsync)

When the application is ready to start, ASP.NET Core calls `DemTileIndexInitializer.StartAsync()`:

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    try
    {
        // 1. Build index by scanning S3
        var populatedIndex = await _builder.BuildAsync();
        
        // 2. Copy tiles into the singleton index
        foreach (var tile in populatedIndex.GetAllTiles())
        {
            _index.Add(tile);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to initialize DEM tile index");
        throw;  // Fail startup if index can't be built
    }
}
```

**Execution Timing:** This runs **after** the DI container is built but **before** the app accepts HTTP requests.

**Failure Behavior:** If the index fails to build, the application startup fails completely. This is intentional—terrain generation requires a valid DEM index.

### 3. **S3 Scanning** (DemTileIndexBuilder.BuildAsync)

The builder fetches all `.hgt` files from S3:

```csharp
public async Task<DemTileIndex> BuildAsync()
{
    var index = new DemTileIndex();
    
    var request = new ListObjectsV2Request
    {
        BucketName = _bucketName,
        Prefix = "dem/srtm/"  // Lists only S3 objects under dem/srtm/
    };
    
    ListObjectsV2Response response;
    do
    {
        // Paginated S3 listing (handles buckets with >1000 objects)
        response = await _s3Client.ListObjectsV2Async(request);
        
        foreach (var s3Object in response.S3Objects)
        {
            // Filter for .hgt files only
            if (s3Object.Key.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase))
            {
                // Extract filename: "dem/srtm/N46W113.hgt" → "N46W113.hgt"
                string filename = Path.GetFileName(s3Object.Key);
                
                // Parse geographic bounds from filename
                var tile = SrtmFilenameParser.Parse(filename);
                
                // Update with full S3 key path for later fetching
                var tileWithFullKey = tile with { S3Key = s3Object.Key };
                index.Add(tileWithFullKey);
            }
        }
        
        request.ContinuationToken = response.NextContinuationToken;
    }
    while (response.IsTruncated == true);
    
    return index;
}
```

**Process Steps:**
1. Issues `ListObjectsV2` request with prefix `dem/srtm/`
2. Iterates through paginated results (pagination handles large tile sets)
3. For each `.hgt` file, extracts the filename
4. Parses the filename to extract geographic metadata
5. Stores the full S3 key path for later retrieval

**Note:** This **does not download tile data**—it only indexes metadata. Actual HGT file downloads happen later on-demand.

### 4. **Filename Parsing** (SrtmFilenameParser.Parse)

The SRTM filename convention encodes geographic coordinates directly in the filename:

```
Format: [N|S][latitude][E|W][longitude].hgt
Example: N46W113.hgt
```

Parsing logic:
```csharp
public static DemTile Parse(string filename)
{
    // Remove .hgt extension: "N46W113.hgt" → "N46W113"
    string name = filename.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase)
        ? filename[..^4]
        : filename;
    
    // Parse latitude: [N|S][digits]
    char latDir = char.ToUpperInvariant(name[0]);  // 'N' or 'S'
    int latEndIndex = 1;
    while (latEndIndex < name.Length && char.IsDigit(name[latEndIndex]))
        latEndIndex++;
    
    int latValue = int.Parse(name[1..latEndIndex]);  // 46
    double lat = latDir == 'S' ? -latValue : latValue;  // 46° N
    
    // Parse longitude: [E|W][digits]
    char lonDir = char.ToUpperInvariant(name[latEndIndex]);  // 'W' or 'E'
    string lonString = name[(latEndIndex + 1)..];  // 113
    int lonValue = int.Parse(lonString);
    double lon = lonDir == 'W' ? -lonValue : lonValue;  // -113° W
    
    // Each tile covers exactly 1° x 1°
    return new DemTile
    {
        MinLatitude = lat,              // 46.0°
        MaxLatitude = lat + 1.0,        // 47.0°
        MinLongitude = lon,             // -113.0°
        MaxLongitude = lon + 1.0,       // -112.0°
        S3Key = filename                // "dem/srtm/N46W113.hgt"
    };
}
```

**Example Parsing:**
- `N46W113.hgt` → Tile covering 46-47°N, 113-112°W (northwestern Montana)
- `S45E010.hgt` → Tile covering -45-(-44)°N, 10-11°E (southern Africa)
- `N00E000.hgt` → Tile covering 0-1°N, 0-1°E (near equator/prime meridian)

### 5. **Index Data Structure** (DemTileIndex)

The in-memory index uses a simple dictionary for fast lookup:

```csharp
public sealed class DemTileIndex
{
    private readonly Dictionary<string, DemTile> _tiles = new();
    
    public void Add(DemTile tile)
    {
        _tiles[tile.S3Key] = tile;  // Key: "dem/srtm/N46W113.hgt"
    }
    
    public DemTile? FindTileContaining(double latitude, double longitude)
    {
        // Linear search through tiles to find one containing the coordinates
        foreach (var tile in _tiles.Values)
        {
            if (latitude >= tile.MinLatitude && latitude < tile.MaxLatitude &&
                longitude >= tile.MinLongitude && longitude < tile.MaxLongitude)
            {
                return tile;
            }
        }
        return null;
    }
    
    public IReadOnlyCollection<DemTile> GetAllTiles() => _tiles.Values;
    public int Count => _tiles.Count;
}
```

**Data Model (DemTile):**
```csharp
public sealed record DemTile
{
    public required double MinLatitude { get; init; }    // 46.0
    public required double MaxLatitude { get; init; }    // 47.0
    public required double MinLongitude { get; init; }   // -113.0
    public required double MaxLongitude { get; init; }   // -112.0
    public required string S3Key { get; init; }          // "dem/srtm/N46W113.hgt"
}
```

---

## Runtime Usage

### Terrain Chunk Generation (TerrainChunkGenerator)

Once indexed, the DEM tiles are used to generate terrain chunks:

```csharp
public sealed class TerrainChunkGenerator
{
    private readonly DemTileIndex _tileIndex;
    private readonly HgtTileCache _tileCache;
    private readonly HgtTileLoader _tileLoader;
    
    public TerrainChunkGenerator(
        WorldCoordinateService coordinateService,
        DemTileIndex tileIndex,        // Injected singleton index
        HgtTileCache tileCache,
        HgtTileLoader tileLoader,
        ...
    )
    {
        _tileIndex = tileIndex;
        _tileCache = tileCache;
        _tileLoader = tileLoader;
    }
    
    public async Task<TerrainChunk> GenerateAsync(...)
    {
        // 1. Find which DEM tile contains this coordinate
        var demTile = _tileIndex.FindTileContaining(latitude, longitude);
        if (demTile == null)
            throw new InvalidOperationException($"No DEM tile found for {latitude}, {longitude}");
        
        // 2. Check if tile data is already cached
        if (!_tileCache.TryGet(demTile.S3Key, out var cachedTileData))
        {
            // 3. Load from S3 if not cached
            var tileData = await _tileLoader.LoadAsync(demTile);
            _tileCache.Add(demTile.S3Key, tileData);
        }
        
        // 4. Sample elevation from tile data
        double elevation = DemSampler.SampleElevation(latitude, longitude, tileData);
        
        // ... generate terrain chunk at this elevation
    }
}
```

### Data Flow

```
Request: GET /api/chunks/terrain?lat=46.5&lon=-112.5
    ↓
TerrainChunkGenerator.GenerateAsync()
    ↓
DemTileIndex.FindTileContaining(46.5, -112.5)
    ↓ Returns: { S3Key: "dem/srtm/N46W113.hgt", MinLat: 46.0, ... }
    ↓
HgtTileCache.TryGet("dem/srtm/N46W113.hgt")
    ↓
If not cached:
    ↓ HgtTileLoader.LoadAsync() → Downloads from S3
    ↓ SrtmDecoder.Decode() → Converts bytes to elevation data
    ↓ HgtTileCache.Add() → Caches for future requests
    ↓
DemSampler.SampleElevation() → Bilinear interpolation
    ↓ Returns: 1847.5 meters (example)
    ↓
TerrainChunkGenerator uses elevation for terrain mesh generation
```

---

## Performance Characteristics

### Index Building (Startup)

| Aspect | Details |
|--------|---------|
| **Scope** | Metadata only (no data download) |
| **S3 Operations** | `ListObjectsV2` with pagination |
| **Memory Usage** | ~200 bytes per tile (~14 KB for ~70 SRTM1 tiles) |
| **Time** | ~1-5 seconds (depends on S3 latency & tile count) |
| **Failure Behavior** | Application startup fails if index can't be built |

### Tile Loading (Runtime)

| Aspect | Details |
|--------|---------|
| **Scope** | Full tile data download & decode |
| **File Size** | SRTM1: ~2.6 MB per tile; SRTM3: ~385 KB per tile |
| **Caching** | `HgtTileCache` (ConcurrentDictionary) |
| **Memory per Tile** | ~10 MB (SRTM1: 3601×3601 shorts); ~1.5 MB (SRTM3: 1201×1201) |
| **First Load** | ~2-10 seconds (depends on tile size & S3 latency) |
| **Cached Loads** | <1 millisecond |

---

## Configuration

### S3 Bucket Structure

```
s3://your-bucket/
├── dem/
│   └── srtm/
│       ├── N46W113.hgt
│       ├── N46W112.hgt
│       ├── N45W113.hgt
│       ├── S45E010.hgt
│       └── ... (more tiles)
└── (other data)
```

### AppSecrets Configuration

```json
{
  "s3BucketName": "your-dem-bucket",
  "UseLocalS3": "false"  // For production AWS S3
}
```

Or for local development:
```json
{
  "s3BucketName": "your-dem-bucket",
  "UseLocalS3": "true",
  "LocalS3Endpoint": "http://localhost:9000",
  "LocalS3AccessKey": "minioadmin",
  "LocalS3SecretKey": "minioadmin"
}
```

---

## Error Handling

### Startup Failure Scenarios

1. **S3 Access Denied**
   - Cause: IAM role lacks S3 read permissions
   - Result: Startup fails with `AmazonServiceException`
   - Fix: Update IAM role policy to include `s3:ListBucket` and `s3:GetObject`

2. **S3 Bucket Not Found**
   - Cause: `s3BucketName` not set or incorrect in app secrets
   - Result: Startup fails with `InvalidOperationException`
   - Fix: Verify bucket name in `WorldAppSecrets`

3. **No HGT Files**
   - Cause: S3 bucket exists but `dem/srtm/` prefix contains no `.hgt` files
   - Result: Index builds successfully but is empty; requests will fail with "No DEM tile found"
   - Fix: Upload SRTM tiles to S3

### Runtime Failures

1. **Coordinate Outside All Tiles**
   - Cause: User requests terrain at coordinates not covered by indexed tiles
   - Result: `DemTileIndex.FindTileContaining()` returns null
   - Handling: TerrainChunkGenerator throws `InvalidOperationException`

2. **S3 Download Timeout**
   - Cause: Network latency or S3 performance issue
   - Result: `HgtTileLoader.LoadAsync()` throws `HttpRequestException`
   - Handling: Depends on request retry logic (not shown in DEM code)

3. **Invalid HGT Data**
   - Cause: Corrupted or malformed HGT file
   - Result: `SrtmDecoder.Decode()` throws `Exception`
   - Handling: Depends on decoder implementation

---

## Related Components

### DemSampler
- **Purpose:** Bilinear interpolation of elevation values
- **Input:** Latitude, longitude, loaded tile data
- **Output:** Interpolated elevation value
- **Called By:** `TerrainChunkGenerator`

### HgtTileCache
- **Purpose:** In-memory cache for decoded tile data
- **Type:** `ConcurrentDictionary<string, SrtmTileData>`
- **Lifetime:** Application singleton (persists across requests)
- **Eviction:** No eviction policy (memory limited by available RAM)

### HgtTileLoader
- **Purpose:** Downloads and decodes HGT files from S3
- **Process:** S3 fetch → byte array → SRTM decode
- **Caching:** Results are cached by `HgtTileCache`

### SrtmFilenameParser
- **Purpose:** Extract geographic bounds from SRTM filename convention
- **Format Handled:** `[N|S][lat][E|W][lon].hgt`
- **Assumptions:** Each tile covers exactly 1° × 1°

---

## Summary Timeline

```
Application Start
    ↓
1. Register DEM services in DI container [Program.cs]
    ↓
2. Build DI container
    ↓
3. HostedService.StartAsync() triggered
    ↓
4. DemTileIndexBuilder.BuildAsync()
    ├─ ListObjectsV2(prefix="dem/srtm/")
    ├─ For each .hgt file:
    │  ├─ SrtmFilenameParser.Parse(filename)
    │  └─ DemTileIndex.Add(tile)
    └─ Return populated index
    ↓
5. Copy tiles to singleton DemTileIndex
    ↓
6. Application ready to accept requests
    ↓
7. On terrain chunk request:
    ├─ DemTileIndex.FindTileContaining(lat, lon)
    ├─ HgtTileLoader.LoadAsync() [if not cached]
    ├─ DemSampler.SampleElevation()
    └─ Return terrain chunk
```

---

## Key Insights

1. **Index is metadata-only** - Startup only reads S3 object metadata, not actual tile data
2. **Lazy loading** - Actual HGT files are downloaded on first access and cached
3. **Fail-fast startup** - If DEM index can't be built, the app won't start (intentional)
4. **Simple lookup** - Linear search through tiles (acceptable for ~70 tiles; could optimize with spatial indexing)
5. **Thread-safe caching** - `HgtTileCache` uses `ConcurrentDictionary` for thread-safety
6. **Coordinates are lat-first** - `FindTileContaining(latitude, longitude)` not `(x, y)` or `(lon, lat)`
