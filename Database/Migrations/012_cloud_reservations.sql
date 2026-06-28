-- 012: Cloud reservation cache for OrderWeb POS pull-reservations integration.

CREATE TABLE IF NOT EXISTS cloud_reservations (
    id INT AUTO_INCREMENT PRIMARY KEY,
    cloud_id VARCHAR(64) NOT NULL,
    reference VARCHAR(64) NOT NULL DEFAULT '',
    reservation_date DATE NOT NULL,
    reservation_time TIME NOT NULL,
    covers INT NOT NULL DEFAULT 0,
    customer_name VARCHAR(255) NOT NULL DEFAULT '',
    customer_phone VARCHAR(64) NOT NULL DEFAULT '',
    customer_email VARCHAR(255) NOT NULL DEFAULT '',
    notes TEXT NULL,
    allergies TEXT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'confirmed',
    source VARCHAR(32) NOT NULL DEFAULT '',
    table_number VARCHAR(64) NOT NULL DEFAULT '',
    deposit_amount_pence INT NOT NULL DEFAULT 0,
    pos_seen_at DATETIME NULL,
    pos_print_status VARCHAR(32) NULL,
    cloud_created_at DATETIME NULL,
    cloud_updated_at DATETIME NULL,
    last_updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY ux_cloud_reservations_cloud_id (cloud_id),
    INDEX idx_cloud_reservations_date_time (reservation_date, reservation_time),
    INDEX idx_cloud_reservations_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reservation_sync_state (
    sync_key VARCHAR(64) PRIMARY KEY,
    last_sync_utc DATETIME NULL,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
