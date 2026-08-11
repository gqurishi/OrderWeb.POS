-- 029: Administrator-controlled availability for table, collection and delivery order services.

CREATE TABLE IF NOT EXISTS order_service_availability_settings (
    id TINYINT UNSIGNED NOT NULL DEFAULT 1,
    table_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    collection_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    delivery_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    updated_by_user_id INT NULL,
    updated_by_name VARCHAR(150) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    CONSTRAINT chk_order_service_availability_singleton CHECK (id = 1),
    CONSTRAINT chk_order_service_availability_one_enabled CHECK (
        table_enabled OR collection_enabled OR delivery_enabled
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO order_service_availability_settings
    (id, table_enabled, collection_enabled, delivery_enabled)
VALUES (1, TRUE, TRUE, TRUE)
ON DUPLICATE KEY UPDATE id = VALUES(id);

CREATE TABLE IF NOT EXISTS order_service_availability_events (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    previous_table_enabled BOOLEAN NOT NULL,
    new_table_enabled BOOLEAN NOT NULL,
    previous_collection_enabled BOOLEAN NOT NULL,
    new_collection_enabled BOOLEAN NOT NULL,
    previous_delivery_enabled BOOLEAN NOT NULL,
    new_delivery_enabled BOOLEAN NOT NULL,
    changed_by_user_id INT NULL,
    changed_by_name VARCHAR(150) NOT NULL,
    changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_order_service_availability_events_time (changed_at),
    INDEX idx_order_service_availability_events_user (changed_by_user_id, changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
