-- QUICK SETUP: Configure OrderWeb.net Integration
-- Replace 'kitchen' with your actual Restaurant ID
-- Replace 'your_api_key_here' with your actual API key from OrderWeb.net

-- Step 1: Check if cloud_config table exists
CREATE TABLE IF NOT EXISTS cloud_config (
    id INT PRIMARY KEY AUTO_INCREMENT,
    tenant_slug VARCHAR(100) NOT NULL,
    api_key VARCHAR(255) NOT NULL,
    cloud_url VARCHAR(255) NOT NULL DEFAULT 'https://orderweb.net/api',
    is_enabled BOOLEAN DEFAULT TRUE,
    polling_interval_seconds INT DEFAULT 60,
    auto_print_enabled BOOLEAN DEFAULT TRUE,
    db_host VARCHAR(100) DEFAULT '',
    db_name VARCHAR(100) DEFAULT '',
    db_username VARCHAR(100) DEFAULT '',
    db_password VARCHAR(255) DEFAULT '',
    db_port INT DEFAULT 0,
    connection_type VARCHAR(50) DEFAULT 'api_polling',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- Step 2: Insert or update configuration
-- IMPORTANT: Change 'kitchen' and 'pos_live_xxxxxxxxxxxx' to your actual values!
INSERT INTO cloud_config (
    tenant_slug, 
    api_key, 
    cloud_url, 
    is_enabled, 
    polling_interval_seconds, 
    auto_print_enabled,
    connection_type
)
VALUES (
    'kitchen',                              -- Your Restaurant ID from OrderWeb.net
    'pos_live_xxxxxxxxxxxx',                -- Your API Key from OrderWeb.net dashboard
    'https://orderweb.net/api',             -- API URL (usually don't change this)
    1,                                       -- Enabled (1=yes, 0=no)
    3,                                       -- Polling every 3 seconds
    1,                                       -- Auto-print enabled
    'api_polling'                            -- Connection type
)
ON DUPLICATE KEY UPDATE 
    tenant_slug = VALUES(tenant_slug),
    api_key = VALUES(api_key),
    cloud_url = VALUES(cloud_url),
    is_enabled = VALUES(is_enabled),
    polling_interval_seconds = VALUES(polling_interval_seconds),
    auto_print_enabled = VALUES(auto_print_enabled),
    connection_type = VALUES(connection_type),
    updated_at = CURRENT_TIMESTAMP;

-- Step 3: Also add to cloud_config table with proper values
INSERT INTO cloud_config (
    `key`,
    `value`
) VALUES 
    ('tenant_slug', 'kitchen'),
    ('api_key', 'pos_live_xxxxxxxxxxxx'),
    ('rest_api_url', 'https://orderweb.net/api'),
    ('websocket_url', 'wss://orderweb.net:9011'),
    ('is_enabled', 'True'),
    ('device_id', UUID())
ON DUPLICATE KEY UPDATE 
    `value` = VALUES(`value`);

-- Verify configuration
SELECT 
    '✅ CONFIGURATION SAVED!' as status,
    tenant_slug as restaurant_id,
    CONCAT(SUBSTRING(api_key, 1, 12), '...', SUBSTRING(api_key, -4)) as api_key_masked,
    cloud_url,
    CASE WHEN is_enabled = 1 THEN 'Enabled ✅' ELSE 'Disabled ❌' END as enabled,
    CONCAT(polling_interval_seconds, ' seconds') as polling_interval,
    CASE WHEN auto_print_enabled = 1 THEN 'Yes ✅' ELSE 'No ❌' END as auto_print
FROM cloud_config
LIMIT 1;

-- Next steps after running this:
SELECT 
    '📋 NEXT STEPS' as action,
    '1. Restart the POS application' as step_1,
    '2. Orders will automatically sync every 3 seconds' as step_2,
    '3. Or go to Web Orders page and click "Sync Now"' as step_3,
    '4. Check Settings > OrderWeb Connect to see status' as step_4;
