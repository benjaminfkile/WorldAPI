-- Migration: Add world_versions table for database-driven world versioning
-- Up: Creates world_versions table and migrates data
-- Down: Removes world_versions table

-- ============================================================================
-- UP: Create world_versions table (authoritative for world definitions)
-- ============================================================================

CREATE TABLE IF NOT EXISTS world_versions (
    id BIGSERIAL PRIMARY KEY,
    version TEXT NOT NULL UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    description TEXT
);

CREATE INDEX IF NOT EXISTS idx_world_versions_version ON world_versions(version);
CREATE INDEX IF NOT EXISTS idx_world_versions_is_active ON world_versions(is_active);

-- ============================================================================
-- Seed initial world versions
-- 
-- The system initially seeds known versions from previous configuration.
-- In production, these should be created through proper deployment workflows.
-- ============================================================================

INSERT INTO world_versions (version, is_active, description) VALUES
    ('world-v1', true, 'Initial production world'),
    ('world-v2', false, 'Staging world for testing'),
    ('world-dev', false, 'Development world')
ON CONFLICT (version) DO NOTHING;

-- ============================================================================
-- Add world_version_id foreign key to world_chunks
-- 
-- This table now tracks which world each chunk belongs to.
-- Multiple logical partitions can exist for different worlds.
-- ============================================================================

-- Add the foreign key column (if not already present)
ALTER TABLE world_chunks
ADD COLUMN IF NOT EXISTS world_version_id BIGINT;

-- Create temporary index for migration (will drop after FK is set)
CREATE INDEX IF NOT EXISTS idx_world_chunks_temp_migration ON world_chunks(world_version);

-- Backfill world_version_id from world_version string
-- This matches world_chunks.world_version to world_versions.version to get the id
UPDATE world_chunks wc
SET world_version_id = wv.id
FROM world_versions wv
WHERE wc.world_version = wv.version
AND wc.world_version_id IS NULL;

-- Add NOT NULL constraint after backfill
ALTER TABLE world_chunks
ALTER COLUMN world_version_id SET NOT NULL;

-- Add foreign key constraint
ALTER TABLE world_chunks
ADD CONSTRAINT fk_world_chunks_world_version_id FOREIGN KEY (world_version_id)
REFERENCES world_versions(id) ON DELETE RESTRICT;

-- Drop temporary index
DROP INDEX IF EXISTS idx_world_chunks_temp_migration;

-- ============================================================================
-- Update unique constraint to include world_version_id
-- 
-- Allows multiple worlds to have the same (chunk_x, chunk_z, layer, resolution)
-- ============================================================================

-- Drop old unique constraint if it exists (it was on world_version string)
ALTER TABLE world_chunks
DROP CONSTRAINT IF NOT EXISTS world_chunks_chunk_x_chunk_z_layer_resolution_world_v_key;

-- Create new unique constraint with world_version_id
ALTER TABLE world_chunks
ADD CONSTRAINT unique_chunk_per_world UNIQUE (chunk_x, chunk_z, layer, resolution, world_version_id);

-- Create index for world version lookups
CREATE INDEX IF NOT EXISTS idx_world_chunks_world_version_id ON world_chunks(world_version_id);

-- ============================================================================
-- DOWN: Revert changes (uncomment to rollback)
-- ============================================================================
-- This would be used for rollback, but is commented by default
-- ALTER TABLE world_chunks DROP CONSTRAINT IF EXISTS fk_world_chunks_world_version_id;
-- ALTER TABLE world_chunks DROP CONSTRAINT IF EXISTS unique_chunk_per_world;
-- ALTER TABLE world_chunks DROP COLUMN IF EXISTS world_version_id;
-- DROP INDEX IF EXISTS idx_world_chunks_world_version;
-- DROP INDEX IF EXISTS idx_world_versions_is_active;
-- DROP INDEX IF EXISTS idx_world_versions_version;
-- DROP TABLE IF EXISTS world_versions;
