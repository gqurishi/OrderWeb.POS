-- Restaurant Management System - Database Migration Script
-- MariaDB/MySQL
-- Database: Pos-net
-- Date: 2025-10-14

-- =============================================================================
-- TABLE: Floors
-- Description: Stores restaurant floor information
-- =============================================================================

CREATE TABLE IF NOT EXISTS Floors (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Description VARCHAR(255) NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive TINYINT(1) DEFAULT 1,
    INDEX idx_name (Name),
    INDEX idx_active (IsActive)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================================
-- TABLE: RestaurantTables
-- Description: Stores restaurant table information with floor association
-- =============================================================================

CREATE TABLE IF NOT EXISTS RestaurantTables (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TableNumber VARCHAR(50) NOT NULL,
    FloorId INT NOT NULL,
    Capacity INT NOT NULL,
    Shape VARCHAR(20) NOT NULL DEFAULT 'Square', -- 'Square' or 'Rectangle'
    Status VARCHAR(20) NOT NULL DEFAULT 'Available', -- 'Available', 'Occupied', 'Reserved'
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive TINYINT(1) DEFAULT 1,
    
    -- Foreign key constraint with CASCADE delete
    -- When a floor is deleted, all its tables are automatically deleted
    CONSTRAINT fk_table_floor 
        FOREIGN KEY (FloorId) 
        REFERENCES Floors(Id) 
        ON DELETE CASCADE 
        ON UPDATE CASCADE,
    
    -- Unique constraint: Table number must be unique within a floor
    -- This allows "Table 1" on Ground Floor and "Table 1" on First Floor
    CONSTRAINT unique_table_per_floor 
        UNIQUE KEY (FloorId, TableNumber),
    
    -- Indexes for better query performance
    INDEX idx_floor_id (FloorId),
    INDEX idx_status (Status),
    INDEX idx_active (IsActive),
    INDEX idx_table_number (TableNumber)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================================
-- VERIFICATION QUERIES
-- =============================================================================

-- Check floors
-- SELECT * FROM Floors WHERE IsActive = 1;

-- Check tables with floor names
-- SELECT 
--     t.Id,
--     t.TableNumber,
--     f.Name AS FloorName,
--     t.Capacity,
--     t.Shape,
--     t.Status,
--     t.CreatedDate
-- FROM RestaurantTables t
-- INNER JOIN Floors f ON t.FloorId = f.Id
-- WHERE t.IsActive = 1
-- ORDER BY f.Name, t.TableNumber;

-- Check table count per floor
-- SELECT 
--     f.Name AS FloorName,
--     COUNT(t.Id) AS TableCount
-- FROM Floors f
-- LEFT JOIN RestaurantTables t ON f.Id = t.FloorId AND t.IsActive = 1
-- WHERE f.IsActive = 1
-- GROUP BY f.Id, f.Name
-- ORDER BY f.Name;

-- =============================================================================
-- NOTES:
-- =============================================================================
-- 1. ON DELETE CASCADE: When a floor is deleted, all its tables are deleted
-- 2. UNIQUE constraint on (FloorId, TableNumber): Allows same table number on different floors
-- 3. utf8mb4 charset: Supports emojis and international characters
-- 4. Indexes added for better performance on common queries
-- 5. IsActive flag: For soft delete functionality (optional, can use hard delete)
-- 6. Production migrations intentionally leave floors and tables empty.
-- =============================================================================
