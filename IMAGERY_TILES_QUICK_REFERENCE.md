# Imagery Tiles Controller - Quick Reference

## Basic Usage

### Request a Tile

```bash
# Production (with CloudFront)
curl -i "https://api.example.com/world/imagery/maptiler/10/341/612"

# Development (S3 streaming)
curl -i "http://localhost:5000/world/imagery/maptiler/10/341/612"
```

### Response Examples

#### Cache Hit - CloudFront Enabled
```
HTTP/1.1 302 Found
Location: https://d1234567890.cloudfront.net/imagery/maptiler/10/341/612.webp
Cache-Control: public, max-age=31536000, immutable
```

#### Cache Hit - CloudFront Disabled
```
HTTP/1.1 200 OK
Content-Type: image/webp
Content-Length: 45234
Cache-Control: public, max-age=31536000, immutable
ETag: "abc123def456"

[binary webp data - 45234 bytes]
```

#### Cache Miss - Fetching from MapTiler
```
HTTP/1.1 200 OK
Content-Type: image/webp
Content-Length: 45234
Cache-Control: public, max-age=31536000, immutable

[binary webp data from MapTiler - 45234 bytes]
[S3 upload starts in background]
```

## Configuration

### Step 1: Set MapTiler API Key

Update your AWS Secrets Manager secret to include:

```json
{
  "mapTilerApiKey": "YOUR_ACTUAL_API_KEY",
  "s3BucketName": "your-bucket-name",
  "cloudfrontUrl": "https://d1234567890.cloudfront.net",
  "useCloudfront": "true"
}
```

### Step 2: Restart API

```bash
# The API will reload secrets on next request
# Or restart the service to pick up changes immediately
```

### Step 3: Test

```bash
curl -i "http://localhost:5000/world/imagery/maptiler/12/2048/1024"
```

## S3 Bucket Setup

### Create S3 Bucket

```bash
aws s3 mb s3://my-tiles-bucket --region us-east-1
```

### Enable CloudFront (Optional)

```bash
# Create CloudFront distribution pointing to S3 bucket
# Set origin domain to: my-tiles-bucket.s3.us-east-1.amazonaws.com
# Enable compression (gzip/brotli)
# Set default root object to: (leave blank)
```

### Verify Tile Storage

```bash
# After first request, check S3 for cached tile
aws s3 ls s3://my-tiles-bucket/imagery/maptiler/10/341/612.webp

# Or list all cached tiles
aws s3 ls s3://my-tiles-bucket/imagery/maptiler/ --recursive
```

## Zoom Level Reference

| Zoom | Scale | Coverage |
|------|-------|----------|
| 0    | 1:591M | Entire world (single tile) |
| 1    | 1:296M | World (4 tiles) |
| 5    | 1:18.5M | Continent |
| 10   | 1:1.15M | Region (USA State) |
| 15   | 1:36km | City |
| 18   | 1:4.5km | Street level |
| 20   | 1:1.1km | Detailed streets |
| 25   | 1:34m  | Building detail |
| 28   | 1:4.3m | Individual trees |

## Tile Coordinate Examples

```
# World map at zoom 1
GET /world/imagery/maptiler/1/0/0
GET /world/imagery/maptiler/1/1/0
GET /world/imagery/maptiler/1/0/1
GET /world/imagery/maptiler/1/1/1

# USA at zoom 4
GET /world/imagery/maptiler/4/2/5

# New York City area at zoom 12
GET /world/imagery/maptiler/12/1205/1539

# San Francisco area at zoom 12
GET /world/imagery/maptiler/12/655/1578

# Custom region - replace with your coordinates
GET /world/imagery/maptiler/10/341/612
```

## Monitoring

### Check Cache Hit Rate

```bash
# Count tiles in S3 cache
aws s3 ls s3://my-tiles-bucket/imagery/maptiler/ --recursive | wc -l

# Check growth over time
aws s3api list-objects-v2 --bucket my-tiles-bucket --prefix imagery/maptiler/ \
  --query 'Contents[].LastModified' | sort
```

### View Recent Logs

```bash
# View API logs for imagery requests
# (depends on your logging setup)
tail -f /var/log/worldapi/app.log | grep "Imagery tile"
```

### CloudFront Analytics

```bash
# Check CloudFront cache statistics
# AWS Console → CloudFront → Distributions → [Your Dist] → Cache Statistics
```

## Common Issues

### ❌ "Provider 'maptiler' is not supported"

**Fix:** Make sure you're using lowercase "maptiler" in the URL:
```bash
# ✓ Correct
curl http://localhost:5000/world/imagery/maptiler/10/341/612

# ✗ Wrong
curl http://localhost:5000/world/imagery/MapTiler/10/341/612
```

### ❌ "Upstream provider returned 403"

**Fix:** Check MapTiler API key in Secrets Manager:
```bash
# Verify the key is correct
curl "https://api.maptiler.com/tiles/topo/10/341/612.webp?key=YOUR_KEY"
```

### ❌ "S3 bucket name not configured"

**Fix:** Add `s3BucketName` to app secrets:
```json
{
  "s3BucketName": "my-actual-bucket-name"
}
```

### ❌ "Invalid tile coordinates"

**Fix:** Ensure coordinates are within valid range:
- Zoom: 0-28
- X, Y: 0 to 2^zoom - 1

```bash
# Example: Zoom 10 valid range is 0-1023
# ✓ Correct
curl http://localhost:5000/world/imagery/maptiler/10/512/512

# ✗ Out of range
curl http://localhost:5000/world/imagery/maptiler/10/2048/2048
```

## Performance Tips

### 1. Use Zoom Level 12-15 for Most UIs
- Fast enough for interactive maps
- Good tile coverage without too many requests
- Balanced cache hit rate

### 2. Prefetch Adjacent Tiles
```javascript
// When user moves map, prefetch neighboring tiles
const adjacentTiles = [
  [z, x-1, y-1], [z, x, y-1], [z, x+1, y-1],
  [z, x-1, y],   [z, x, y],   [z, x+1, y],
  [z, x-1, y+1], [z, x, y+1], [z, x+1, y+1]
];

adjacentTiles.forEach(([z, x, y]) => {
  // Trigger fetch to warm cache
  new Image().src = `/world/imagery/maptiler/${z}/${x}/${y}`;
});
```

### 3. Enable CloudFront
- Reduces API latency on cache hits (302 redirect)
- Offloads to AWS edge network
- Improves global performance

### 4. Use Browser Caching
```html
<!-- Tiles are already cached with immutable headers -->
<!-- Browser will cache for 1 year (31536000 seconds) -->
<img src="http://localhost:5000/world/imagery/maptiler/12/2048/1024" />
```

## Integration Examples

### Leaflet.js

```html
<link rel="stylesheet" href="https://unpkg.com/leaflet/dist/leaflet.css" />
<script src="https://unpkg.com/leaflet/dist/leaflet.js"></script>

<script>
const map = L.map('map').setView([37.7749, -122.4194], 12);

L.tileLayer('/world/imagery/maptiler/{z}/{x}/{y}', {
  maxZoom: 28,
  attribution: 'Tiles via MapTiler'
}).addTo(map);
</script>
```

### MapLibre GL JS

```javascript
const map = new maplibregl.Map({
  container: 'map',
  style: {
    version: 8,
    sources: {
      tiles: {
        type: 'raster',
        tiles: ['/world/imagery/maptiler/{z}/{x}/{y}'],
        tileSize: 256
      }
    },
    layers: [{
      id: 'tiles',
      type: 'raster',
      source: 'tiles'
    }]
  },
  center: [-122.4194, 37.7749],
  zoom: 12
});
```

### OpenLayers

```javascript
const source = new ol.source.XYZ({
  url: '/world/imagery/maptiler/{z}/{x}/{y}'
});

const layer = new ol.layer.Tile({ source: source });
const map = new ol.Map({
  target: 'map',
  layers: [layer],
  view: new ol.View({
    center: ol.proj.fromLonLat([-122.4194, 37.7749]),
    zoom: 12
  })
});
```

## API Response Status Codes

| Status | Meaning |
|--------|---------|
| 200    | Tile successfully returned (stream or from cache) |
| 204    | Tile coordinates out of valid range |
| 302    | CloudFront redirect (cached tile) |
| 400    | Invalid parameters (bad zoom, coordinates, provider) |
| 408    | Request timeout (client cancelled or exceeded 30s) |
| 500    | Configuration error (missing API key, S3 bucket, etc.) |
| 502    | Upstream provider error (MapTiler returned error) |

## Troubleshooting Checklist

- [ ] API is running and responding to requests
- [ ] MapTiler API key is valid and in Secrets Manager
- [ ] S3 bucket exists and API has read/write permissions
- [ ] CloudFront distribution is configured (if using CloudFront)
- [ ] Tile coordinates are within valid range (0-28 for zoom)
- [ ] Network connectivity to MapTiler is working
- [ ] S3 region matches bucket location
- [ ] IAM role has s3:GetObject and s3:PutObject permissions
- [ ] Check API logs for errors: `grep "Imagery tile" /var/log/worldapi/app.log`

## Performance Benchmarks

Typical response times (after S3 caching):

| Scenario | Time |
|----------|------|
| Cache hit (CloudFront) | 100-200ms |
| Cache hit (S3 stream) | 100-300ms |
| Cache miss (MapTiler fetch) | 500ms-2s |
| Parallel requests (race) | ~1s (first wins) |

All times include network latency. CloudFront enables best performance for repeated tiles.
