-- 028: Separate table service charges from delivery fees and provide auditable policy snapshots.

CREATE TABLE IF NOT EXISTS table_service_charge_settings (
    id TINYINT UNSIGNED NOT NULL DEFAULT 1,
    is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    percentage DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    classification ENUM('optional', 'compulsory') NOT NULL DEFAULT 'optional',
    updated_by_user_id INT NULL,
    updated_by_name VARCHAR(150) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    CONSTRAINT chk_table_service_charge_settings_singleton CHECK (id = 1),
    CONSTRAINT chk_table_service_charge_settings_percentage CHECK (percentage >= 0.00 AND percentage <= 100.00)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO table_service_charge_settings
    (id, is_enabled, percentage, classification, updated_by_user_id, updated_by_name)
VALUES
    (1, FALSE, 0.00, 'optional', NULL, NULL)
ON DUPLICATE KEY UPDATE id = VALUES(id);

CREATE TABLE IF NOT EXISTS table_service_charge_setting_events (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    previous_is_enabled BOOLEAN NULL,
    new_is_enabled BOOLEAN NOT NULL,
    previous_percentage DECIMAL(5,2) NULL,
    new_percentage DECIMAL(5,2) NOT NULL,
    previous_classification ENUM('optional', 'compulsory') NULL,
    new_classification ENUM('optional', 'compulsory') NOT NULL,
    changed_by_user_id INT NULL,
    changed_by_name VARCHAR(150) NOT NULL,
    changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_table_service_charge_event_percentage CHECK (new_percentage >= 0.00 AND new_percentage <= 100.00),
    INDEX idx_table_service_charge_setting_events_time (changed_at),
    INDEX idx_table_service_charge_setting_events_user (changed_by_user_id, changed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS service_charge_percentage DECIMAL(5,2) NOT NULL DEFAULT 0.00 AFTER delivery_fee,
    ADD COLUMN IF NOT EXISTS service_charge_basis DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER service_charge_percentage,
    ADD COLUMN IF NOT EXISTS service_charge_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER service_charge_basis,
    ADD COLUMN IF NOT EXISTS service_charge_status ENUM('not_configured', 'applied', 'removed') NOT NULL DEFAULT 'not_configured' AFTER service_charge_amount,
    ADD COLUMN IF NOT EXISTS service_charge_classification ENUM('optional', 'compulsory') NULL AFTER service_charge_status,
    ADD COLUMN IF NOT EXISTS service_charge_removal_reason VARCHAR(255) NULL AFTER service_charge_classification,
    ADD COLUMN IF NOT EXISTS service_charge_removed_by_user_id INT NULL AFTER service_charge_removal_reason,
    ADD COLUMN IF NOT EXISTS service_charge_removed_by_name VARCHAR(150) NULL AFTER service_charge_removed_by_user_id,
    ADD COLUMN IF NOT EXISTS service_charge_approved_by_user_id INT NULL AFTER service_charge_removed_by_name,
    ADD COLUMN IF NOT EXISTS service_charge_approved_by_name VARCHAR(150) NULL AFTER service_charge_approved_by_user_id,
    ADD COLUMN IF NOT EXISTS service_charge_removed_at DATETIME NULL AFTER service_charge_approved_by_name,
    ADD INDEX IF NOT EXISTS idx_orders_service_charge_reporting (order_type, service_charge_status, created_at),
    ADD INDEX IF NOT EXISTS idx_orders_service_charge_removal (service_charge_removed_at, service_charge_approved_by_user_id);

CREATE TABLE IF NOT EXISTS order_service_charge_events (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    event_type ENUM('applied', 'recalculated', 'removed', 'restored') NOT NULL,
    service_charge_percentage DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    service_charge_basis DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    service_charge_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    service_charge_classification ENUM('optional', 'compulsory') NULL,
    reason VARCHAR(255) NULL,
    performed_by_user_id INT NULL,
    performed_by_name VARCHAR(150) NOT NULL,
    approved_by_user_id INT NULL,
    approved_by_name VARCHAR(150) NULL,
    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    metadata_json JSON NULL,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    INDEX idx_order_service_charge_events_order_time (order_id, event_at),
    INDEX idx_order_service_charge_events_type_time (event_type, event_at),
    INDEX idx_order_service_charge_events_approver (approved_by_user_id, event_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
