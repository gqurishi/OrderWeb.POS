-- Enhanced Restaurant Table Management System
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
    UNIQUE KEY unique_active_session (TableId, IsActive),
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

-- Update RestaurantTables to add current session info
ALTER TABLE RestaurantTables 
ADD COLUMN CurrentSessionId INT NULL,
ADD COLUMN LastOccupied DATETIME NULL,
ADD COLUMN TotalSessionsToday INT DEFAULT 0,
ADD FOREIGN KEY (CurrentSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL;

-- Create trigger to update table status when session changes
DELIMITER $$

CREATE TRIGGER update_table_status_on_session 
AFTER UPDATE ON TableSessions
FOR EACH ROW
BEGIN
    -- Update table status based on session status
    IF NEW.Status = 'Closed' AND OLD.Status != 'Closed' THEN
        UPDATE RestaurantTables 
        SET Status = 'Available', 
            CurrentSessionId = NULL,
            LastOccupied = NEW.EndTime
        WHERE Id = NEW.TableId;
        
        -- Calculate actual duration
        UPDATE TableSessions 
        SET ActualDuration = TIMESTAMPDIFF(MINUTE, StartTime, EndTime)
        WHERE Id = NEW.Id;
    ELSEIF NEW.Status IN ('Occupied', 'Ordering', 'FoodServed', 'Payment') THEN
        UPDATE RestaurantTables 
        SET Status = 'Occupied',
            CurrentSessionId = NEW.Id
        WHERE Id = NEW.TableId;
    ELSEIF NEW.Status = 'Cleaning' THEN
        UPDATE RestaurantTables 
        SET Status = 'Reserved' -- Use Reserved for cleaning status
        WHERE Id = NEW.TableId;
    END IF;
END$$

DELIMITER ;

-- Insert some sample data for testing
INSERT INTO TableSessions (TableId, SessionNumber, PartySize, Status, CustomerNotes, SpecialOccasion) VALUES
(1, 'S001', 4, 'FoodServed', 'Window seat requested', 'Birthday'),
(2, 'S002', 2, 'Ordering', 'Vegetarian preferences', NULL),
(3, 'S003', 6, 'Payment', 'Business meeting', NULL);

-- Sample session notes
INSERT INTO SessionNotes (SessionId, Note, NoteType, CreatedBy) VALUES
(1, 'Customer has peanut allergy', 'Allergy', 'Admin'),
(1, 'Birthday cake needed at 8 PM', 'Request', 'Admin'),
(2, 'Customer prefers quiet table', 'Request', 'Admin'),
(3, 'VIP customer - regular visitor', 'VIP', 'Admin');

-- Update RestaurantTables with current sessions
UPDATE RestaurantTables rt
JOIN TableSessions ts ON rt.Id = ts.TableId AND ts.IsActive = TRUE
SET rt.CurrentSessionId = ts.Id, rt.Status = 'Occupied'
WHERE ts.Status != 'Closed';

-- View to get complete table information with current session
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
    t.TotalSessionsToday,
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
WHERE t.IsActive = TRUE AND f.IsActive = TRUE;