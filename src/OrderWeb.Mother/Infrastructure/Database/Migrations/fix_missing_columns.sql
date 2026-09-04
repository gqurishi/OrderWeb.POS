-- ============================================
-- EMERGENCY FIX: Add Missing Columns
-- Run this to fix "Unknown column" errors
-- ============================================

-- Fix 1: Add priority column to print_queue if missing
SET @dbname = DATABASE();
SET @tablename = 'print_queue';
SET @columnname = 'priority';
SET @preparedStatement = (SELECT IF(
  (
    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE
      (table_name = @tablename)
      AND (table_schema = @dbname)
      AND (column_name = @columnname)
  ) > 0,
  'SELECT 1 as column_exists',
  CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' INT DEFAULT 5 COMMENT "1=urgent, 5=normal, 10=low" AFTER print_content')
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- Fix 2: Add print_in_red column to menu_items if missing
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
  'SELECT 1 as column_exists',
  CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' TINYINT(1) DEFAULT 0 COMMENT "If 1, print this item in RED color for highlighting"')
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- Verify the fixes
SELECT 'Columns added successfully' AS status;

SELECT 'print_queue.priority' AS column_name, 
       COLUMN_TYPE, COLUMN_DEFAULT, COLUMN_COMMENT 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
AND TABLE_NAME = 'print_queue' 
AND COLUMN_NAME = 'priority';

SELECT 'menu_items.print_in_red' AS column_name,
       COLUMN_TYPE, COLUMN_DEFAULT, COLUMN_COMMENT 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
AND TABLE_NAME = 'menu_items' 
AND COLUMN_NAME = 'print_in_red';
