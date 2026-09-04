-- Label Print Settings Migration
-- Adds label printing configuration to menu items
-- Run this script on the Pos-net database

-- ========================================
-- Add Label Print Settings to FoodMenuItems
-- ========================================

-- Add label_text column (custom text for label printer)
ALTER TABLE FoodMenuItems 
ADD COLUMN label_text VARCHAR(100) NULL 
COMMENT 'Custom text to print on label (empty = no label)';

-- Add print_component_labels column (toggle for meal deal component labels)
ALTER TABLE FoodMenuItems 
ADD COLUMN print_component_labels BOOLEAN NOT NULL DEFAULT 0 
COMMENT 'Print individual labels for each component (meal deals only)';

-- ========================================
-- Verification Query
-- ========================================
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT,
    COLUMN_COMMENT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'Pos-net' 
  AND TABLE_NAME = 'FoodMenuItems'
  AND COLUMN_NAME IN ('label_text', 'print_component_labels');
