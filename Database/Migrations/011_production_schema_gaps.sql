-- OrderWeb POS production schema
-- 011: Gaps merged from legacy Infrastructure/Database/Migrations (label print, customers, orders, sync log, indexes).

ALTER TABLE FoodMenuItems
    ADD COLUMN IF NOT EXISTS label_text VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS print_component_labels TINYINT(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS component_labels_json TEXT NULL,
    ADD COLUMN IF NOT EXISTS print_group_id VARCHAR(36) NULL;

CREATE INDEX IF NOT EXISTS idx_foodmenu_print_group ON FoodMenuItems (print_group_id);

ALTER TABLE collection_customers
    ADD COLUMN IF NOT EXISTS last_order_date DATETIME NULL;

ALTER TABLE delivery_customers
    ADD COLUMN IF NOT EXISTS last_order_date DATETIME NULL;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS printed_at DATETIME NULL,
    ADD COLUMN IF NOT EXISTS print_error TEXT NULL;

CREATE TABLE IF NOT EXISTS sync_log (
    id INT AUTO_INCREMENT PRIMARY KEY,
    sync_type ENUM('orders', 'menu', 'customers', 'inventory', 'full_sync') NOT NULL,
    sync_direction ENUM('upload', 'download', 'bidirectional') NOT NULL,
    source_system VARCHAR(50) NOT NULL,
    records_processed INT DEFAULT 0,
    records_successful INT DEFAULT 0,
    records_failed INT DEFAULT 0,
    status ENUM('started', 'in_progress', 'completed', 'failed', 'cancelled') DEFAULT 'started',
    error_details TEXT NULL,
    sync_duration_seconds INT NULL,
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP NULL,
    INDEX idx_sync_log_type_status (sync_type, status),
    INDEX idx_sync_log_started (started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX IF NOT EXISTS idx_orders_created_sync ON orders (created_at DESC, sync_status);
CREATE INDEX IF NOT EXISTS idx_orders_number ON orders (order_number);
CREATE INDEX IF NOT EXISTS idx_orders_customer ON orders (customer_name, customer_phone);
CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items (order_id);
