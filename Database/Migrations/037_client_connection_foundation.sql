-- OrderWeb POS production schema
-- 037: Mother API connection foundation for Client POS pairing, sessions, heartbeat, and live sync.

ALTER TABLE terminal_pairings
    ADD COLUMN IF NOT EXISTS terminal_id VARCHAR(64) NULL AFTER terminal_mode,
    ADD COLUMN IF NOT EXISTS device_type VARCHAR(80) NULL AFTER terminal_id,
    ADD COLUMN IF NOT EXISTS platform VARCHAR(80) NULL AFTER device_type,
    ADD COLUMN IF NOT EXISTS device_id VARCHAR(160) NULL AFTER platform,
    ADD COLUMN IF NOT EXISTS device_name VARCHAR(160) NULL AFTER device_id,
    ADD COLUMN IF NOT EXISTS token_hash VARCHAR(128) NULL AFTER device_name,
    ADD COLUMN IF NOT EXISTS last_seen_at DATETIME NULL AFTER paired_at,
    ADD COLUMN IF NOT EXISTS app_version VARCHAR(40) NULL AFTER last_seen_at,
    ADD COLUMN IF NOT EXISTS current_user_id INT NULL AFTER app_version,
    ADD COLUMN IF NOT EXISTS battery_status VARCHAR(80) NULL AFTER current_user_id,
    ADD COLUMN IF NOT EXISTS client_status VARCHAR(40) NULL AFTER battery_status,
    ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE AFTER client_status,
    ADD COLUMN IF NOT EXISTS last_ip_address VARCHAR(45) NULL AFTER enabled,
    ADD COLUMN IF NOT EXISTS last_sync_event_id BIGINT NOT NULL DEFAULT 0 AFTER last_ip_address,
    ADD COLUMN IF NOT EXISTS tenant_slug VARCHAR(120) NULL AFTER last_sync_event_id,
    ADD COLUMN IF NOT EXISTS restaurant_slug VARCHAR(120) NULL AFTER tenant_slug,
    ADD COLUMN IF NOT EXISTS revoked_at DATETIME NULL AFTER restaurant_slug,
    ADD COLUMN IF NOT EXISTS websocket_status VARCHAR(40) NULL AFTER revoked_at,
    ADD COLUMN IF NOT EXISTS websocket_connected_at DATETIME NULL AFTER websocket_status,
    ADD COLUMN IF NOT EXISTS websocket_disconnected_at DATETIME NULL AFTER websocket_connected_at,
    ADD COLUMN IF NOT EXISTS websocket_last_message_at DATETIME NULL AFTER websocket_disconnected_at;

CREATE INDEX IF NOT EXISTS idx_terminal_pairings_terminal_id ON terminal_pairings (terminal_id);
CREATE INDEX IF NOT EXISTS idx_terminal_pairings_token_hash ON terminal_pairings (token_hash);
CREATE INDEX IF NOT EXISTS idx_terminal_pairings_last_seen ON terminal_pairings (last_seen_at);
CREATE INDEX IF NOT EXISTS idx_terminal_pairings_enabled ON terminal_pairings (enabled);
CREATE INDEX IF NOT EXISTS idx_terminal_pairings_websocket_status ON terminal_pairings (websocket_status);

CREATE TABLE IF NOT EXISTS terminal_pairing_attempts (
    terminal_name VARCHAR(120) NOT NULL PRIMARY KEY,
    failed_attempts INT NOT NULL DEFAULT 0,
    locked_until DATETIME NULL,
    last_reason VARCHAR(80) NULL,
    last_ip_address VARCHAR(45) NULL,
    last_attempt_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_terminal_pairing_attempts_locked (locked_until),
    INDEX idx_terminal_pairing_attempts_last_attempt (last_attempt_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS client_user_sessions (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    terminal_id VARCHAR(64) NOT NULL,
    user_id INT NOT NULL,
    session_token_hash VARCHAR(128) NOT NULL,
    expires_at DATETIME NOT NULL,
    revoked_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at DATETIME NULL,
    INDEX idx_client_user_sessions_terminal (terminal_id),
    INDEX idx_client_user_sessions_user (user_id),
    INDEX idx_client_user_sessions_token (session_token_hash),
    INDEX idx_client_user_sessions_expiry (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS terminal_events (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    event_type VARCHAR(80) NOT NULL,
    entity_type VARCHAR(80) NULL,
    entity_id VARCHAR(80) NULL,
    payload_json JSON NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_terminal_events_created (created_at),
    INDEX idx_terminal_events_type (event_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
