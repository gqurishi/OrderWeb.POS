-- 016: Store floor background images centrally so every terminal can cache them locally.

CREATE TABLE IF NOT EXISTS FloorBackgroundImages (
    FloorId INT PRIMARY KEY,
    FileName VARCHAR(255) NOT NULL,
    MimeType VARCHAR(100) NOT NULL,
    ContentHash CHAR(64) NOT NULL,
    ImageData MEDIUMBLOB NOT NULL,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_floor_background_floor
        FOREIGN KEY (FloorId) REFERENCES Floors(Id)
        ON DELETE CASCADE,
    INDEX idx_floor_background_hash (ContentHash),
    INDEX idx_floor_background_updated (UpdatedAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
