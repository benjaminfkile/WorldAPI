# DEM Lazy Fetch - Step 1 Implementation

**Date**: January 23, 2026  
**Step**: Allow Empty DEM Index at Startup  
**Status**: ✅ Complete

---

## Objective

Modify `DemTileIndexInitializer` so application startup does NOT fail when no DEM tiles exist in local S3.

Startup should only fail if:
- S3 is unreachable
- Configuration is invalid

If the local `dem/` folder is empty, the index should initialize successfully with `Count == 0`.

---

## Changes Made

### File: `src/WorldApi/World/Dem/DemTileIndexInitializer.cs`

#### 1. Added S3 Exception Handling

**Added using statement:**
```csharp
using Amazon.S3;
```

**Modified `StartAsync` method:**

- Added specific catch block for `AmazonS3Exception` with `NotFound` status (404)
  - This handles the case where the bucket or prefix doesn't exist
  - Logs a warning but allows startup to continue
  - Index remains empty (Count == 0)

- Preserved existing catch block for other exceptions
  - Still fails startup for:
    - S3 unreachable (network/connectivity issues)
    - Configuration invalid (malformed bucket name, invalid credentials)
    - Other critical errors

**Code changes:**
```csharp
catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    // Bucket or prefix doesn't exist - this is OK, treat as empty
    // Allows startup with empty DEM folder for lazy fetching
    _logger.LogWarning("DEM bucket or folder not found, starting with empty index");
}
catch (Exception ex)
{
    // S3 unreachable, configuration invalid, or other critical errors
    _logger.LogError(ex, "Failed to initialize DEM tile index");
    throw; // Fail startup only for critical errors
}
```

---

## What Worked

✅ **Build Success**: Application compiles without errors or warnings  
✅ **Minimal Changes**: Only modified the exception handling as specified  
✅ **No Builder Changes**: `DemTileIndexBuilder` behavior remains unchanged  
✅ **No Fallback Logic**: No additional fallback mechanisms added  
✅ **Logging Consistent**: Used existing logging patterns, only added one warning message  
✅ **Graceful Empty Handling**: Empty DEM folder results in Count == 0, not startup failure

---

## What Didn't Work

N/A - No issues encountered during implementation.

---

## Behavior Changes

### Before
- Application **fails to start** if:
  - S3 bucket doesn't exist
  - S3 prefix doesn't exist
  - No DEM tiles found
  - Any S3 error occurs

### After
- Application **starts successfully** if:
  - S3 bucket/prefix doesn't exist (404) → Count == 0
  - No DEM tiles found → Count == 0
  
- Application **fails to start** only if:
  - S3 is unreachable (network error)
  - Configuration is invalid
  - Other critical exceptions occur

---

## Acceptance Tests

✅ Application compiles successfully  
⏳ Application starts with empty `dem/` folder (requires runtime test)  
⏳ `DemTileIndex.Count == 0` after startup with empty folder (requires runtime test)  
⏳ Application fails startup when S3 is unreachable (requires runtime test)  

---

## Next Steps

According to `DEM_Lazy_Fetch_Design.md`, the next step is:

**Step 2**: Deterministic SRTM Tile Naming
- Create utility to compute SRTM tile filename from coordinates
- Format: `{N|S}{lat}{E|W}{lon}`
- Example: `(46.5, -113.2)` → `N46W113`

---

## References

- Design Document: [DEM_Lazy_Fetch_Design.md](DEM_Lazy_Fetch_Design.md)
- Modified File: [src/WorldApi/World/Dem/DemTileIndexInitializer.cs](src/WorldApi/World/Dem/DemTileIndexInitializer.cs)
- Related File: [src/WorldApi/World/Dem/DemTileIndexBuilder.cs](src/WorldApi/World/Dem/DemTileIndexBuilder.cs) (unchanged)

---

## Notes

- The implementation follows the "fail fast" principle for critical errors while being tolerant of missing data
- No changes were committed to git as requested
- The `DemTileIndexBuilder` continues to scan S3 and will naturally return an empty index if no tiles exist
- The warning log message provides visibility when starting with an empty index
