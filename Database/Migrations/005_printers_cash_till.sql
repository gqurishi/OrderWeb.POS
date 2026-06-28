-- OrderWeb POS production migration 005
-- Printers, print queue, print routing, cash drawer audit, and till expenses.

CREATE TABLE IF NOT EXISTS print_groups (
    id VARCHAR(36) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    printer_ip VARCHAR(45) NULL,
    printer_port INT NOT NULL DEFAULT 9100,
    printer_type VARCHAR(50) NOT NULL DEFAULT 'kitchen',
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    color_code VARCHAR(7) NOT NULL DEFAULT '#6366F1',
    display_order INT NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_print_groups_active_order (is_active, display_order),
    INDEX idx_print_groups_type (printer_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS network_printers (
    id INT PRIMARY KEY AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL,
    ip_address VARCHAR(45) NOT NULL,
    port INT NOT NULL DEFAULT 9100,
    brand ENUM('epson', 'star', 'other') NOT NULL DEFAULT 'epson',
    printer_type ENUM('receipt', 'kitchen', 'bar', 'label', 'online', 'takeaway') NOT NULL,
    paper_width ENUM('80mm', '58mm') NOT NULL DEFAULT '80mm',
    has_cash_drawer TINYINT(1) NOT NULL DEFAULT 0,
    has_cutter TINYINT(1) NOT NULL DEFAULT 1,
    has_buzzer TINYINT(1) NOT NULL DEFAULT 0,
    is_enabled TINYINT(1) NOT NULL DEFAULT 1,
    is_online TINYINT(1) NOT NULL DEFAULT 0,
    last_seen DATETIME NULL,
    color_code VARCHAR(7) NOT NULL DEFAULT '#6366F1',
    display_order INT NOT NULL DEFAULT 0,
    notes TEXT NULL,
    print_group_id VARCHAR(36) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY idx_ip_port (ip_address, port),
    INDEX idx_network_printers_print_group_id (print_group_id),
    INDEX idx_network_printers_type_enabled (printer_type, is_enabled),
    CONSTRAINT fk_network_printers_print_group
        FOREIGN KEY (print_group_id) REFERENCES print_groups(id)
        ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS network_print_queue (
    id INT PRIMARY KEY AUTO_INCREMENT,
    printer_id INT NOT NULL,
    order_id VARCHAR(100) NULL,
    job_type VARCHAR(50) NOT NULL DEFAULT 'receipt',
    print_data LONGBLOB NOT NULL,
    status ENUM('pending', 'printing', 'completed', 'failed') NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    max_retries INT NOT NULL DEFAULT 5,
    error_message TEXT NULL,
    created_by_terminal_name VARCHAR(120) NULL,
    claimed_by_terminal_name VARCHAR(120) NULL,
    claimed_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    started_at DATETIME NULL,
    completed_at DATETIME NULL,
    printed_at DATETIME NULL,
    last_attempt DATETIME NULL,
    CONSTRAINT fk_network_print_queue_printer
        FOREIGN KEY (printer_id) REFERENCES network_printers(id)
        ON DELETE CASCADE,
    INDEX idx_status (status),
    INDEX idx_printer_status (printer_id, status),
    INDEX idx_network_print_queue_status (status),
    INDEX idx_network_print_queue_printer_status (printer_id, status),
    INDEX idx_network_print_queue_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS printer_settings (
    id INT PRIMARY KEY AUTO_INCREMENT,
    setting_key VARCHAR(100) NOT NULL,
    setting_value TEXT NULL,
    description VARCHAR(255) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY ux_printer_settings_key (setting_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO printer_settings (setting_key, setting_value, description)
SELECT 'print_service_enabled', 'true', 'Enable the local network print service'
WHERE NOT EXISTS (SELECT 1 FROM printer_settings WHERE setting_key = 'print_service_enabled');

INSERT INTO printer_settings (setting_key, setting_value, description)
SELECT 'print_retry_count', '5', 'Maximum retry attempts for failed print jobs'
WHERE NOT EXISTS (SELECT 1 FROM printer_settings WHERE setting_key = 'print_retry_count');

INSERT INTO printer_settings (setting_key, setting_value, description)
SELECT 'print_job_retention_days', '7', 'Days to keep completed print queue rows'
WHERE NOT EXISTS (SELECT 1 FROM printer_settings WHERE setting_key = 'print_job_retention_days');

CREATE TABLE IF NOT EXISTS cash_drawer_events (
    id INT PRIMARY KEY AUTO_INCREMENT,
    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    requested_by_user_id INT NULL,
    requested_by_name VARCHAR(150) NULL,
    requested_by_role VARCHAR(20) NULL,
    reason VARCHAR(255) NULL,
    source_area VARCHAR(80) NULL,
    order_id VARCHAR(100) NULL,
    order_number VARCHAR(50) NULL,
    table_session_id INT NULL,
    table_number VARCHAR(50) NULL,
    device_type VARCHAR(50) NOT NULL DEFAULT 'receipt_printer_rj11',
    printer_id INT NULL,
    printer_name VARCHAR(100) NULL,
    printer_ip VARCHAR(45) NULL,
    printer_port INT NULL,
    success TINYINT(1) NOT NULL DEFAULT 0,
    error_message TEXT NULL,
    till_expense_id INT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_cash_drawer_event_at (event_at),
    INDEX idx_cash_drawer_user_time (requested_by_user_id, event_at),
    INDEX idx_cash_drawer_success_time (success, event_at),
    INDEX idx_cash_drawer_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS till_expenses (
    id INT PRIMARY KEY AUTO_INCREMENT,
    category VARCHAR(30) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'settled',
    description VARCHAR(255) NULL,
    amount_taken DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    amount_spent DECIMAL(10,2) NULL,
    amount_returned DECIMAL(10,2) NULL,
    net_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    counted_cash DECIMAL(10,2) NULL,
    order_id VARCHAR(100) NULL,
    order_number VARCHAR(50) NULL,
    source_area VARCHAR(80) NULL,
    recorded_by_user_id INT NULL,
    recorded_by_name VARCHAR(150) NULL,
    settled_by_user_id INT NULL,
    settled_by_name VARCHAR(150) NULL,
    settled_at DATETIME NULL,
    voided TINYINT(1) NOT NULL DEFAULT 0,
    void_reason VARCHAR(255) NULL,
    notes TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_till_expense_status (status, created_at),
    INDEX idx_till_expense_created (created_at),
    INDEX idx_till_expense_user (recorded_by_user_id, status),
    INDEX idx_till_expense_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
