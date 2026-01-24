# World Origin Anchoring - Quick API Reference

## Endpoints

### Get World Contract

**Endpoint:** `GET /api/world-versions/{version}/contract`

**Purpose:** Retrieve the immutable world contract including geographic origin and spatial parameters.

**Parameters:**
- `version` (path) - World version string (e.g., "v1.0")

**Success Response (200):**
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

**Error Responses:**
- **404 Not Found**: Version does not exist or is not active
- **500 Internal Server Error**: Configuration or database error

---

## Implementation Details

### What Happens on Startup

1. **Database Load**: Application loads active world versions from PostgreSQL
2. **Anchor Check**: For each version, check if ANY chunks exist
3. **Anchor Generation**: If no chunks exist:
   - Create minimal-resolution anchor chunk at (0, 0)
   - Write to S3
   - Insert metadata into database
4. **Idempotent**: On subsequent boots, anchor generation is skipped (chunks already exist)

### Anchor Chunk Specifications

- **Location**: World coordinates (0, 0)
- **Resolution**: 2 (3×3 vertex grid)
- **Terrain**: Flat (all elevations = 0 meters)
- **World-Space Size**: Matches `ChunkSizeMeters` from config
- **S3 Key**: `chunks/{worldVersion}/terrain/0_0_r2.bin`
- **Database Status**: `ready` (immediately available)

### World Contract Properties

The world contract is **immutable per deployment** and includes:

| Property | Type | Description |
|----------|------|-------------|
| `version` | string | World version identifier |
| `origin.latitude` | number | Origin latitude (decimal degrees) |
| `origin.longitude` | number | Origin longitude (decimal degrees) |
| `chunkSizeMeters` | integer | Canonical chunk size in meters |
| `metersPerDegreeLatitude` | number | Conversion factor for Y-axis |
| `immutable` | boolean | Always `true`; contract is fixed |
| `description` | string | Contract documentation |

### Usage Example (Client)

```csharp
// Get world contract once at startup
var response = await httpClient.GetAsync("/api/world-versions/v1.0/contract");
var contract = await response.Content.ReadAsAsync<WorldContract>();

// Cache origin for coordinate conversion
double originLat = contract.Origin.Latitude;
double originLon = contract.Origin.Longitude;
double chunkSize = contract.ChunkSizeMeters;
double metersPerDegLat = contract.MetersPerDegreeLatitude;

// Convert world-space chunk coordinates to geographic coordinates
double chunkX = 5;  // 5 chunks east of origin
double chunkZ = 3;  // 3 chunks north of origin

double chunkLatitude = originLat + (chunkZ * chunkSize) / 111320;
double chunkLongitude = originLon + (chunkX * chunkSize) / (metersPerDegLat);
```

---

## Related Endpoints

### Get Active Versions
`GET /api/world-versions/active` - Get list of active world versions available for querying

---

## Notes

- The world contract is **cached immutable data**; clients can safely cache it for the application lifetime.
- All coordinate conversions use **flat-earth approximation** (no globe projection).
- The anchor chunk ensures the world has a fixed geographic foundation even before terrain data is streamed.
- Anchor chunk generation is **idempotent** and **safe to run on every boot**.

