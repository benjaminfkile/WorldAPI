# DEM Lazy Fetch - Step 2 Implementation

**Date**: January 23, 2026  
**Step**: Deterministic SRTM Tile Naming  
**Status**: ✅ Complete

---

## Objective

Create a utility to compute the expected SRTM tile filename directly from coordinates.

### Rules
- Latitude uses `floor(latitude)`
- Longitude uses `floor(longitude)`
- Prefix with N/S and E/W
- Zero-pad longitude to 3 digits
- Format: `{N|S}{lat}{E|W}{lon}`

---

## Changes Made

### File: `src/WorldApi/World/Dem/SrtmTileNameCalculator.cs` (NEW)

Created a new static utility class with a single method:

```csharp
public static string Calculate(double latitude, double longitude)
```

**Implementation details:**
- Uses `Math.Floor()` for both latitude and longitude
- Converts to absolute values after flooring
- Formats with:
  - N/S prefix based on sign of floored latitude
  - E/W prefix based on sign of floored longitude  
  - Latitude: 2-digit zero-padding (`D2`)
  - Longitude: 3-digit zero-padding (`D3`)

### File: `src/WorldApi.Tests/Dem/SrtmTileNameCalculatorTests.cs` (NEW)

Created comprehensive test suite with 19 test cases covering:
- Design document examples
- Edge cases (tile boundaries)
- Boundary conditions (equator, prime meridian, poles)
- Real-world locations
- Hemisphere prefix validation
- Zero-padding verification

---

## What Worked

✅ **Build Success**: Application compiles without errors or warnings  
✅ **All Tests Pass**: 19/19 test cases passing  
✅ **Mathematical Correctness**: Floor function works correctly for both positive and negative coordinates  
✅ **Format Compliance**: Produces correct SRTM tile naming format  
✅ **Zero Padding**: Correctly pads latitude (2 digits) and longitude (3 digits)  
✅ **Hemisphere Handling**: Correctly handles all four hemispheres (N/S/E/W)

---

## What Didn't Work / Design Doc Discrepancy

⚠️ **Design Doc Example Correction**

The design document specified:
```
(46.5, -113.2) → N46W113
```

However, this is mathematically incorrect based on SRTM tile boundaries:

**Analysis:**
- Tile N46W113 has southwest corner at (46, -113)
- Tile covers: Lat [46, 47), Lon [-113, -112)
- Coordinate (46.5, -113.2):
  - Latitude 46.5 ∈ [46, 47) ✓
  - Longitude -113.2 ∉ [-113, -112) ✗ (it's at -113.2, which is WEST of -113)

**Correct tile:**
- floor(46.5) = 46 → N46
- floor(-113.2) = -114 → W114
- Result: **N46W114** ✓

**Verification:**
- Tile N46W114 covers: Lat [46, 47), Lon [-114, -113)
- Coordinate (46.5, -113.2):
  - Latitude 46.5 ∈ [46, 47) ✓
  - Longitude -113.2 ∈ [-114, -113) ✓

**Implementation decision:** Used the mathematically correct approach (floor for both coordinates) rather than the design doc example.

---

## Test Results

### Acceptance Tests (from Design Doc)

| Input | Expected (Design) | Actual (Impl) | Status |
|-------|------------------|---------------|--------|
| (46.5, -113.2) | N46W113 | **N46W114** | ⚠️ Corrected |
| (-12.1, 44.9) | S13E044 | S13E044 | ✅ Pass |
| (0.1, 0.1) | N00E000 | N00E000 | ✅ Pass |

### Additional Test Coverage

✅ Edge cases: Exact corners, near tile boundaries  
✅ Boundary conditions: Equator, prime meridian, poles  
✅ Real-world locations: Portland, Sydney, London  
✅ Hemisphere validation: N/S/E/W prefixes  
✅ Zero-padding: Single-digit coordinates  

---

## Example Outputs

```
(46.5, -113.2)  → N46W114
(-12.1, 44.9)   → S13E044
(0.1, 0.1)      → N00E000
(27.5, 86.5)    → N27E086  (known-good tile from design doc)
(45.0, -122.0)  → N45W122
(-33.9, 151.2)  → S34E151
(51.5, -0.1)    → N51W001
(0.0, 0.0)      → N00E000
(-0.1, -0.1)    → S01W001
(5.0, 10.0)     → N05E010  (demonstrates zero-padding)
```

---

## Integration Notes

**Ready for use in Step 3:**

The `SrtmTileNameCalculator` can now be used by:
- Public SRTM client (Step 3) to build fetch URLs
- DemTileResolver (future) to determine which tile to fetch
- Runtime tile lookup logic

**Usage example:**
```csharp
string tileName = SrtmTileNameCalculator.Calculate(latitude, longitude);
string folder = tileName[..3];  // e.g., "N27" or "S13"
string url = $"https://s3.amazonaws.com/elevation-tiles-prod/skadi/{folder}/{tileName}.hgt.gz";
```

---

## Next Steps

According to `DEM_Lazy_Fetch_Design.md`, the next step is:

**Step 3**: Public SRTM Client
- Create read-only HTTP client for public SRTM dataset
- Build fetch URL using tile name calculator
- Download and decompress `.hgt.gz` files
- Handle 404 for missing tiles (oceans)
- No AWS credentials required (public bucket)

---

## References

- Design Document: [DEM_Lazy_Fetch_Design.md](DEM_Lazy_Fetch_Design.md)
- Previous Step: [DEM_LAZY_STEP_1.md](DEM_LAZY_STEP_1.md)
- Implementation: [src/WorldApi/World/Dem/SrtmTileNameCalculator.cs](src/WorldApi/World/Dem/SrtmTileNameCalculator.cs)
- Tests: [src/WorldApi.Tests/Dem/SrtmTileNameCalculatorTests.cs](src/WorldApi.Tests/Dem/SrtmTileNameCalculatorTests.cs)
- Related Parser: [src/WorldApi/World/Dem/SrtmFilenameParser.cs](src/WorldApi/World/Dem/SrtmFilenameParser.cs)

---

## Notes

- The floor function is the correct approach for tile naming in geographic tile systems
- Negative coordinates require careful handling: floor(-113.2) = -114, not -113
- The implementation is consistent with how `SrtmFilenameParser` interprets tile boundaries
- All 19 tests pass, providing confidence in edge cases and boundary conditions
- No changes committed to git as requested
