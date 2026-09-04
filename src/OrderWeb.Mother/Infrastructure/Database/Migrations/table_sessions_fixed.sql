-- Enhanced Restaurant Table Management System (Fixed)
-- Adding table sessions, party management, and enhanced status tracking

-- Create TableSessions table for tracking table occupancy
CREATE TABLE IF NOT EXISTS TableSessions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TableId INT NOT NULL,
    CurrentOrderId VARCHAR(100) NULL,
    ParentSessionId INT NULL,
    MergedIntoSessionId INT NULL,
    SessionNumber VARCHAR(20) NOT NULL, -- S001, S002, etc.
    PartySize INT NOT NULL DEFAULT 1,
    StartTime DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EndTime DATETIME NULL,
    Status ENUM('Occupied', 'Ordering', 'FoodServed', 'Payment', 'Cleaning', 'Closed') NOT NULL DEFAULT 'Occupied',
    CustomerNotes TEXT,
    SpecialOccasion VARCHAR(100), -- Birthday, Anniversary, etc.
    EstimatedDuration INT DEFAULT 60, -- Minutes
    ActualDuration INT, -- Calculated when closed
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    
    FOREIGN KEY (TableId) REFERENCES RestaurantTables(Id) ON DELETE CASCADE,
    FOREIGN KEY (CurrentOrderId) REFERENCES orders(order_id) ON DELETE SET NULL,
    FOREIGN KEY (ParentSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL,
    FOREIGN KEY (MergedIntoSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL,
    INDEX idx_session_status (Status),
    INDEX idx_session_date (StartTime),
    INDEX idx_table_sessions (TableId, IsActive),
    INDEX idx_session_current_order (CurrentOrderId),
    INDEX idx_session_parent (ParentSessionId),
    INDEX idx_session_merged_into (MergedIntoSessionId)
);

-- Create SessionNotes table for additional notes during service
CREATE TABLE IF NOT EXISTS SessionNotes (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SessionId INT NOT NULL,
    Note TEXT NOT NULL,
    NoteType ENUM('General', 'Allergy', 'Request', 'Complaint', 'VIP') NOT NULL DEFAULT 'General',
    CreatedBy VARCHAR(100), -- Admin/Staff name for now
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (SessionId) REFERENCES TableSessions(Id) ON DELETE CASCADE,
    INDEX idx_session_notes (SessionId)
);

-- Create TableSessionEvents table for audit trail
CREATE TABLE IF NOT EXISTS TableSessionEvents (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SessionId INT NOT NULL,
    EventType VARCHAR(50) NOT NULL,
    ActorName VARCHAR(100) NULL,
    PayloadJson JSON NULL,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (SessionId) REFERENCES TableSessions(Id) ON DELETE CASCADE,
    INDEX idx_session_events_session_created (SessionId, CreatedDate),
    INDEX idx_session_events_type_created (EventType, CreatedDate)
);

-- Update RestaurantTables to add current session info (only if columns don't exist)
SET @sql = (SELECT IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
     WHERE table_name = 'RestaurantTables' 
     AND table_schema = 'Pos-net' 
     AND column_name = 'CurrentSessionId') = 0,
    'ALTER TABLE RestaurantTables ADD COLUMN CurrentSessionId INT NULL, ADD COLUMN LastOccupied DATETIME NULL, ADD COLUMN TotalSessionsToday INT DEFAULT 0',
    'SELECT "Columns already exist" AS message'
));
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Add foreign key if it doesn't exist
SET @sql = (SELECT IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
     WHERE table_name = 'RestaurantTables' 
     AND table_schema = 'Pos-net' 
     AND constraint_name = 'fk_current_session') = 0,
    'ALTER TABLE RestaurantTables ADD CONSTRAINT fk_current_session FOREIGN KEY (CurrentSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL',
    'SELECT "Foreign key already exists" AS message'
));
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Create or replace view for complete table information
DROP VIEW IF EXISTS TableWithSessionInfo;
CREATE VIEW TableWithSessionInfo AS
SELECT 
    t.Id AS TableId,
    t.TableNumber,
    t.FloorId,
    f.Name AS FloorName,
    t.Capacity,
    t.Shape,
    t.Status AS TableStatus,
    t.LastOccupied,
    IFNULL(t.TotalSessionsToday, 0) AS TotalSessionsToday,
    s.Id AS SessionId,
    s.SessionNumber,
    s.PartySize,
    s.StartTime,
    s.Status AS SessionStatus,
    s.CustomerNotes,
    s.SpecialOccasion,
    s.EstimatedDuration,
    TIMESTAMPDIFF(MINUTE, s.StartTime, NOW()) AS MinutesOccupied,
    CASE 
        WHEN s.Status = 'Occupied' THEN ' Just Seated'
        WHEN s.Status = 'Ordering' THEN ' Taking Order'
        WHEN s.Status = 'FoodServed' THEN ' Dining'
        WHEN s.Status = 'Payment' THEN ' Ready to Pay'
        WHEN s.Status = 'Cleaning' THEN ' Cleaning'
        ELSE ' Available'
    END AS StatusDisplay
FROM RestaurantTables t
LEFT JOIN Floors f ON t.FloorId = f.Id
LEFT JOIN TableSessions s ON t.CurrentSessionId = s.Id AND s.IsActive = TRUE
WHERE t.IsActive = TRUE AND (f.IsActive = TRUE OR f.IsActive IS NULL);

SELECT 'Table session management system installed successfully!' AS Result;
