-- ============================================
-- PRINTER SYSTEM MIGRATION
-- Date: 2024-10-24
-- Description: Complete print management system
-- for Kitchen, Bar, Receipt, and Online printers
-- ============================================

-- 1. PRINTERS TABLE
-- Stores all printer configurations
CREATE TABLE IF NOT EXISTS printers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    printer_name VARCHAR(100) NOT NULL,
    printer_type ENUM('Kitchen', 'Bar', 'Receipt', 'Online', 'Custom') NOT NULL,
    printer_technology ENUM('Thermal', 'DotMatrix', 'PDF') DEFAULT 'Thermal',
    ip_address VARCHAR(50) NOT NULL,
    port INT DEFAULT 9100,
    is_enabled TINYINT(1) DEFAULT 1,
    is_online TINYINT(1) DEFAULT 1,
    last_online_check DATETIME NULL,
    display_order INT DEFAULT 0,
    color_code VARCHAR(7) DEFAULT '#10B981',
    notes TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_printer_type (printer_type),
    INDEX idx_enabled (is_enabled),
    INDEX idx_display_order (display_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. PRINTER-SUBCATEGORY MAPPINGS
-- Links printers to which sub-categories they handle
CREATE TABLE IF NOT EXISTS printer_subcategory_mappings (
    id INT AUTO_INCREMENT PRIMARY KEY,
    printer_id INT NOT NULL,
    subcategory_id INT NOT NULL,
    print_sequence VARCHAR(50) DEFAULT 'Standard' COMMENT 'Starter, Main, Dessert, or Standard',
    is_active TINYINT(1) DEFAULT 1,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (printer_id) REFERENCES printers(id) ON DELETE CASCADE,
    FOREIGN KEY (subcategory_id) REFERENCES menu_categories(id) ON DELETE CASCADE,
    UNIQUE KEY unique_printer_subcategory (printer_id, subcategory_id),
    INDEX idx_printer (printer_id),
    INDEX idx_subcategory (subcategory_id),
    INDEX idx_active (is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. PRINT QUEUE
-- Manages all print jobs with retry logic
CREATE TABLE IF NOT EXISTS print_queue (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NOT NULL,
    printer_id INT NOT NULL,
    ticket_type VARCHAR(50) COMMENT 'Starter Ticket, Main Ticket, Bar Ticket, Receipt, Modification, etc.',
    print_content JSON NOT NULL COMMENT 'Contains items, table, time, etc.',
    priority INT DEFAULT 5 COMMENT '1=urgent, 5=normal, 10=low',
    status ENUM('pending', 'printing', 'success', 'failed', 'cancelled') DEFAULT 'pending',
    attempt_count INT DEFAULT 0,
    max_attempts INT DEFAULT 10,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    scheduled_for DATETIME NULL COMMENT 'For delayed printing (e.g., desserts)',
    started_at DATETIME NULL,
    completed_at DATETIME NULL,
    error_message TEXT,
    FOREIGN KEY (printer_id) REFERENCES printers(id) ON DELETE RESTRICT,
    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
    INDEX idx_status (status),
    INDEX idx_printer_status (printer_id, status),
    INDEX idx_created (created_at),
    INDEX idx_scheduled (scheduled_for),
    INDEX idx_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. PRINT LOGS
-- Audit trail for all print events
CREATE TABLE IF NOT EXISTS print_logs (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT NULL,
    printer_id INT NULL,
    print_queue_id INT NULL,
    event_type VARCHAR(50) COMMENT 'print_success, print_failed, printer_offline, retry_attempt, etc.',
    message TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (printer_id) REFERENCES printers(id) ON DELETE SET NULL,
    FOREIGN KEY (print_queue_id) REFERENCES print_queue(id) ON DELETE SET NULL,
    INDEX idx_printer (printer_id),
    INDEX idx_event (event_type),
    INDEX idx_created (created_at),
    INDEX idx_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. APP SETTINGS
-- Key-value store for print system configuration
CREATE TABLE IF NOT EXISTS app_settings (
    setting_key VARCHAR(100) PRIMARY KEY,
    setting_value TEXT,
    description VARCHAR(255),
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 6. ADD PRINT COLOR COLUMN TO MENU_ITEMS
-- Check if column exists before adding
SET @dbname = DATABASE();
SET @tablename = 'menu_items';
SET @columnname = 'print_in_red';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (table_name = @tablename)
      AND (table_schema = @dbname)
      AND (column_name = @columnname)
  ) > 0,
  'SELECT 1',
  CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' TINYINT(1) DEFAULT 0 COMMENT "If 1, print this item in RED color for highlighting"')
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- Insert default settings
INSERT INTO app_settings (setting_key, setting_value, description) VALUES 
    ('print_mode', 'test', 'Printing mode: test (PDF) or real (network printers)'),
    ('auto_retry_enabled', 'true', 'Enable automatic retry for failed prints'),
    ('max_retry_attempts', '10', 'Maximum number of retry attempts'),
    ('retry_interval_seconds', '30', 'Seconds to wait between retry attempts'),
    ('print_test_output_path', '', 'Path for PDF test outputs (empty = app folder)'),
    ('block_order_on_fail', 'false', 'Block order placement if print fails'),
    ('kitchen_multi_ticket', 'true', 'Print kitchen orders as separate tickets (Starter, Main, etc.)'),
    ('auto_print_modifications', 'true', 'Automatically print modification tickets when order changes'),
    ('printer_health_check_interval', '60', 'Seconds between printer status checks')
ON DUPLICATE KEY UPDATE setting_value=setting_value;

-- Insert sample printers (optional - for testing)
-- Uncomment to create default printer setup
/*
INSERT INTO printers (printer_name, printer_type, printer_technology, ip_address, port, color_code, display_order, notes) VALUES
    ('Kitchen Station 1', 'Kitchen', 'Thermal', '192.168.1.101', 9100, '#EF4444', 1, 'Main kitchen printer - near grill'),
    ('Bar Station', 'Bar', 'Thermal', '192.168.1.102', 9100, '#3B82F6', 2, 'Bar printer - drinks and cold items'),
    ('Receipt Printer', 'Receipt', 'DotMatrix', '192.168.1.103', 9100, '#64748B', 3, 'Customer receipts - dot matrix for copies'),
    ('Online Order Printer', 'Online', 'Thermal', '192.168.1.104', 9100, '#10B981', 4, 'Packing station for delivery/takeaway orders');
*/

-- ============================================
-- VERIFICATION QUERIES
-- Run these to verify installation
-- ============================================

-- Check tables created
SELECT 'Tables created successfully' AS status,
    (SELECT COUNT(*) FROM information_schema.tables 
     WHERE table_schema = DATABASE() 
     AND table_name IN ('printers', 'printer_subcategory_mappings', 'print_queue', 'print_logs', 'app_settings')) AS table_count;

-- Check app_settings populated
SELECT setting_key, setting_value, description FROM app_settings ORDER BY setting_key;

-- Check print_in_red column added
SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_DEFAULT, COLUMN_COMMENT 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
AND TABLE_NAME = 'menu_items' 
AND COLUMN_NAME = 'print_in_red';

-- ============================================
-- ROLLBACK SCRIPT (if needed)
-- ============================================
/*
DROP TABLE IF EXISTS print_logs;
DROP TABLE IF EXISTS print_queue;
DROP TABLE IF EXISTS printer_subcategory_mappings;
DROP TABLE IF EXISTS printers;
DELETE FROM app_settings WHERE setting_key LIKE 'print_%' OR setting_key LIKE '%retry%' OR setting_key LIKE '%printer%';
ALTER TABLE menu_items DROP COLUMN IF EXISTS print_in_red;
*/
