-- ===================================================================
-- Online Order Auto-Print Migration for POS-in-NET
-- Purpose: Add Online and Takeaway printer types support
-- Date: December 6, 2025
-- ===================================================================

-- Step 1: Update network_printers table to support new printer types
-- The printer_type column is already VARCHAR so no schema change needed
-- Just documenting the new values: 'online', 'takeaway'

-- Step 2: Create online_order_print_tracking table
-- Tracks print jobs specifically for online orders
CREATE TABLE IF NOT EXISTS online_order_print_tracking (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_id VARCHAR(255) NOT NULL COMMENT 'OrderWeb.net order UUID',
    order_number VARCHAR(50) NOT NULL COMMENT 'Order display number (e.g., KIT-3763)',
    
    -- Online printer (customer receipt)
    online_printer_id INT NULL COMMENT 'Network printer ID for customer receipt',
    online_job_id INT NULL COMMENT 'Print queue job ID',
    online_status ENUM('pending', 'queued', 'printing', 'completed', 'failed') DEFAULT 'pending',
    online_error TEXT NULL,
    online_attempts INT DEFAULT 0,
    online_printed_at DATETIME NULL,
    
    -- Takeaway printer (kitchen ticket)
    takeaway_printer_id INT NULL COMMENT 'Network printer ID for kitchen ticket',
    takeaway_job_id INT NULL COMMENT 'Print queue job ID',
    takeaway_status ENUM('pending', 'queued', 'printing', 'completed', 'failed') DEFAULT 'pending',
    takeaway_error TEXT NULL,
    takeaway_attempts INT DEFAULT 0,
    takeaway_printed_at DATETIME NULL,
    
    -- Overall status
    overall_status ENUM('pending', 'partial', 'completed', 'failed') DEFAULT 'pending',
    ack_sent BOOLEAN DEFAULT FALSE COMMENT 'Whether ACK was sent to OrderWeb.net',
    ack_sent_at DATETIME NULL,
    
    -- Timestamps
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    -- Indexes
    INDEX idx_order_id (order_id),
    INDEX idx_order_number (order_number),
    INDEX idx_overall_status (overall_status),
    INDEX idx_created_at (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Step 3: Add index to network_printers for type lookup
-- Check if index exists first (MySQL 8.0+)
SET @index_exists = (
    SELECT COUNT(*) 
    FROM information_schema.statistics 
    WHERE table_schema = DATABASE() 
    AND table_name = 'network_printers' 
    AND index_name = 'idx_printer_type'
);

SET @sql = IF(@index_exists = 0, 
    'CREATE INDEX idx_printer_type ON network_printers(printer_type)',
    'SELECT "Index already exists"'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Step 4: Add configuration for auto-print settings
INSERT IGNORE INTO cloud_config (`key`, `value`) VALUES 
    ('online_auto_print_enabled', 'True'),
    ('online_print_customer_receipt', 'True'),
    ('online_print_kitchen_ticket', 'True'),
    ('online_print_max_retries', '3'),
    ('online_print_retry_interval_minutes', '3');

-- ===================================================================
-- Verification queries (run manually to check setup)
-- ===================================================================

-- Check new printer types are recognized:
-- SELECT DISTINCT printer_type FROM network_printers;

-- Check online order tracking table:
-- DESCRIBE online_order_print_tracking;

-- Check configuration:
-- SELECT * FROM cloud_config WHERE `key` LIKE 'online_%';
