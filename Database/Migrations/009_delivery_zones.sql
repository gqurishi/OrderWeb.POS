-- OrderWeb POS production migration 009
-- Local delivery zone setup used by POS delivery orders.

CREATE TABLE IF NOT EXISTS delivery_zones (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    delivery_fee DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_delivery_zones_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS delivery_zone_postcodes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    zone_id INT NOT NULL,
    postcode VARCHAR(16) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY ux_delivery_zone_postcode (postcode),
    INDEX idx_delivery_zone_postcodes_zone (zone_id),
    CONSTRAINT fk_delivery_zone_postcodes_zone
        FOREIGN KEY (zone_id) REFERENCES delivery_zones(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS delivery_unassigned_postcodes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    postcode VARCHAR(16) NOT NULL,
    request_count INT NOT NULL DEFAULT 1,
    first_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY ux_delivery_unassigned_postcode (postcode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
