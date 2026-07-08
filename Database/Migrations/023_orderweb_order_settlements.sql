-- 023: Track OrderWeb settlement callbacks sent when web orders are paid/closed on the till.

CREATE TABLE IF NOT EXISTS orderweb_order_settlements (
    id INT AUTO_INCREMENT PRIMARY KEY,
    local_order_id VARCHAR(100) NOT NULL,
    cloud_order_id VARCHAR(100) NOT NULL,
    order_number VARCHAR(50) NULL,
    status ENUM('completed', 'cancelled', 'no_show') NOT NULL DEFAULT 'completed',
    payment_method ENUM('cash', 'card') NULL,
    amount_paid DECIMAL(10,2) NULL,
    paid_at DATETIME NULL,
    fulfillment VARCHAR(30) NULL,
    device_id VARCHAR(120) NOT NULL,
    idempotency_key VARCHAR(120) NOT NULL,
    sent_to_cloud BOOLEAN NOT NULL DEFAULT FALSE,
    queued_for_retry BOOLEAN NOT NULL DEFAULT FALSE,
    last_attempt_at DATETIME NULL,
    sent_at DATETIME NULL,
    response_status INT NULL,
    response_body TEXT NULL,
    last_error TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY ux_orderweb_order_settlements_cloud_order (cloud_order_id),
    UNIQUE KEY ux_orderweb_order_settlements_idempotency (idempotency_key),
    INDEX idx_orderweb_order_settlements_local_order (local_order_id),
    INDEX idx_orderweb_order_settlements_status (status, sent_to_cloud, queued_for_retry),
    INDEX idx_orderweb_order_settlements_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
