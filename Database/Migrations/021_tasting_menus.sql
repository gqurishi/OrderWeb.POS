-- 021: First-class tasting menu setup.
-- Tasting menus are priced packages with options and course choices stored in JSON.

CREATE TABLE IF NOT EXISTS TastingMenus (
    Id VARCHAR(36) PRIMARY KEY,
    Name VARCHAR(150) NOT NULL,
    Description TEXT NULL,
    Color VARCHAR(20) NOT NULL DEFAULT '#0EA5E9',
    ConfigJson JSON NOT NULL,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    DisplayOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_tasting_menu_active (Active),
    INDEX idx_tasting_menu_order (DisplayOrder)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
