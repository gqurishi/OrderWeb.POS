-- 044: Align terminal_events with Client sync publish (event_type).
-- Older 006 tables used change_kind / event_id; live-sync used event_kind.
-- Access Save publishes features.updated into event_type (and supplies event_id when required).

ALTER TABLE terminal_events
    ADD COLUMN IF NOT EXISTS event_type VARCHAR(80) NULL AFTER id;

ALTER TABLE terminal_events
    ADD COLUMN IF NOT EXISTS entity_type VARCHAR(80) NULL;

UPDATE terminal_events
SET event_type = COALESCE(NULLIF(TRIM(event_type), ''), 'legacy')
WHERE event_type IS NULL OR TRIM(event_type) = '';
