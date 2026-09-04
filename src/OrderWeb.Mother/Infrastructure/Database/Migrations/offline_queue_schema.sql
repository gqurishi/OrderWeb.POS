-- ============================================
-- Offline Queue System for OrderWeb Integration
-- ============================================
-- Purpose: Queue operations when connection is lost
-- Operations queued: order status updates, print ACKs, 
-- gift cards, loyalty points, any API calls
-- ============================================

CREATE TABLE IF NOT EXISTS offline_queue (
    id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Operation details
    operation_type VARCHAR(50) NOT NULL COMMENT 'Type: order_status, print_ack, gift_card, loyalty, custom',
    endpoint VARCHAR(255) NOT NULL COMMENT 'API endpoint to call',
    http_method VARCHAR(10) NOT NULL DEFAULT 'POST' COMMENT 'GET, POST, PUT, DELETE, PATCH',
    
    -- Request payload
    payload JSON NOT NULL COMMENT 'JSON payload to send',
    headers JSON NULL COMMENT 'Custom HTTP headers if needed',
    
    -- Queue metadata
    priority INT NOT NULL DEFAULT 5 COMMENT '1=highest, 10=lowest',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    scheduled_at DATETIME NULL COMMENT 'When to attempt sending (for delayed operations)',
    
    -- Retry logic
    status ENUM('pending', 'processing', 'sent', 'failed', 'cancelled') NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    max_retries INT NOT NULL DEFAULT 3,
    last_attempt_at DATETIME NULL,
    last_error TEXT NULL,
    
    -- Success tracking
    sent_at DATETIME NULL,
    response_status INT NULL COMMENT 'HTTP status code',
    response_body TEXT NULL COMMENT 'Response from server',
    
    -- Indexing for performance
    INDEX idx_status_priority (status, priority),
    INDEX idx_created_at (created_at),
    INDEX idx_operation_type (operation_type),
    INDEX idx_scheduled_at (scheduled_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Offline queue for API operations when connection is lost';

-- ============================================
-- Queue Statistics View
-- ============================================
CREATE OR REPLACE VIEW offline_queue_stats AS
SELECT 
    operation_type,
    status,
    COUNT(*) as count,
    MIN(created_at) as oldest_item,
    MAX(created_at) as newest_item,
    AVG(retry_count) as avg_retries
FROM offline_queue
WHERE status IN ('pending', 'processing', 'failed')
GROUP BY operation_type, status;

-- ============================================
-- Cleanup old sent items (keep last 7 days)
-- ============================================
-- Run this periodically to prevent table bloat
-- DELETE FROM offline_queue 
-- WHERE status = 'sent' 
-- AND sent_at < DATE_SUB(NOW(), INTERVAL 7 DAY);
