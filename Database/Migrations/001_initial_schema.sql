-- OrderWeb POS production schema
-- 001: Core app metadata, settings, business profile, and customer cache.

CREATE TABLE IF NOT EXISTS app_schema_version (
    id INT PRIMARY KEY DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 0,
    app_version VARCHAR(40) NULL,
    database_name VARCHAR(128) NOT NULL DEFAULT 'orderweb_pos',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CHECK (id = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO app_schema_version (id, schema_version, database_name)
VALUES (1, 0, 'orderweb_pos')
ON DUPLICATE KEY UPDATE id = id;

CREATE TABLE IF NOT EXISTS migration_history (
    id INT AUTO_INCREMENT PRIMARY KEY,
    migration_id VARCHAR(80) NOT NULL UNIQUE,
    migration_name VARCHAR(180) NOT NULL,
    checksum_sha256 CHAR(64) NULL,
    started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    finished_at DATETIME NULL,
    success TINYINT(1) NOT NULL DEFAULT 0,
    error_message TEXT NULL,
    app_version VARCHAR(40) NULL,
    executed_by VARCHAR(120) NULL,
    INDEX idx_migration_history_success (success, started_at),
    INDEX idx_migration_history_finished (finished_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS settings (
    id INT AUTO_INCREMENT PRIMARY KEY,
    setting_key VARCHAR(100) UNIQUE NOT NULL,
    setting_value TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO settings (setting_key, setting_value)
VALUES
    ('printing.config.scope', 'shared_database'),
    ('printing.ownership.mode', 'direct'),
    ('printing.queue.owner_terminal_name', 'mother'),
    ('printing.direct.enabled', 'true')
ON DUPLICATE KEY UPDATE setting_key = VALUES(setting_key);

CREATE TABLE IF NOT EXISTS cloud_config (
    id INT AUTO_INCREMENT PRIMARY KEY,
    api_base_url VARCHAR(500) NOT NULL DEFAULT 'https://orderweb.net/api',
    tenant_slug VARCHAR(255) NOT NULL DEFAULT '',
    api_key VARCHAR(500) NOT NULL DEFAULT '',
    websocket_url VARCHAR(500) NOT NULL DEFAULT '',
    connection_timeout INT DEFAULT 30,
    polling_interval_seconds INT DEFAULT 30,
    max_retry_attempts INT DEFAULT 3,
    is_enabled BOOLEAN DEFAULT FALSE,
    is_api_tested BOOLEAN DEFAULT FALSE,
    is_websocket_tested BOOLEAN DEFAULT FALSE,
    api_test_result TEXT,
    websocket_test_result TEXT,
    last_api_test TIMESTAMP NULL,
    last_websocket_test TIMESTAMP NULL,
    auto_print_enabled BOOLEAN DEFAULT TRUE,
    notifications_enabled BOOLEAN DEFAULT TRUE,
    online_order_master_enabled BOOLEAN DEFAULT TRUE,
    online_order_master_terminal_name VARCHAR(120) DEFAULT '',
    last_sync TIMESTAMP NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    cloud_url VARCHAR(500) DEFAULT 'https://orderweb.net/api/pos'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO cloud_config (tenant_slug, api_key, cloud_url, is_enabled)
SELECT '', '', 'https://orderweb.net/api/pos', FALSE
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM cloud_config LIMIT 1);

CREATE TABLE IF NOT EXISTS business_info (
    id INT PRIMARY KEY AUTO_INCREMENT,
    restaurant_name VARCHAR(255) NOT NULL DEFAULT 'Restaurant POS',
    address TEXT DEFAULT '',
    city VARCHAR(100) DEFAULT '',
    county VARCHAR(100) DEFAULT '',
    country VARCHAR(100) DEFAULT '',
    postcode VARCHAR(20) DEFAULT '',
    phone_number VARCHAR(50) DEFAULT '',
    email VARCHAR(255) DEFAULT '',
    website VARCHAR(255) DEFAULT '',
    vat_number VARCHAR(100) DEFAULT '',
    tax_code VARCHAR(100) DEFAULT '',
    description TEXT DEFAULT '',
    logo_path VARCHAR(500) DEFAULT '',
    label_printer_ip VARCHAR(45) DEFAULT NULL,
    label_printer_port INT DEFAULT 9100,
    label_printer_enabled TINYINT(1) DEFAULT 0,
    opening_till_float DECIMAL(10,2) NOT NULL DEFAULT 0,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by VARCHAR(100) DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO business_info (restaurant_name, address, phone_number, email, description)
SELECT 'Restaurant POS', '', '', '', 'Premium Dining Experience'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM business_info LIMIT 1);

CREATE TABLE IF NOT EXISTS customer_data (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_types VARCHAR(20) NOT NULL DEFAULT 'collection',
    name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50) NOT NULL,
    full_address TEXT NULL,
    city VARCHAR(100) NULL,
    county VARCHAR(100) NULL,
    postcode VARCHAR(20) NULL,
    cloud_customer_id VARCHAR(64) DEFAULT '',
    phone_normalized VARCHAR(20) DEFAULT '',
    sync_status VARCHAR(20) NOT NULL DEFAULT 'pending',
    sync_error TEXT NULL,
    cached_at DATETIME NULL,
    last_used_at DATETIME NULL,
    last_sync_at DATETIME NULL,
    points_balance INT DEFAULT 0,
    tier_level VARCHAR(50) DEFAULT '',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    last_collection_order_date DATETIME NULL,
    last_delivery_order_date DATETIME NULL,
    INDEX idx_customer_phone (phone_number),
    INDEX idx_customer_name (name),
    INDEX idx_customer_postcode (postcode),
    INDEX idx_customer_order_types (order_types),
    INDEX idx_customer_phone_norm (phone_normalized),
    INDEX idx_customer_last_used (last_used_at),
    INDEX idx_customer_sync_status (sync_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS pos_customer_sync_queue (
    queue_id INT AUTO_INCREMENT PRIMARY KEY,
    local_customer_cache_id INT NOT NULL,
    payload_json TEXT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    retry_count INT DEFAULT 0,
    next_retry_at DATETIME NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    last_error TEXT NULL,
    INDEX idx_queue_status (status),
    INDEX idx_queue_cache_id (local_customer_cache_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

