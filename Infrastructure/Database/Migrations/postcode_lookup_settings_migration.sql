-- =====================================================
-- Postcode Lookup Settings Migration
-- =====================================================
-- Creates table to store postcode lookup provider settings
-- Supports Mapbox and future Custom PAF implementations
-- =====================================================

-- Create postcode_lookup_settings table
CREATE TABLE IF NOT EXISTS postcode_lookup_settings (
    id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Provider selection
    provider VARCHAR(50) DEFAULT 'Mapbox' COMMENT 'Active provider: Mapbox or Custom',
    
    -- Mapbox settings
    mapbox_api_token VARCHAR(500) DEFAULT '' COMMENT 'Mapbox secret access token',
    mapbox_enabled BOOLEAN DEFAULT TRUE COMMENT 'Enable Mapbox provider',
    
    -- Custom PAF settings (for future use)
    custom_api_url VARCHAR(500) DEFAULT '' COMMENT 'Custom PAF API endpoint URL',
    custom_auth_token VARCHAR(500) DEFAULT '' COMMENT 'Custom API authentication token',
    custom_enabled BOOLEAN DEFAULT FALSE COMMENT 'Enable custom PAF provider',
    
    -- Usage statistics
    total_lookups INT DEFAULT 0 COMMENT 'Total number of lookups performed',
    last_used DATETIME NULL COMMENT 'Last time lookup was used',
    
    -- Audit fields
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_provider (provider),
    INDEX idx_last_used (last_used)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Postcode lookup provider configuration';

-- Insert default settings (Mapbox as default provider)
INSERT INTO postcode_lookup_settings (provider, mapbox_enabled)
VALUES ('Mapbox', TRUE)
ON DUPLICATE KEY UPDATE updated_at = CURRENT_TIMESTAMP;

-- =====================================================
-- Verification Query
-- =====================================================
SELECT 'Postcode lookup settings table created successfully' AS status;
SELECT * FROM postcode_lookup_settings;
