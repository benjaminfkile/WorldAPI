-- Migration: Add dem_tiles table for tracking DEM readiness per tile per world version
-- Purpose: Tracks DEM tile status (missing, downloading, ready, failed) and coordinates S3 storage
-- Up: Creates dem_tiles table with indexes and constraints
-- Down: Removes dem_tiles table

-- ============================================================================
-- UP: Create dem_tiles table
-- ============================================================================

CREATE TABLE IF NOT EXISTS dem_tiles (
    id BIGSERIAL PRIMARY KEY,
    world_version_id BIGINT NOT NULL,
    tile_key TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'missing',
    s3_key TEXT,
    last_error TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Foreign key to world_versions
    CONSTRAINT fk_dem_tiles_world_version_id FOREIGN KEY (world_version_id)
        REFERENCES world_versions(id) ON DELETE CASCADE,
    
    -- Unique constraint: Only one entry per (world_version_id, tile_key)
    -- This prevents duplicate downloads for the same tile in the same world
    CONSTRAINT unique_dem_tile_per_world UNIQUE (world_version_id, tile_key),
    
    -- Enforce valid status values
    CONSTRAINT check_dem_status CHECK (status IN ('missing', 'downloading', 'ready', 'failed'))
);

-- ============================================================================
-- Indexes for query performance
-- ============================================================================

-- Lookup by world_version_id for migration/bulk operations
CREATE INDEX IF NOT EXISTS idx_dem_tiles_world_version_id ON dem_tiles(world_version_id);

-- Lookup tiles by status for worker polling (find missing or downloading tiles)
CREATE INDEX IF NOT EXISTS idx_dem_tiles_status ON dem_tiles(status);

-- Combined index for finding all missing tiles in a world (worker query pattern)
CREATE INDEX IF NOT EXISTS idx_dem_tiles_world_status ON dem_tiles(world_version_id, status);

-- Index for finding a specific tile quickly (common check-before-insert pattern)
CREATE INDEX IF NOT EXISTS idx_dem_tiles_tile_key ON dem_tiles(tile_key);

-- ============================================================================
-- Trigger to auto-update updated_at timestamp
-- ============================================================================

CREATE OR REPLACE FUNCTION update_dem_tiles_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_dem_tiles_update_timestamp ON dem_tiles;

CREATE TRIGGER trg_dem_tiles_update_timestamp
BEFORE UPDATE ON dem_tiles
FOR EACH ROW
EXECUTE FUNCTION update_dem_tiles_timestamp();

-- ============================================================================
-- DOWN: Revert changes (uncomment to rollback)
-- ============================================================================
-- DROP TRIGGER IF EXISTS trg_dem_tiles_update_timestamp ON dem_tiles;
-- DROP FUNCTION IF EXISTS update_dem_tiles_timestamp();
-- DROP TABLE IF EXISTS dem_tiles;
