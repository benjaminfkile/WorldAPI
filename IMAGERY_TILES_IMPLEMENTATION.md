# ImageryTilesController Implementation Summary

## Overview

The `ImageryTilesController` is a production-grade tile server for serving map imagery tiles via XYZ format. It implements a cache-first architecture with CloudFront integration, MapTiler upstream fallback, and fire-and-forget S3 persistence.

## Architecture

### Request Flow

```
Client Request
    ↓
[Cache Check - S3]
    ├─ HIT  → CloudFront Redirect (if enabled) OR S3 Stream
    │
    └─ MISS → Fetch MapTiler
             → Stream to Client (async)
             → Fire-and-Forget S3 Upload
             → Return 200 OK
```

### Key Design Decisions

1. **Cache-First**: Tiles are checked in S3 before any upstream request
2. **Non-Blocking**: Client never waits for S3 persistence
3. **Immutability**: Once stored, tiles are never overwritten
4. **CDN-Friendly**: 302 redirects to CloudFront when configured
5. **No Database**: Pure S3-based storage, zero metadata overhead

## Endpoint

```
GET /world/imagery/{provider}/{z}/{x}/{y}
```

**Example:**
```
GET /world/imagery/maptiler/10/341/612
```

**Parameters:**
- `provider`: Imagery provider name (currently "maptiler")
- `z`: Zoom level (0-28 for Web Mercator)
- `x`: Tile column
- `y`: Tile row

**Response:**
- **302 Found**: If CloudFront enabled and tile cached (redirect to CDN URL)
- **200 OK**: Stream binary tile data (image/webp)
- **204 No Content**: Tile coordinates out of valid range
- **400 Bad Request**: Invalid parameters
- **500 Internal Server Error**: Configuration error
- **502 Bad Gateway**: Upstream provider error

## Configuration

Add the following to AWS Secrets Manager (`WorldAppSecrets`):

```json
{
  "mapTilerApiKey": "YOUR_MAPTILER_API_KEY",
  "cloudfrontUrl": "https://d1234567890.cloudfront.net",
  "useCloudfront": "true",
  "s3BucketName": "your-bucket-name",
  "useLocalS3": "false"
}
```

**Required fields:**
- `mapTilerApiKey`: MapTiler API key for upstream requests
- `s3BucketName`: S3 bucket for tile storage

**Optional fields:**
- `cloudfrontUrl`: CloudFront distribution URL (enables CDN redirects)
- `useCloudfront`: Set to "false" to disable CloudFront redirects
- `useLocalS3`: Set to "true" to use MinIO instead of AWS S3

## S3 Storage Layout

Tiles are stored immutably in S3:

```
imagery/
  └── maptiler/
      └── {z}/
          └── {x}/
              └── {y}.webp
```

**Example:** `imagery/maptiler/10/341/612.webp`

**Metadata:**
- `immutable: true` - Marks tile as permanent
- `cached-at: <ISO8601 timestamp>` - Cache creation time
- `Cache-Control: public, max-age=31536000, immutable` - 1 year, immutable

## Response Headers

All successful responses include:

```
Cache-Control: public, max-age=31536000, immutable
Content-Type: image/webp
ETag: (from source, if available)
Content-Length: (from source)
```

## Caching Behavior

### CloudFront Enabled
```
Client Request → API → S3 Check
    ├─ HIT  → 302 Redirect to CloudFront
    └─ MISS → MapTiler → Stream to Client + Async S3 Upload
```

**Benefits:**
- Cold hit: 302 redirect (instant)
- Warm hit: Client fetches directly from CloudFront edge
- No API involvement on subsequent requests

### CloudFront Disabled
```
Client Request → API → S3 Check
    ├─ HIT  → Stream directly from S3
    └─ MISS → MapTiler → Stream to Client + Async S3 Upload
```

**Benefits:**
- Simple setup (no CloudFront required)
- Direct S3 streaming (still fast with S3 caching)

## MapTiler Integration

The controller constructs MapTiler requests dynamically:

```
https://api.maptiler.com/tiles/topo/{z}/{x}/{y}.webp?key=API_KEY
```

**Features:**
- Uses "topo" style (can be extended to support other styles)
- Forwards response headers (Content-Type, ETag, Content-Length)
- Handles upstream errors gracefully (502, 503)
- Respects MapTiler rate limits (no retry logic; returns error)

## Fire-and-Forget S3 Upload

On cache miss:

1. Fetch tile from MapTiler
2. **Stream to client immediately** (non-blocking)
3. **Parallel: Persist to S3 asynchronously**
   - Runs without awaiting
   - Logs errors but doesn't fail the request
   - Checks for existing tile before write (immutability)

**Behavior:**
- If S3 write fails: Error logged, client still gets tile
- If multiple requests race: First one to win S3 write persists
- Subsequent requests find tile in cache

## Logging

The controller logs key events:

```
[INF] Imagery tile cache hit: maptiler/10/341/612
[INF] Imagery tile cache miss: maptiler/10/341/612, fetching from upstream
[INF] Imagery tile CloudFront redirect: imagery/maptiler/10/341/612 -> https://cdn.../...
[INF] Imagery tile S3 stream: imagery/maptiler/10/341/612, ContentLength=45234
[INF] Fetching imagery tile from MapTiler: maptiler/10/341/612
[INF] Imagery tile streaming complete: maptiler/10/341/612, triggering async S3 persistence
[INF] Imagery tile persisted to S3: imagery/maptiler/10/341/612, Size=45234 bytes
[ERR] Error persisting imagery tile to S3: ... (non-fatal)
```

## Performance Characteristics

### Cache Hit (CloudFront Enabled)
- Response time: ~100-200ms (redirect only, no data transfer)
- Client hits CloudFront edge for actual tile

### Cache Hit (CloudFront Disabled)
- Response time: ~100-300ms (S3 stream)
- Direct S3 streaming with HTTP range support

### Cache Miss
- Response time: ~500ms-2s (MapTiler round-trip)
- Client gets tile, S3 upload starts in background
- Subsequent requests hit cache

## Implementation Files

- **Controller**: [ImageryTilesController.cs](src/WorldApi/Controllers/ImageryTilesController.cs)
- **Configuration**: [WorldAppSecrets.cs](src/WorldApi/Configuration/WorldAppSecrets.cs) - Added `MapTilerApiKey`
- **DI Setup**: [Program.cs](src/WorldApi/Program.cs) - Added HttpClient registration
- **Helper**: `TeeStream` (in ImageryTilesController) - Simultaneously writes to client + S3 buffer

## Testing Checklist

- [ ] CloudFront redirect working (302 + correct URL)
- [ ] S3 streaming working (correct headers + binary data)
- [ ] MapTiler fetch working (valid tiles returned)
- [ ] S3 persistence working (tile appears in bucket after miss)
- [ ] Async upload doesn't block client
- [ ] Invalid coordinates rejected (400)
- [ ] Missing MapTiler API key handled (500)
- [ ] S3 upload errors logged (non-fatal)
- [ ] Tile immutability enforced (no overwrites)
- [ ] Cache headers set correctly (31536000 seconds = 1 year)
- [ ] CloudFront disabled behavior works
- [ ] LocalS3 / MinIO integration works

## Future Enhancements

1. **Multiple Styles**: Support other MapTiler styles (satellite, outdoors, etc.)
2. **Rate Limiting**: Optional request throttling per tile
3. **Tile Analytics**: Track cache hits/misses per tile
4. **Fallback Providers**: Support Stamen, Mapbox, or other XYZ sources
5. **Tile Formats**: Support PNG, JPEG, GeoTIFF formats
6. **Batch Operations**: Support multiple tile requests in single call
7. **Cache Busting**: Endpoint to invalidate tiles (e.g., after source update)
8. **Metrics**: Prometheus-style metrics for monitoring

## References

- [MapTiler XYZ Tiles API](https://docs.maptiler.com/cloud/api/tiles/)
- [AWS S3 SDK for .NET](https://docs.aws.amazon.com/sdkfornet/v3/developer-guide/s3-apis.html)
- [Web Mercator Tile Coordinates](https://wiki.openstreetmap.org/wiki/Tiles)
- [HTTP Caching Best Practices](https://developer.mozilla.org/en-US/docs/Web/HTTP/Caching)
