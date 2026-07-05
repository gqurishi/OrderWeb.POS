-- OrderWeb POS production schema
-- 019: Preserve staff clock/report history by deactivating users instead of deleting them.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE AFTER role;

UPDATE users
SET is_active = TRUE
WHERE is_active IS NULL;
