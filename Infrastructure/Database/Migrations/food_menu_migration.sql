-- Food Menu System Migration
-- Creates the FoodMenuCategories, FoodMenuItems, PredefinedComments, and PredefinedNotes tables
-- Run this script on the Pos-net database

-- ========================================
-- 1. FoodMenuCategories Table
-- ========================================
CREATE TABLE IF NOT EXISTS FoodMenuCategories (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    Name VARCHAR(100) NOT NULL,
    Description TEXT,
    ParentId VARCHAR(36) DEFAULT NULL,
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    Color VARCHAR(20) NOT NULL DEFAULT '#3B82F6',
    Icon VARCHAR(50) NOT NULL DEFAULT '',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_foodmenu_cat_parent (ParentId),
    INDEX idx_foodmenu_cat_active (Active),
    INDEX idx_foodmenu_cat_order (DisplayOrder),
    
    CONSTRAINT fk_foodmenu_cat_parent 
        FOREIGN KEY (ParentId) REFERENCES FoodMenuCategories(Id) 
        ON DELETE SET NULL
);

-- ========================================
-- 2. FoodMenuItems Table
-- ========================================
CREATE TABLE IF NOT EXISTS FoodMenuItems (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    CategoryId VARCHAR(36) NOT NULL,
    Name VARCHAR(150) NOT NULL,
    Description TEXT,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    Color VARCHAR(20) DEFAULT '#3B82F6',
    DisplayOrder INT NOT NULL DEFAULT 0,
    IsFeatured BOOLEAN NOT NULL DEFAULT FALSE,
    PreparationTime INT DEFAULT 15,
    VatRate DECIMAL(5,2) DEFAULT 20.00,
    VatType VARCHAR(50) DEFAULT 'standard',
    IsVatExempt BOOLEAN NOT NULL DEFAULT FALSE,
    VatNotes TEXT,
    Addons TEXT,
    Tags TEXT,
    print_in_red BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_foodmenu_item_cat (CategoryId),
    INDEX idx_foodmenu_item_order (DisplayOrder),
    INDEX idx_foodmenu_item_featured (IsFeatured),
    
    CONSTRAINT fk_foodmenu_item_cat 
        FOREIGN KEY (CategoryId) REFERENCES FoodMenuCategories(Id) 
        ON DELETE CASCADE
);

-- ========================================
-- 3. PredefinedComments Table (Customer-Facing)
-- ========================================
CREATE TABLE IF NOT EXISTS PredefinedComments (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    CommentText VARCHAR(255) NOT NULL,
    Category VARCHAR(100),
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    Color VARCHAR(20) NOT NULL DEFAULT '#3B82F6',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_comment_cat (Category),
    INDEX idx_comment_active (Active),
    INDEX idx_comment_order (DisplayOrder)
);

-- ========================================
-- 4. PredefinedNotes Table (Kitchen-Only)
-- ========================================
CREATE TABLE IF NOT EXISTS PredefinedNotes (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    NoteText VARCHAR(255) NOT NULL,
    Category VARCHAR(100),
    Priority VARCHAR(20) NOT NULL DEFAULT 'normal',
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    Color VARCHAR(20) NOT NULL DEFAULT '#F59E0B',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_note_cat (Category),
    INDEX idx_note_active (Active),
    INDEX idx_note_priority (Priority),
    INDEX idx_note_order (DisplayOrder)
);

-- ========================================
-- Verification Query
-- ========================================
SELECT 'FoodMenuCategories' as TableName, COUNT(*) as RowCount FROM FoodMenuCategories
UNION ALL
SELECT 'FoodMenuItems', COUNT(*) FROM FoodMenuItems
UNION ALL
SELECT 'PredefinedNotes', COUNT(*) FROM PredefinedNotes
UNION ALL
SELECT 'PredefinedComments', COUNT(*) FROM PredefinedComments;
