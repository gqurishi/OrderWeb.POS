-- ========================================
-- VAT System Migration
-- Adds smart VAT calculation for UK restaurants
-- Supports both simple items and meal deals with components
-- ========================================

USE `Pos-net`;

-- ========================================
-- 1. Update FoodMenuItems table with new VAT fields
-- ========================================

-- Add dual pricing columns for order mode specific pricing
ALTER TABLE FoodMenuItems
ADD COLUMN IF NOT EXISTS price_dine_in DECIMAL(10,2) DEFAULT NULL
COMMENT 'Price for dine-in/table/in-house orders';

ALTER TABLE FoodMenuItems
ADD COLUMN IF NOT EXISTS price_takeaway DECIMAL(10,2) DEFAULT NULL
COMMENT 'Price for takeaway/collection/pickup/delivery orders';

-- Add vat_config_type column (replaces old VatType)
ALTER TABLE FoodMenuItems 
ADD COLUMN IF NOT EXISTS vat_config_type VARCHAR(20) DEFAULT 'standard' 
COMMENT 'simple=single item, component=meal deal with mixed hot/cold';

-- Add vat_category column (for simple items)
ALTER TABLE FoodMenuItems 
ADD COLUMN IF NOT EXISTS vat_category VARCHAR(20) DEFAULT 'HotFood' 
COMMENT 'NoVAT, HotFood, ColdFood, HotBeverage, ColdBeverage, Alcohol';

-- Add calculated_vat_rate column (effective VAT % for display)
ALTER TABLE FoodMenuItems 
ADD COLUMN IF NOT EXISTS calculated_vat_rate DECIMAL(5,2) DEFAULT 20.00
COMMENT 'Auto-calculated effective VAT rate for the item';

-- Update existing VatType column to match new naming
UPDATE FoodMenuItems 
SET vat_config_type = 'standard' 
WHERE VatType = 'standard' OR VatType IS NULL OR VatType = '';

UPDATE FoodMenuItems 
SET vat_config_type = 'component' 
WHERE VatType = 'mixed' OR VatType = 'component';

-- Set default vat_category based on existing data
UPDATE FoodMenuItems 
SET vat_category = CASE 
    WHEN IsVatExempt = TRUE THEN 'NoVAT'
    WHEN VatRate = 0 THEN 'ColdFood'
    ELSE 'HotFood'
END
WHERE vat_category IS NULL OR vat_category = '';

-- Update calculated_vat_rate from existing VatRate
UPDATE FoodMenuItems 
SET calculated_vat_rate = VatRate 
WHERE calculated_vat_rate IS NULL OR calculated_vat_rate = 0;

-- Backfill dual pricing from legacy Price column
UPDATE FoodMenuItems
SET price_dine_in = Price
WHERE price_dine_in IS NULL;

UPDATE FoodMenuItems
SET price_takeaway = Price
WHERE price_takeaway IS NULL;


-- ========================================
-- 2. Create MenuItemComponents table (for meal deals)
-- ========================================

CREATE TABLE IF NOT EXISTS MenuItemComponents (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    MenuItemId VARCHAR(36) NOT NULL,
    ComponentName VARCHAR(100) NOT NULL,
    ComponentPrice DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    ComponentType VARCHAR(20) NOT NULL DEFAULT 'HotFood',
    -- ComponentType options: HotFood, ColdFood, HotBeverage, ColdBeverage, Alcohol
    VatRate DECIMAL(5,2) NOT NULL DEFAULT 20.00,
    -- Auto-assigned: 20% for hot/alcohol, 0% for cold (for takeaway)
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_component_item (MenuItemId),
    INDEX idx_component_order (MenuItemId, SortOrder),
    
    CONSTRAINT fk_component_menuitem 
        FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) 
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Components for meal deals with mixed hot/cold items';


-- ========================================
-- 3. Add indexes for performance
-- ========================================

ALTER TABLE FoodMenuItems ADD INDEX IF NOT EXISTS idx_vat_config (vat_config_type);
ALTER TABLE FoodMenuItems ADD INDEX IF NOT EXISTS idx_vat_category (vat_category);


-- ========================================
-- 4. Sample data for testing (optional)
-- ========================================

-- Example: Standard hot food item
-- INSERT INTO FoodMenuItems (Id, CategoryId, Name, Price, vat_config_type, vat_category, calculated_vat_rate)
-- VALUES (UUID(), 'some-category-id', 'Hot Pizza', 12.99, 'standard', 'HotFood', 20.00);

-- Example: Cold food item
-- INSERT INTO FoodMenuItems (Id, CategoryId, Name, Price, vat_config_type, vat_category, calculated_vat_rate)
-- VALUES (UUID(), 'some-category-id', 'Cold Sandwich', 5.99, 'standard', 'ColdFood', 0.00);

-- Example: Meal deal with components
-- INSERT INTO FoodMenuItems (Id, CategoryId, Name, Price, vat_config_type, vat_category, calculated_vat_rate)
-- VALUES ('meal-deal-id', 'some-category-id', 'Biryani Meal', 15.00, 'component', NULL, 16.00);

-- INSERT INTO MenuItemComponents (MenuItemId, ComponentName, ComponentPrice, ComponentType, VatRate, SortOrder)
-- VALUES 
-- ('meal-deal-id', 'Chicken Biryani (Hot)', 12.00, 'HotFood', 20.00, 1),
-- ('meal-deal-id', 'Raita (Cold)', 3.00, 'ColdFood', 0.00, 2);


-- ========================================
-- 5. Verification queries
-- ========================================

-- Check the new columns
SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_DEFAULT, COLUMN_COMMENT 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = 'Pos-net' 
AND TABLE_NAME = 'FoodMenuItems' 
AND COLUMN_NAME IN ('price_dine_in', 'price_takeaway', 'vat_config_type', 'vat_category', 'calculated_vat_rate');

-- Check the new table
SHOW CREATE TABLE MenuItemComponents;

-- Count items by VAT configuration
SELECT vat_config_type, vat_category, COUNT(*) as count
FROM FoodMenuItems
GROUP BY vat_config_type, vat_category;


-- ========================================
-- ROLLBACK SCRIPT (if needed)
-- ========================================
/*
ALTER TABLE FoodMenuItems DROP COLUMN IF EXISTS vat_config_type;
ALTER TABLE FoodMenuItems DROP COLUMN IF EXISTS vat_category;
ALTER TABLE FoodMenuItems DROP COLUMN IF EXISTS calculated_vat_rate;
DROP TABLE IF EXISTS MenuItemComponents;
*/
