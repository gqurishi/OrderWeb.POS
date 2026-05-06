-- Check if cloud configuration exists
SELECT 
    'Configuration Status' as check_type,
    CASE 
        WHEN COUNT(*) > 0 AND is_enabled = 1 THEN 'CONFIGURED & ENABLED ✅'
        WHEN COUNT(*) > 0 AND is_enabled = 0 THEN 'CONFIGURED BUT DISABLED ⚠️'
        ELSE 'NOT CONFIGURED ❌'
    END as status,
    tenant_slug as restaurant_id,
    CASE 
        WHEN LENGTH(api_key) > 0 THEN 'API Key Set ✅'
        ELSE 'API Key Missing ❌'
    END as api_key_status,
    cloud_url,
    is_enabled,
    polling_interval_seconds
FROM cloud_config
LIMIT 1;

-- If no configuration exists, show setup instructions
SELECT 
    '⚠️ SETUP REQUIRED' as message,
    'Go to Settings > OrderWeb Connect' as step_1,
    'Enter Restaurant ID (e.g., kitchen)' as step_2,
    'Enter API Key from OrderWeb.net dashboard' as step_3,
    'Click Save Settings' as step_4,
    'Click Connect to OrderWeb.net' as step_5
WHERE (SELECT COUNT(*) FROM cloud_config) = 0;

-- Quick setup: Insert default configuration (UNCOMMENT AND MODIFY)
-- INSERT INTO cloud_config (tenant_slug, api_key, cloud_url, is_enabled, polling_interval_seconds, auto_print_enabled)
-- VALUES ('kitchen', 'YOUR_API_KEY_HERE', 'https://orderweb.net/api', 1, 60, 1)
-- ON DUPLICATE KEY UPDATE 
--     tenant_slug = 'kitchen',
--     api_key = 'YOUR_API_KEY_HERE',
--     is_enabled = 1;
