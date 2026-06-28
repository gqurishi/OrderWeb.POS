-- OrderWeb POS production migration 010
-- Shared OrderWeb UK postcode/address lookup settings.
-- New installs create only the OrderWeb address lookup configuration.

CREATE TABLE IF NOT EXISTS postcode_lookup_settings (
    id INT AUTO_INCREMENT PRIMARY KEY,
    provider VARCHAR(50) NOT NULL DEFAULT 'OrderWeb',
    orderweb_address_api_key VARCHAR(500) NOT NULL DEFAULT '',
    orderweb_base_url VARCHAR(500) NOT NULL DEFAULT 'https://orderweb.net',
    orderweb_address_enabled TINYINT(1) NOT NULL DEFAULT 1,
    total_lookups INT NOT NULL DEFAULT 0,
    last_used DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_provider (provider),
    INDEX idx_last_used (last_used)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO postcode_lookup_settings
    (provider, orderweb_address_api_key, orderweb_base_url, orderweb_address_enabled)
SELECT
    'OrderWeb', '', 'https://orderweb.net', 1
WHERE NOT EXISTS (SELECT 1 FROM postcode_lookup_settings LIMIT 1);
