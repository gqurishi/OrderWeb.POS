-- Phase 4 Bar Inventory movements (also applied at runtime by BarStockService).

CREATE TABLE IF NOT EXISTS bar_stock_movements (
    id VARCHAR(36) NOT NULL PRIMARY KEY,
    stock_item_id VARCHAR(36) NOT NULL,
    movement_type VARCHAR(20) NOT NULL,
    qty_delta DECIMAL(12,3) NOT NULL,
    qty_before DECIMAL(12,3) NOT NULL,
    qty_after DECIMAL(12,3) NOT NULL,
    reason VARCHAR(255) NULL,
    staff_user_id VARCHAR(36) NULL,
    staff_display_name VARCHAR(120) NULL,
    terminal_id VARCHAR(80) NULL,
    terminal_label VARCHAR(120) NULL,
    source VARCHAR(20) NOT NULL DEFAULT 'mother',
    idempotency_key VARCHAR(80) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_bar_stock_mov_idem (idempotency_key),
    INDEX idx_bar_stock_mov_stock (stock_item_id, created_at),
    INDEX idx_bar_stock_mov_type (movement_type, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
