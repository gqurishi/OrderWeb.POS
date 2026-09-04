-- Migration: Add component_labels_json field to FoodMenuItems
-- Date: 2025-12-11
-- Purpose: Store list of component names to print as individual labels when Print Component Labels toggle is enabled

-- Add column to store component labels as JSON array
ALTER TABLE FoodMenuItems 
ADD COLUMN IF NOT EXISTS component_labels_json TEXT NULL 
COMMENT 'JSON array of component names to print as labels (e.g., ["Roti", "Curry", "Rice"])';

-- Example usage:
-- When print_component_labels = 1 and component_labels_json is set,
-- the system will print individual labels for each component in the array
-- This is independent of meal deals - works for any item type
