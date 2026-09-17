-- Phase 1 Bar Inventory data model (also applied at runtime by BarStockService / MenuItemService).
-- Stock master + menu-item track/link. Old menu items default Track Off.

CREATE TABLE IF NOT EXISTS bar_stock_items (
    id VARCHAR(36) NOT NULL PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    sku VARCHAR(50) NULL,
    stock_unit VARCHAR(20) NOT NULL DEFAULT 'bottle',
    pack_size DECIMAL(12,3) NOT NULL DEFAULT 1.000,
    par_level DECIMAL(12,3) NOT NULL DEFAULT 0.000,
    on_hand DECIMAL(12,3) NOT NULL DEFAULT 0.000,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_bar_stock_name (name),
    INDEX idx_bar_stock_sku (sku),
    INDEX idx_bar_stock_active (is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE FoodMenuItems
    ADD COLUMN IF NOT EXISTS track_bar_inventory TINYINT(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS bar_stock_item_id VARCHAR(36) NULL,
    ADD COLUMN IF NOT EXISTS sell_portion_qty DECIMAL(12,3) NULL,
    ADD COLUMN IF NOT EXISTS sell_portion_unit VARCHAR(20) NULL;

ALTER TABLE FoodMenuItems
    ADD INDEX IF NOT EXISTS idx_foodmenu_track_bar (track_bar_inventory),
    ADD INDEX IF NOT EXISTS idx_foodmenu_bar_stock (bar_stock_item_id);
