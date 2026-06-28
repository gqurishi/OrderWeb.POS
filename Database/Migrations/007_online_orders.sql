-- OrderWeb POS production migration 007
-- Online order integration, offline retry queue, ACK tracking, heartbeat, and daily report sync log.

CREATE TABLE IF NOT EXISTS offline_queue (
    id INT AUTO_INCREMENT PRIMARY KEY,
    operation_type VARCHAR(50) NOT NULL,
    endpoint VARCHAR(255) NOT NULL,
    http_method VARCHAR(10) NOT NULL DEFAULT 'POST',
    payload JSON NOT NULL,
    headers JSON NULL,
    priority INT NOT NULL DEFAULT 5,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    scheduled_at DATETIME NULL,
    status ENUM('pending', 'processing', 'sent', 'failed', 'cancelled') NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    max_retries INT NOT NULL DEFAULT 3,
    last_attempt_at DATETIME NULL,
    last_error TEXT NULL,
    sent_at DATETIME NULL,
    response_status INT NULL,
    response_body TEXT NULL,
    INDEX idx_status_priority (status, priority),
    INDEX idx_created_at (created_at),
    INDEX idx_operation_type (operation_type),
    INDEX idx_scheduled_at (scheduled_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE OR REPLACE VIEW offline_queue_stats AS
SELECT
    operation_type,
    status,
    COUNT(*) AS count,
    MIN(created_at) AS oldest_item,
    MAX(created_at) AS newest_item,
    AVG(retry_count) AS avg_retries
FROM offline_queue
WHERE status IN ('pending', 'processing', 'failed')
GROUP BY operation_type, status;

CREATE TABLE IF NOT EXISTS pending_acks (
    id INT PRIMARY KEY AUTO_INCREMENT,
    order_id VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL,
    reason TEXT NULL,
    printed_at DATETIME NULL,
    device_id VARCHAR(50) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    retry_count INT NOT NULL DEFAULT 0,
    last_retry_at DATETIME NULL,
    INDEX idx_pending_acks_created (created_at),
    INDEX idx_pending_acks_retry (retry_count, created_at),
    INDEX idx_pending_acks_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS heartbeat_log (
    id INT PRIMARY KEY AUTO_INCREMENT,
    device_id VARCHAR(100) NOT NULL,
    status VARCHAR(20) NOT NULL,
    pending_acks_count INT NOT NULL DEFAULT 0,
    pending_orders_count INT NOT NULL DEFAULT 0,
    last_print_at DATETIME NULL,
    sent_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_heartbeat_device (device_id, sent_at),
    INDEX idx_heartbeat_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_received_log (
    id INT PRIMARY KEY AUTO_INCREMENT,
    order_id VARCHAR(255) NOT NULL,
    received_at DATETIME NOT NULL,
    device_id VARCHAR(100) NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'received',
    sent_to_cloud TINYINT(1) NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_order_received (order_id, received_at),
    INDEX idx_order_received_status (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS online_order_print_tracking (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id VARCHAR(255) NOT NULL,
    order_number VARCHAR(50) NOT NULL,
    online_printer_id INT NULL,
    online_job_id INT NULL,
    online_status ENUM('pending', 'queued', 'printing', 'completed', 'failed') NOT NULL DEFAULT 'pending',
    online_error TEXT NULL,
    online_attempts INT NOT NULL DEFAULT 0,
    online_printed_at DATETIME NULL,
    takeaway_printer_id INT NULL,
    takeaway_job_id INT NULL,
    takeaway_status ENUM('pending', 'queued', 'printing', 'completed', 'failed') NOT NULL DEFAULT 'pending',
    takeaway_error TEXT NULL,
    takeaway_attempts INT NOT NULL DEFAULT 0,
    takeaway_printed_at DATETIME NULL,
    overall_status ENUM('pending', 'partial', 'completed', 'failed') NOT NULL DEFAULT 'pending',
    ack_sent TINYINT(1) NOT NULL DEFAULT 0,
    ack_sent_at DATETIME NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_order_id (order_id),
    INDEX idx_order_number (order_number),
    INDEX idx_overall_status (overall_status),
    INDEX idx_created_at (created_at),
    CONSTRAINT fk_online_tracking_online_printer
        FOREIGN KEY (online_printer_id) REFERENCES network_printers(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_online_tracking_takeaway_printer
        FOREIGN KEY (takeaway_printer_id) REFERENCES network_printers(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_online_tracking_online_job
        FOREIGN KEY (online_job_id) REFERENCES network_print_queue(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_online_tracking_takeaway_job
        FOREIGN KEY (takeaway_job_id) REFERENCES network_print_queue(id)
        ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO settings (setting_key, setting_value)
SELECT 'online_auto_print_enabled', 'True'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM settings WHERE setting_key = 'online_auto_print_enabled');

INSERT INTO settings (setting_key, setting_value)
SELECT 'online_print_customer_receipt', 'True'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM settings WHERE setting_key = 'online_print_customer_receipt');

INSERT INTO settings (setting_key, setting_value)
SELECT 'online_print_kitchen_ticket', 'True'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM settings WHERE setting_key = 'online_print_kitchen_ticket');

INSERT INTO settings (setting_key, setting_value)
SELECT 'online_print_max_retries', '3'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM settings WHERE setting_key = 'online_print_max_retries');

INSERT INTO settings (setting_key, setting_value)
SELECT 'online_print_retry_interval_minutes', '3'
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM settings WHERE setting_key = 'online_print_retry_interval_minutes');

CREATE TABLE IF NOT EXISTS orderweb_daily_report_sync_log (
    id INT PRIMARY KEY AUTO_INCREMENT,
    report_date DATE NOT NULL,
    tenant VARCHAR(120) NOT NULL,
    total_sales DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    total_orders INT NOT NULL DEFAULT 0,
    cash_sales DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    card_sales DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    trigger_source VARCHAR(30) NOT NULL DEFAULT 'manual',
    success TINYINT(1) NOT NULL DEFAULT 0,
    response_body TEXT NULL,
    error_message VARCHAR(500) NULL,
    uploaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_orderweb_daily_report_date (report_date),
    INDEX idx_orderweb_daily_report_success (success, uploaded_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
