-- OrderWeb POS production schema
-- 004: Orders, order lines, payments, lifecycle events, refunds, and local customer tables.

CREATE TABLE IF NOT EXISTS orders (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id VARCHAR(100) UNIQUE NOT NULL,
    order_number VARCHAR(50) NULL,
    cloud_order_id VARCHAR(100) NULL,
    customer_name VARCHAR(100),
    customer_phone VARCHAR(20),
    customer_email VARCHAR(255) NULL,
    customer_address TEXT,
    total_amount DECIMAL(10,2),
    subtotal_amount DECIMAL(10,2) NULL,
    discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    delivery_fee DECIMAL(10,2) DEFAULT 0,
    tax_amount DECIMAL(10,2) DEFAULT 0,
    order_type ENUM('pickup', 'delivery', 'table') DEFAULT 'pickup',
    source_channel ENUM('local', 'web') DEFAULT 'local',
    table_session_id INT NULL,
    payment_method VARCHAR(50) NULL,
    special_instructions TEXT NULL,
    scheduled_time TIMESTAMP NULL,
    status ENUM('new', 'kitchen', 'preparing', 'ready', 'delivering', 'completed', 'cancelled') DEFAULT 'new',
    local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
    is_open BOOLEAN DEFAULT TRUE,
    void_reason VARCHAR(255) NULL,
    voided_at DATETIME NULL,
    voided_by VARCHAR(100) NULL,
    paid_at DATETIME NULL,
    first_sent_at DATETIME NULL,
    last_sent_at DATETIME NULL,
    send_attempt_count INT NOT NULL DEFAULT 0,
    send_failure_count INT NOT NULL DEFAULT 0,
    send_latency_ms INT NULL,
    first_payment_attempt_at DATETIME NULL,
    payment_attempt_count INT NOT NULL DEFAULT 0,
    payment_completion_seconds INT NULL,
    operational_last_event_at DATETIME NULL,
    draft_abandoned_flag BOOLEAN NOT NULL DEFAULT FALSE,
    draft_abandoned_at DATETIME NULL,
    print_status VARCHAR(40) NULL,
    order_data JSON,
    sync_status ENUM('synced', 'pending', 'failed') DEFAULT 'pending',
    kitchen_time TIMESTAMP NULL,
    preparing_time TIMESTAMP NULL,
    ready_time TIMESTAMP NULL,
    delivering_time TIMESTAMP NULL,
    completed_time TIMESTAMP NULL,
    updated_by_terminal_name VARCHAR(120) NULL,
    updated_by_terminal_at DATETIME NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_orders_table_session_open (table_session_id, is_open),
    INDEX idx_orders_report_date_source_type_status (created_at, source_channel, order_type, status),
    INDEX idx_orders_report_search (order_number, customer_name, customer_phone),
    INDEX idx_orders_print_status (print_status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_items (
    id INT AUTO_INCREMENT PRIMARY KEY,
    client_item_id VARCHAR(100) NULL,
    cloud_item_id INT NULL,
    menu_item_id VARCHAR(100) NULL,
    variant_id VARCHAR(100) NULL,
    variant_name VARCHAR(100) NULL,
    display_name VARCHAR(180) NULL,
    print_group_id VARCHAR(36) NULL,
    order_id INT,
    item_name VARCHAR(100) NOT NULL,
    quantity INT NOT NULL,
    item_price DECIMAL(10,2) NULL,
    special_instructions TEXT,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    INDEX idx_order_items_report_order_menu (order_id, menu_item_id),
    INDEX idx_order_items_variant (variant_id),
    INDEX idx_order_items_menu_variant (menu_item_id, variant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_item_addons (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_item_id INT NOT NULL,
    addon_id VARCHAR(100) NULL,
    addon_name VARCHAR(150) NOT NULL,
    addon_price DECIMAL(10,2) NOT NULL DEFAULT 0,
    quantity INT NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (order_item_id) REFERENCES order_items(id) ON DELETE CASCADE,
    INDEX idx_order_item_addons_report_item (order_item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_payments (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    attempt_no INT NOT NULL DEFAULT 1,
    payment_method ENUM('cash', 'card', 'gift_card', 'refund', 'tip_adjust') NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    currency_code CHAR(3) NOT NULL DEFAULT 'GBP',
    status ENUM('attempted', 'approved', 'failed', 'voided') NOT NULL DEFAULT 'attempted',
    reference VARCHAR(100) NULL,
    tip_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
    metadata_json JSON NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(100) NULL,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    INDEX idx_order_payments_order_created (order_id, created_at),
    INDEX idx_order_payments_order_status (order_id, status),
    INDEX idx_order_payments_method_created (payment_method, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_events (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    event_type ENUM(
        'created','line_added','line_removed','line_updated',
        'sent','resend','send_failed','split',
        'payment_attempt','payment_approved','payment_failed',
        'void_requested','voided','reopened',
        'table_transferred','table_merged','state_changed','draft_abandoned'
    ) NOT NULL,
    actor_type ENUM('user', 'system', 'manager') NOT NULL DEFAULT 'system',
    actor_id VARCHAR(100) NULL,
    actor_name VARCHAR(150) NULL,
    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    payload_json JSON NULL,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    INDEX idx_order_events_order_time (order_id, event_at),
    INDEX idx_order_events_type_time (event_type, event_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_item_send_tracking (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    order_item_id INT NOT NULL,
    send_batch_id VARCHAR(36) NOT NULL,
    station_type ENUM('kitchen', 'bar', 'receipt', 'other') NOT NULL DEFAULT 'kitchen',
    print_group_id VARCHAR(36) NULL,
    route_target VARCHAR(120) NULL,
    send_status ENUM('queued', 'sent', 'printed', 'failed', 'retrying') NOT NULL DEFAULT 'queued',
    sent_at DATETIME NULL,
    printed_at DATETIME NULL,
    failure_reason VARCHAR(255) NULL,
    attempt_count INT NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    FOREIGN KEY (order_item_id) REFERENCES order_items(id) ON DELETE CASCADE,
    INDEX idx_order_send_item_created (order_item_id, created_at),
    INDEX idx_order_send_batch (send_batch_id),
    INDEX idx_order_send_status_updated (send_status, updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_refunds (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    refund_amount DECIMAL(10,2) NOT NULL,
    refund_type ENUM('full', 'partial') NOT NULL,
    reason TEXT,
    refunded_at DATETIME NOT NULL,
    refunded_by VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_order_refunds_order_id (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS collection_customers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50) NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_collection_customer_phone (phone_number),
    INDEX idx_collection_customer_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS delivery_customers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50) NOT NULL,
    address TEXT NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_delivery_customer_phone (phone_number),
    INDEX idx_delivery_customer_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_number_settings (
    id INT PRIMARY KEY AUTO_INCREMENT,
    order_prefix VARCHAR(3) NOT NULL DEFAULT 'KIT',
    daily_counter INT NOT NULL DEFAULT 0,
    counter_date DATE NOT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_counter_date (counter_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO order_number_settings (order_prefix, daily_counter, counter_date)
SELECT 'KIT', 0, CURDATE()
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM order_number_settings LIMIT 1);
