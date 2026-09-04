-- Visual Table Layout Migration
-- Adds position tracking and floor background image support

-- Add background image to Floors table
ALTER TABLE Floors ADD COLUMN IF NOT EXISTS BackgroundImage VARCHAR(500) NULL;

-- Add position columns to RestaurantTables table
ALTER TABLE RestaurantTables ADD COLUMN IF NOT EXISTS PositionX INT DEFAULT 0;
ALTER TABLE RestaurantTables ADD COLUMN IF NOT EXISTS PositionY INT DEFAULT 0;

-- Update existing tables with default positions (grid layout)
-- This will spread existing tables in a grid pattern
SET @row_num = 0;
UPDATE RestaurantTables 
SET 
    PositionX = ((@row_num := @row_num + 1) - 1) % 6 * 140 + 40,
    PositionY = FLOOR((@row_num - 1) / 6) * 140 + 40
WHERE PositionX = 0 AND PositionY = 0;
