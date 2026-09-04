-- ===================================================================
-- Order Print Tracking Migration for POS-in-NET
-- Purpose: Add print status tracking and acknowledgment queue
-- Date: November 21, 2025
-- ===================================================================

-- Step 1: Add print status columns to orders table
ALTER TABLE orders 
ADD COLUMN print_status VARCHAR(20) DEFAULT 'pending' COMMENT 'Print status: pending, sent_to_pos, printing, printed, failed',
ADD COLUMN printed_at DATETIME NULL COMMENT 'When order was printed',
ADD COLUMN print_error TEXT NULL COMMENT 'Error message if print failed',
ADD COLUMN print_device_id VARCHAR(50) NULL COMMENT 'Which POS device printed this order';

-- Add index for faster queries
CREATE INDEX idx_orders_print_status ON orders(print_status, CreatedAt);

-- Step 2: Create pending_acks table for retry queue
CREATE TABLE IF NOT EXISTS pending_acks (
  id INT PRIMARY KEY AUTO_INCREMENT,
  order_id VARCHAR(255) NOT NULL COMMENT 'Order ID to acknowledge',
  status VARCHAR(20) NOT NULL COMMENT 'printed or failed',
  reason TEXT NULL COMMENT 'Error reason if failed',
  printed_at DATETIME NULL COMMENT 'When order was printed',
  device_id VARCHAR(50) NULL COMMENT 'POS device identifier',
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  retry_count INT DEFAULT 0 COMMENT 'Number of retry attempts',
  last_retry_at DATETIME NULL COMMENT 'Last retry attempt timestamp',
  INDEX idx_pending_acks_created (created_at),
  INDEX idx_pending_acks_retry (retry_count, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Queue for failed print acknowledgments';

-- Step 3: Update existing orders to have initial print_status
UPDATE orders 
SET print_status = 'pending' 
WHERE print_status IS NULL;

-- ===================================================================
-- Migration Complete
-- ===================================================================
-- Next steps:
-- 1. Update CloudOrderService.cs to implement polling
-- 2. Update PrintService.cs to send acknowledgments
-- 3. Implement ACK retry queue service
-- ===================================================================
