-- OrderWeb UK address lookup settings
CREATE TABLE IF NOT EXISTS postcode_lookup_settings (
    id INT AUTO_INCREMENT PRIMARY KEY,
    provider VARCHAR(50) DEFAULT 'OrderWeb',
    orderweb_address_api_key VARCHAR(500) DEFAULT '' COMMENT 'Shared OrderWeb owp_ key',
    orderweb_base_url VARCHAR(500) DEFAULT 'https://orderweb.net',
    orderweb_address_enabled BOOLEAN DEFAULT TRUE,
    total_lookups INT DEFAULT 0,
    last_used DATETIME NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_provider (provider),
    INDEX idx_last_used (last_used)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO postcode_lookup_settings (provider, orderweb_address_enabled)
SELECT 'OrderWeb', TRUE FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM postcode_lookup_settings LIMIT 1);
