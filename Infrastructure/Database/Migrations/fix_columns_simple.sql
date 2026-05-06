-- Fix missing columns for Pos-net database
USE `Pos-net`;

-- Add priority column to print_queue if it doesn't exist
ALTER TABLE print_queue ADD COLUMN IF NOT EXISTS priority INT DEFAULT 5 COMMENT '1=urgent, 5=normal, 10=low' AFTER print_content;

-- Add print_in_red column to MenuItems if it doesn't exist  
ALTER TABLE MenuItems ADD COLUMN IF NOT EXISTS print_in_red TINYINT(1) DEFAULT 0 COMMENT 'If 1, print this item in RED color for highlighting';

-- Verify the columns were added
SELECT 'Columns added to Pos-net database' AS status;

DESCRIBE print_queue;
DESCRIBE MenuItems;
