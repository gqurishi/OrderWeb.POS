-- OrderWeb POS production migration 006
-- Restaurant floor/table layout, table sessions, and Mother/Child terminal state.

CREATE TABLE IF NOT EXISTS Floors (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Description VARCHAR(255) NULL,
    BackgroundImage TEXT NULL,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    INDEX idx_name (Name),
    INDEX idx_active (IsActive)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RestaurantTables (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TableNumber VARCHAR(50) NOT NULL,
    FloorId INT NOT NULL,
    Capacity INT NOT NULL,
    Shape VARCHAR(20) NOT NULL DEFAULT 'Square',
    Status VARCHAR(20) NOT NULL DEFAULT 'Available',
    TableDesignIcon VARCHAR(100) NULL DEFAULT 'table_1.png',
    PositionX INT NOT NULL DEFAULT 0,
    PositionY INT NOT NULL DEFAULT 0,
    CurrentSessionId INT NULL,
    LastOccupied DATETIME NULL,
    TotalSessionsToday INT NOT NULL DEFAULT 0,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    CONSTRAINT fk_table_floor
        FOREIGN KEY (FloorId) REFERENCES Floors(Id)
        ON DELETE CASCADE
        ON UPDATE CASCADE,
    UNIQUE KEY unique_table_per_floor (FloorId, TableNumber),
    INDEX idx_floor_id (FloorId),
    INDEX idx_status (Status),
    INDEX idx_active (IsActive),
    INDEX idx_table_number (TableNumber),
    INDEX idx_current_session (CurrentSessionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS TableSessions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    TableId INT NOT NULL,
    CurrentOrderId VARCHAR(100) NULL,
    ParentSessionId INT NULL,
    MergedIntoSessionId INT NULL,
    SessionNumber VARCHAR(20) NOT NULL,
    PartySize INT NOT NULL DEFAULT 1,
    StartTime DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EndTime DATETIME NULL,
    Status ENUM('Occupied', 'Ordering', 'FoodServed', 'Payment', 'Cleaning', 'Closed') NOT NULL DEFAULT 'Occupied',
    CustomerNotes TEXT NULL,
    SpecialOccasion VARCHAR(100) NULL,
    EstimatedDuration INT NOT NULL DEFAULT 60,
    ActualDuration INT NULL,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    CONSTRAINT fk_table_sessions_table
        FOREIGN KEY (TableId) REFERENCES RestaurantTables(Id)
        ON DELETE CASCADE,
    CONSTRAINT fk_table_sessions_current_order
        FOREIGN KEY (CurrentOrderId) REFERENCES orders(order_id)
        ON DELETE SET NULL,
    CONSTRAINT fk_table_sessions_parent_session
        FOREIGN KEY (ParentSessionId) REFERENCES TableSessions(Id)
        ON DELETE SET NULL,
    CONSTRAINT fk_table_sessions_merged_into_session
        FOREIGN KEY (MergedIntoSessionId) REFERENCES TableSessions(Id)
        ON DELETE SET NULL,
    INDEX idx_session_status (Status),
    INDEX idx_session_date (StartTime),
    INDEX idx_table_sessions (TableId, IsActive),
    INDEX idx_session_current_order (CurrentOrderId),
    INDEX idx_session_parent (ParentSessionId),
    INDEX idx_session_merged_into (MergedIntoSessionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS SessionNotes (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SessionId INT NOT NULL,
    Note TEXT NOT NULL,
    NoteType ENUM('General', 'Allergy', 'Request', 'Complaint', 'VIP') NOT NULL DEFAULT 'General',
    CreatedBy VARCHAR(100) NULL,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_session_notes_session
        FOREIGN KEY (SessionId) REFERENCES TableSessions(Id)
        ON DELETE CASCADE,
    INDEX idx_session_notes (SessionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS TableSessionEvents (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SessionId INT NOT NULL,
    EventType VARCHAR(50) NOT NULL,
    ActorName VARCHAR(100) NULL,
    PayloadJson JSON NULL,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_session_events_session
        FOREIGN KEY (SessionId) REFERENCES TableSessions(Id)
        ON DELETE CASCADE,
    INDEX idx_session_events_session_created (SessionId, CreatedDate),
    INDEX idx_session_events_type_created (EventType, CreatedDate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @add_current_session_fk = (
    SELECT IF(
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'RestaurantTables'
              AND CONSTRAINT_NAME = 'fk_restaurant_tables_current_session'
        ) = 0,
        'ALTER TABLE RestaurantTables ADD CONSTRAINT fk_restaurant_tables_current_session FOREIGN KEY (CurrentSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL',
        'SELECT 1'
    )
);
PREPARE stmt_add_current_session_fk FROM @add_current_session_fk;
EXECUTE stmt_add_current_session_fk;
DEALLOCATE PREPARE stmt_add_current_session_fk;

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
        WHEN s.Status = 'Occupied' THEN 'Just Seated'
        WHEN s.Status = 'Ordering' THEN 'Taking Order'
        WHEN s.Status = 'FoodServed' THEN 'Dining'
        WHEN s.Status = 'Payment' THEN 'Ready to Pay'
        WHEN s.Status = 'Cleaning' THEN 'Cleaning'
        ELSE 'Available'
    END AS StatusDisplay
FROM RestaurantTables t
LEFT JOIN Floors f ON t.FloorId = f.Id
LEFT JOIN TableSessions s ON t.CurrentSessionId = s.Id AND s.IsActive = TRUE
WHERE t.IsActive = TRUE AND (f.IsActive = TRUE OR f.IsActive IS NULL);

CREATE TABLE IF NOT EXISTS terminal_health (
    terminal_name VARCHAR(120) NOT NULL PRIMARY KEY,
    terminal_mode ENUM('Mother', 'Child') NOT NULL,
    database_host VARCHAR(255) NOT NULL,
    app_version VARCHAR(40) NULL,
    last_seen_at DATETIME NOT NULL,
    last_status VARCHAR(40) NOT NULL DEFAULT 'online',
    last_error TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_terminal_health_seen (last_seen_at),
    INDEX idx_terminal_health_mode (terminal_mode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS terminal_pairings (
    terminal_name VARCHAR(120) NOT NULL PRIMARY KEY,
    terminal_mode ENUM('Child') NOT NULL DEFAULT 'Child',
    pairing_code VARCHAR(12) NULL,
    pairing_expires_at DATETIME NULL,
    paired_at DATETIME NULL,
    disabled_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_terminal_pairings_code (pairing_code),
    INDEX idx_terminal_pairings_expiry (pairing_expires_at),
    INDEX idx_terminal_pairings_paired (paired_at),
    INDEX idx_terminal_pairings_disabled (disabled_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS terminal_events (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    event_id CHAR(36) NOT NULL,
    terminal_name VARCHAR(120) NULL,
    change_kind VARCHAR(80) NOT NULL,
    entity_name VARCHAR(120) NULL,
    entity_id VARCHAR(120) NULL,
    payload_json JSON NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY ux_terminal_events_event_id (event_id),
    INDEX idx_terminal_events_created (created_at),
    INDEX idx_terminal_events_kind (change_kind, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
