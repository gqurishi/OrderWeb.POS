-- OrderWeb POS production schema
-- 020: Menu item price variants and order-line variant persistence.

CREATE TABLE IF NOT EXISTS MenuItemVariants (
    Id VARCHAR(36) PRIMARY KEY,
    MenuItemId VARCHAR(36) NOT NULL,
    Name VARCHAR(100) NOT NULL,
    Description TEXT NULL,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_menu_item_variants_item (MenuItemId),
    INDEX idx_menu_item_variants_active_order (MenuItemId, Active, DisplayOrder),
    CONSTRAINT fk_menu_item_variants_item FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE order_items
    ADD COLUMN IF NOT EXISTS variant_id VARCHAR(100) NULL AFTER menu_item_id,
    ADD COLUMN IF NOT EXISTS variant_name VARCHAR(100) NULL AFTER variant_id,
    ADD COLUMN IF NOT EXISTS display_name VARCHAR(180) NULL AFTER variant_name,
    ADD INDEX IF NOT EXISTS idx_order_items_variant (variant_id),
    ADD INDEX IF NOT EXISTS idx_order_items_menu_variant (menu_item_id, variant_id);
