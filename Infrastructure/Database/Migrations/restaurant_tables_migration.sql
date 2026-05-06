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
-- SAMPLE DATA (Optional - Remove if you don't want sample data)
-- =============================================================================

-- Insert sample floors
INSERT INTO Floors (Name, Description) VALUES
    ('Ground Floor', 'Main dining area'),
    ('First Floor', 'Private dining rooms'),
    ('Rooftop', 'Outdoor seating area')
ON DUPLICATE KEY UPDATE Description = VALUES(Description);

-- Insert sample tables (only if floors exist)
INSERT INTO RestaurantTables (TableNumber, FloorId, Capacity, Shape, Status) VALUES
    ('Table 1', (SELECT Id FROM Floors WHERE Name = 'Ground Floor'), 4, 'Square', 'Available'),
    ('Table 2', (SELECT Id FROM Floors WHERE Name = 'Ground Floor'), 2, 'Rectangle', 'Occupied'),
    ('Table 3', (SELECT Id FROM Floors WHERE Name = 'Ground Floor'), 6, 'Square', 'Available'),
    ('Table 4', (SELECT Id FROM Floors WHERE Name = 'Ground Floor'), 4, 'Rectangle', 'Available'),
    ('Table 1', (SELECT Id FROM Floors WHERE Name = 'First Floor'), 8, 'Rectangle', 'Reserved'),
    ('Table 2', (SELECT Id FROM Floors WHERE Name = 'First Floor'), 4, 'Square', 'Available'),
    ('Table 3', (SELECT Id FROM Floors WHERE Name = 'First Floor'), 6, 'Rectangle', 'Available'),
    ('Table 1', (SELECT Id FROM Floors WHERE Name = 'Rooftop'), 4, 'Square', 'Available'),
    ('Table 2', (SELECT Id FROM Floors WHERE Name = 'Rooftop'), 8, 'Rectangle', 'Available')
ON DUPLICATE KEY UPDATE Capacity = VALUES(Capacity), Shape = VALUES(Shape);

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
-- =============================================================================
