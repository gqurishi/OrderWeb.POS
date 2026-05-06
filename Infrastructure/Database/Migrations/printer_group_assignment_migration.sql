-- Migration: Add print_group_id to network_printers table
-- Purpose: Link printers to print groups for menu item routing
-- Date: 2024

USE `Pos-net`;

-- Add print_group_id column to network_printers if it doesn't exist
ALTER TABLE network_printers 
ADD COLUMN IF NOT EXISTS print_group_id VARCHAR(36) NULL
COMMENT 'Links printer to a print group for menu item routing';

-- Add index for faster lookups
CREATE INDEX IF NOT EXISTS idx_print_group_id ON network_printers(print_group_id);

-- Verify the change
SELECT 
    'network_printers' as table_name,
    COUNT(*) as total_printers,
    SUM(CASE WHEN print_group_id IS NOT NULL THEN 1 ELSE 0 END) as printers_with_group,
    SUM(CASE WHEN print_group_id IS NULL THEN 1 ELSE 0 END) as printers_without_group
FROM network_printers;

-- Show example of how to assign printer to print group
-- UPDATE network_printers 
-- SET print_group_id = 'your-print-group-id-here'
-- WHERE id = your_printer_id;

-- Note: When a printer is assigned to a print group through the Printer Setup UI,
-- the print_group_id will be automatically saved.
-- Menu items can then reference these print groups to route orders to specific printers.
