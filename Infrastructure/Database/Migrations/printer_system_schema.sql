-- ============================================
-- NETWORK PRINTER SYSTEM SCHEMA
-- Created: 2024-11-30
-- Description: Tables for network printer management
-- ============================================

-- Printers Table
CREATE TABLE IF NOT EXISTS network_printers (
    id INT PRIMARY KEY AUTO_INCREMENT,
    
    -- Basic Info
    name VARCHAR(100) NOT NULL COMMENT 'Display name like "Kitchen Hot"',
    ip_address VARCHAR(45) NOT NULL COMMENT 'IPv4 or IPv6 address',
    port INT DEFAULT 9100 COMMENT 'Default ESC/POS port',
    
    -- Printer Configuration
    brand ENUM('epson', 'star', 'other') DEFAULT 'epson' COMMENT 'Printer manufacturer',
    printer_type ENUM('receipt', 'kitchen', 'bar', 'label') NOT NULL COMMENT 'Purpose of printer',
    paper_width ENUM('80mm', '58mm') DEFAULT '80mm' COMMENT 'Thermal paper width',
    
    -- Features
    has_cash_drawer BOOLEAN DEFAULT FALSE COMMENT 'Connected cash drawer (receipt only)',
    has_cutter BOOLEAN DEFAULT TRUE COMMENT 'Auto paper cutter',
    has_buzzer BOOLEAN DEFAULT FALSE COMMENT 'Audio alert on print',
    
    -- Status
    is_enabled BOOLEAN DEFAULT TRUE COMMENT 'Admin can disable printer',
    is_online BOOLEAN DEFAULT FALSE COMMENT 'Last connection check result',
    last_seen DATETIME NULL COMMENT 'Last successful connection',
    
    -- UI Customization
    color_code VARCHAR(7) DEFAULT '#6366F1' COMMENT 'Hex color for UI display',
    display_order INT DEFAULT 0 COMMENT 'Order in printer list',
    notes TEXT NULL COMMENT 'Admin notes',
    
    -- Timestamps
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    -- Indexes
    INDEX idx_printer_type (printer_type),
    INDEX idx_is_enabled (is_enabled),
    UNIQUE INDEX idx_ip_port (ip_address, port)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Print Queue Table (for retry mechanism)
CREATE TABLE IF NOT EXISTS print_queue (
    id INT PRIMARY KEY AUTO_INCREMENT,
    printer_id INT NOT NULL,
    
    -- Job Info
    order_id INT NULL COMMENT 'Related order if applicable',
    job_type ENUM('receipt', 'kitchen_ticket', 'test', 'cash_drawer') NOT NULL,
    print_data LONGBLOB COMMENT 'ESC/POS commands as bytes',
    
    -- Status
    status ENUM('pending', 'printing', 'completed', 'failed') DEFAULT 'pending',
    retry_count INT DEFAULT 0,
    max_retries INT DEFAULT 5,
    error_message TEXT NULL,
    
    -- Timestamps
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    started_at DATETIME NULL,
    completed_at DATETIME NULL,
    
    -- Foreign Keys
    FOREIGN KEY (printer_id) REFERENCES network_printers(id) ON DELETE CASCADE,
    
    -- Indexes
    INDEX idx_status (status),
    INDEX idx_created (created_at),
    INDEX idx_printer_status (printer_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Printer Settings Table (global settings)
CREATE TABLE IF NOT EXISTS printer_settings (
    id INT PRIMARY KEY AUTO_INCREMENT,
    setting_key VARCHAR(50) NOT NULL UNIQUE,
    setting_value TEXT,
    description VARCHAR(255),
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Insert default settings
INSERT INTO printer_settings (setting_key, setting_value, description) VALUES
('health_check_interval', '30', 'Seconds between printer health checks'),
('queue_poll_interval', '5', 'Seconds between queue processing'),
('default_timeout', '5000', 'TCP connection timeout in milliseconds'),
('test_mode', 'false', 'When true, prints go to PDF instead of printer')
ON DUPLICATE KEY UPDATE setting_key = setting_key;

-- ============================================
-- SAMPLE DATA (Optional - Comment out for production)
-- ============================================

-- INSERT INTO network_printers (name, ip_address, port, brand, printer_type, has_cash_drawer, has_buzzer, color_code, display_order) VALUES
-- ('Front Counter', '192.168.1.100', 9100, 'epson', 'receipt', TRUE, FALSE, '#10B981', 1),
-- ('Takeaway', '192.168.1.101', 9100, 'epson', 'receipt', FALSE, FALSE, '#10B981', 2),
-- ('Kitchen Hot', '192.168.1.102', 9100, 'epson', 'kitchen', FALSE, TRUE, '#EF4444', 3),
-- ('Kitchen Cold', '192.168.1.103', 9100, 'epson', 'kitchen', FALSE, TRUE, '#3B82F6', 4),
-- ('Kitchen Grill', '192.168.1.104', 9100, 'star', 'kitchen', FALSE, TRUE, '#F59E0B', 5),
-- ('Bar', '192.168.1.105', 9100, 'star', 'bar', FALSE, TRUE, '#8B5CF6', 6);
