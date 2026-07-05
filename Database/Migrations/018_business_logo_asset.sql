-- 018: Store the business logo centrally so every POS terminal can cache it locally.

ALTER TABLE business_info
    ADD COLUMN IF NOT EXISTS logo_file_name VARCHAR(255) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS logo_mime_type VARCHAR(100) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS logo_content_hash CHAR(64) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS logo_data MEDIUMBLOB NULL;

CREATE INDEX IF NOT EXISTS idx_business_logo_hash ON business_info (logo_content_hash);
