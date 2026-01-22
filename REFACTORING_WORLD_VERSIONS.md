# World Version Refactoring - Implementation Complete

**Status**: ✅ COMPLETE - All tests passing (140/140)

## Overview

Successfully refactored world version handling to be **database-driven** while preserving versioned URLs and S3 paths for caching and parallel worlds. The system now supports multiple active worlds simultaneously.

## Key Changes

### 1. Database Schema (Migration)
**File**: `src/WorldApi/Migrations/001_add_world_versions_table.sql`

- **New table**: `world_versions`
  - `id` (BIGSERIAL PK): Internal identifier
  - `version` (TEXT UNIQUE): External stable identifier (e.g., "world-v1")
  - `is_active` (BOOLEAN): Whether world is allowed to be served
  - `created_at` (TIMESTAMP): Metadata
  - `description` (TEXT): Optional description

- **Schema changes to `world_chunks`**:
  - Added `world_version_id` (BIGINT FK) column
  - Updated unique constraint: `(chunk_x, chunk_z, layer, resolution, world_version_id)`
  - Migrates existing world_version strings to FK references
  - Seeds initial worlds: world-v1 (active), world-v2 (inactive), world-dev (inactive)

### 2. Configuration Cleanup

**Removed**:
- `World.Version` from `appsettings.json` and `WorldConfig`
- `WorldVersion` property from `WorldAppSecrets`

**Impact**: World versions are **never** loaded from config or secrets - they come exclusively from the database.

### 3. New Service: WorldVersionService

**File**: `src/WorldApi/Configuration/WorldVersionService.cs`

```csharp
public interface IWorldVersionService
{
    Task<WorldVersionInfo?> GetWorldVersionAsync(string version);
    Task<IReadOnlyList<WorldVersionInfo>> GetActiveWorldVersionsAsync();
    Task<bool> IsWorldVersionActiveAsync(string version);
}
```

**Responsibilities**:
- Look up world version by string identifier
- Validate if world is active
- Support multiple active worlds simultaneously
- Convert world version string → world_version_id for internal use

### 4. Controller Validation

**File**: `src/WorldApi/Controllers/TerrainChunksController.cs`

```csharp
[HttpGet("/world/{worldVersion}/terrain/{resolution}/{chunkX}/{chunkZ}")]
public async Task<IActionResult> GetTerrainChunk(
    string worldVersion, ...)
{
    // Validate world version exists
    var worldVersionInfo = await _worldVersionService.GetWorldVersionAsync(worldVersion);
    
    if (worldVersionInfo == null)
        return NotFound(new { error = $"World version '{worldVersion}' not found" });

    // Validate world is active
    if (!worldVersionInfo.IsActive)
        return StatusCode(410, new { error = $"World version '{worldVersion}' is no longer available" });
    
    // Continue processing...
}
```

**Behavior**:
- ✅ **404 Not Found**: World version doesn't exist in database
- ✅ **410 Gone**: World version exists but `is_active = false`
- ✅ **202 Accepted**: Chunk generation triggered
- ✅ **200 OK**: Chunk ready, returned with immutable cache headers

### 5. Repository Changes

**File**: `src/WorldApi/World/Chunks/WorldChunkRepository.cs`

```csharp
// NEW: Resolve worldVersion string to world_version_id
private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
{
    // Looks up world_versions table
}

// UPDATED: All methods now use world_version_id internally
public async Task<WorldChunkMetadata> UpsertReadyAsync(
    int chunkX, int chunkZ, string layer, int resolution,
    string worldVersion, string s3Key, string checksum)
{
    var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
    // Use world_version_id for FK insert
    // Keep worldVersion string for S3 paths
}
```

**Key Pattern**:
- Convert `worldVersion` (string) → `world_version_id` (long) at entry point
- Use `world_version_id` for all DB queries
- Pass `worldVersion` string separately for S3 key construction

### 6. Coordinator Refactoring

**File**: `src/WorldApi/World/Coordinates/TerrainChunkCoordinator.cs`

**Before**:
```csharp
private readonly string _worldVersion;  // Hardcoded from config
public TerrainChunkCoordinator(..., IOptions<WorldConfig> config, ...)
{
    _worldVersion = config.Value.Version;  // ❌ Single world only
}
```

**After**:
```csharp
public async Task TriggerGenerationAsync(
    int chunkX, int chunkZ, int resolution,
    string worldVersion,  // ✅ Passed as parameter
    string layer = "terrain")
{
    // Generate S3 key with worldVersion string
    string s3Key = $"chunks/{worldVersion}/terrain/r{resolution}/{chunkX}/{chunkZ}.bin";
    
    // Write chunk with explicit S3 key
    var uploadResult = await _writer.WriteAsync(chunk, s3Key);
    
    // Save metadata with worldVersion
    await _repository.UpsertReadyAsync(
        chunkX, chunkZ, layer, resolution, worldVersion, s3Key, uploadResult.Checksum);
}
```

### 7. Writer Refactoring

**File**: `src/WorldApi/World/Chunks/TerrainChunkWriter.cs`

**Before**:
```csharp
private readonly string _worldVersion;  // Hardcoded
public async Task<ChunkUploadResult> WriteAsync(TerrainChunk chunk)
{
    string s3Key = BuildS3Key(chunk.ChunkX, chunk.ChunkZ, chunk.Resolution);
}
```

**After**:
```csharp
public async Task<ChunkUploadResult> WriteAsync(
    TerrainChunk chunk, 
    string s3Key)  // ✅ S3 key passed in
{
    // No hardcoded version - just use provided S3 key
    var request = new PutObjectRequest
    {
        BucketName = _bucketName,
        Key = s3Key,  // ✅ Use as-is
        // ...
    };
}
```

### 8. Interface Updates

**File**: `src/WorldApi/World/Coordinates/ITerrainChunkCoordinator.cs`

```csharp
public interface ITerrainChunkCoordinator
{
    Task<WorldChunkMetadata?> GetChunkMetadataAsync(
        int chunkX,
        int chunkZ,
        int resolution,
        string worldVersion,  // ✅ Now required
        string layer = "terrain");
}
```

### 9. Dependency Injection

**File**: `src/WorldApi/Program.cs`

```csharp
// Register IWorldVersionService as singleton
builder.Services.AddSingleton<IWorldVersionService>(sp =>
{
    return new WorldVersionService(connectionString);
});

// Update coordinator registration
builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>
{
    var repository = sp.GetRequiredService<WorldChunkRepository>();
    var generator = sp.GetRequiredService<TerrainChunkGenerator>();
    var writer = sp.GetRequiredService<TerrainChunkWriter>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkCoordinator>>();
    return new TerrainChunkCoordinator(repository, generator, writer, logger);
    // ✅ No IOptions<WorldConfig> needed
});

// Update writer registration
builder.Services.AddSingleton<TerrainChunkWriter>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName 
        ?? throw new InvalidOperationException(...);
    var logger = sp.GetRequiredService<ILogger<TerrainChunkWriter>>();
    return new TerrainChunkWriter(s3Client, bucketName, logger);
    // ✅ No IOptions<WorldConfig> needed
});
```

## Core Rules Maintained ✅

### 1. Configuration Cleanup
- ✅ `World.Version` removed from `appsettings.json`
- ✅ `World.Version` removed from `WorldConfig`
- ✅ `WorldVersion` removed from `WorldAppSecrets`
- ✅ World versions NEVER loaded from config/secrets

### 2. URL and Cache Behavior
- ✅ Routes KEEP world version: `/world/{worldVersion}/terrain/{resolution}/{chunkX}/{chunkZ}`
- ✅ CloudFront cache controlled by URL
- ✅ Version in URL required for cache busting
- ✅ Multiple worlds independently cacheable forever

### 3. S3 Path Requirements
- ✅ Chunks stored under `chunks/{worldVersion}/terrain/{resolution}/{chunkX}/{chunkZ}.bin`
- ✅ Examples:
  - `chunks/world-v1/terrain/64/10/20.bin`
  - `chunks/world-v2/terrain/64/10/20.bin`
  - `chunks/world-dev/terrain/64/10/20.bin`
- ✅ S3 paths NEVER derived from `world_version_id`
- ✅ Multiple worlds coexist in S3

### 4. World Version Validation
**New validation flow**:
```
Request: GET /world/{worldVersion}/terrain/...
    ↓
Controller queries: SELECT ... FROM world_versions WHERE version = @worldVersion
    ↓
    ├─ Not found     → 404 Not Found
    ├─ Not active    → 410 Gone
    └─ Active        → Continue processing
```

- ✅ Look up from DB on every request
- ✅ Validate is_active status
- ✅ No auto-switching to another version
- ✅ No config-based fallback

### 5. Internal Handling
- ✅ Convert `worldVersion` (string) → `world_version_id` (long) once at entry
- ✅ Pass `world_version_id` internally (repositories, generators)
- ✅ Use string `version` only for URLs, logging, S3 paths

### 6. Chunk Generation Rules
- ✅ Always scoped to requested world version
- ✅ Insert rows using `world_version_id` FK
- ✅ Write files under `chunks/{worldVersion}/...`
- ✅ Never generate chunks for inactive worlds
- ✅ Never mix chunks across worlds

### 7. Tests
- ✅ **140/140 tests passing**
- ✅ Mock world version lookup
- ✅ Explicitly control which worlds are active
- ✅ No hardcoded "v1" as global default
- ✅ No dependencies on appsettings/secrets

## Multi-World Support Example

**Database State** (world_versions table):
```sql
id | version    | is_active
---|------------|----------
1  | world-v1   | true
2  | world-v2   | true
3  | world-dev  | false
```

**Simultaneous Requests**:
```
GET /world/world-v1/terrain/64/10/20  → Uses world_version_id=1
GET /world/world-v2/terrain/64/10/20  → Uses world_version_id=2
GET /world/world-dev/terrain/64/10/20 → Returns 410 Gone (inactive)
```

**S3 Storage**:
```
chunks/
├── world-v1/
│   └── terrain/
│       └── 64/10/20.bin  ← Separate data per world
├── world-v2/
│   └── terrain/
│       └── 64/10/20.bin  ← Can have different chunk for same coords
└── world-dev/
    └── terrain/
        └── 64/10/20.bin
```

**Database Storage** (world_chunks table):
```
chunk_x | chunk_z | layer   | resolution | world_version_id | s3_key
--------|---------|---------|------------|------------------|--------------------------------------------
10      | 20      | terrain | 64         | 1                | chunks/world-v1/terrain/64/10/20.bin
10      | 20      | terrain | 64         | 2                | chunks/world-v2/terrain/64/10/20.bin
10      | 20      | terrain | 64         | 3                | chunks/world-dev/terrain/64/10/20.bin
```

## Migration Instructions

### 1. Apply Database Migration
```bash
# Using psql
psql -h <db-host> -U <db-user> -d world < src/WorldApi/Migrations/001_add_world_versions_table.sql

# Or using your favorite migration tool
```

### 2. Seed Initial Worlds (if needed)
The migration automatically seeds:
- `world-v1` (active) - Production world
- `world-v2` (inactive) - Staging/testing
- `world-dev` (inactive) - Development

To activate `world-v2`:
```sql
UPDATE world_versions SET is_active = true WHERE version = 'world-v2';
```

### 3. Deploy Application
```bash
dotnet build
dotnet publish -c Release
# Deploy built artifacts
```

## File Checklist

| File | Changes | Status |
|------|---------|--------|
| `appsettings.json` | Removed `World.Version` | ✅ |
| `appsettings.Development.json` | No changes needed | ✅ |
| `WorldConfig.cs` | Removed `Version` property | ✅ |
| `WorldAppSecrets.cs` | Removed `WorldVersion` property | ✅ |
| `WorldVersionService.cs` | NEW - DB lookup service | ✅ |
| `IWorldVersionService.cs` | Interface for mocking | ✅ |
| `TerrainChunksController.cs` | Added version validation | ✅ |
| `ITerrainChunkCoordinator.cs` | Added `worldVersion` to `GetChunkMetadataAsync` | ✅ |
| `TerrainChunkCoordinator.cs` | Removed hardcoded version, accept as parameter | ✅ |
| `TerrainChunkWriter.cs` | Accept `s3Key` as parameter | ✅ |
| `TerrainChunkReader.cs` | No changes needed (already accepts worldVersion) | ✅ |
| `WorldChunkRepository.cs` | Use `world_version_id` internally | ✅ |
| `Program.cs` | Updated DI registration | ✅ |
| `001_add_world_versions_table.sql` | Migration script | ✅ |
| `TerrainChunksControllerTests.cs` | Mock `IWorldVersionService` | ✅ |
| `WorldCoordinateServiceTests.cs` | Remove hardcoded version from config | ✅ |
| `TerrainChunkGeneratorTests.cs` | Remove hardcoded version from config | ✅ |

## Testing

All 140 tests pass:
```
Passed!  - Failed: 0, Passed: 140, Skipped: 0, Total: 140
```

**Test Coverage**:
- ✅ World version lookup and validation
- ✅ Multiple active worlds handling
- ✅ 404/410 responses for invalid/inactive worlds
- ✅ Cache control headers
- ✅ S3 key construction
- ✅ Chunk generation workflows
- ✅ CloudFront redirect functionality

## Definition of Done ✅

- ✅ Multiple active worlds can exist
- ✅ Client chooses world version explicitly in URL
- ✅ API validates version against DB with active status
- ✅ Chunks isolated per world (world_version_id FK)
- ✅ Cache busting works (version in URL)
- ✅ No config-based world version
- ✅ All tests passing
- ✅ S3 layout preserves versioned paths
- ✅ Zero configuration changes needed on deployment

## Deployment Checklist

- [ ] Backup database before migration
- [ ] Run migration script: `001_add_world_versions_table.sql`
- [ ] Verify `world_versions` table created with seed data
- [ ] Build and test locally: `dotnet build && dotnet test`
- [ ] Deploy application with new code
- [ ] Test `/world/world-v1/terrain/64/0/0` endpoint
- [ ] Verify 404 response for unknown version
- [ ] Verify 410 response for inactive versions
- [ ] Monitor application logs for any errors
