-- 027: Keep operational caches small without destroying local accountability.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS is_archived BOOLEAN NOT NULL DEFAULT FALSE AFTER is_active,
    ADD COLUMN IF NOT EXISTS archived_at DATETIME NULL AFTER is_archived,
    ADD COLUMN IF NOT EXISTS archived_by_user_id INT NULL AFTER archived_at,
    ADD COLUMN IF NOT EXISTS archived_original_username VARCHAR(50) NULL AFTER archived_by_user_id,
    ADD INDEX IF NOT EXISTS idx_users_active_archived (is_active, is_archived);

CREATE TABLE IF NOT EXISTS local_order_deletion_audit (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    action_type ENUM('web_cache_purge', 'admin_test_delete') NOT NULL,
    original_order_db_id INT NOT NULL,
    order_id VARCHAR(100) NULL,
    order_number VARCHAR(50) NULL,
    cloud_order_id VARCHAR(100) NULL,
    source_channel VARCHAR(20) NOT NULL,
    order_status VARCHAR(40) NULL,
    lifecycle_state VARCHAR(40) NULL,
    total_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    payment_method VARCHAR(50) NULL,
    business_date DATE NOT NULL,
    deletion_reason VARCHAR(500) NOT NULL,
    deleted_by_user_id INT NULL,
    deleted_by_name VARCHAR(150) NOT NULL,
    terminal_name VARCHAR(120) NOT NULL,
    deleted_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_local_order_delete_action (action_type, original_order_db_id),
    INDEX idx_local_order_delete_date (business_date, action_type),
    INDEX idx_local_order_delete_order (order_id, order_number),
    INDEX idx_local_order_delete_actor (deleted_by_user_id, deleted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
