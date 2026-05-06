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
    Icon VARCHAR(50) NOT NULL DEFAULT '🍽️',
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
-- 5. Sample Data (Optional)
-- ========================================

-- Sample Categories
INSERT IGNORE INTO FoodMenuCategories (Id, Name, Description, ParentId, DisplayOrder, Active, Color, Icon)
VALUES
    ('cat-starters', 'Starters', 'Appetizers and starters', NULL, 1, TRUE, '#22C55E', '🥗'),
    ('cat-mains', 'Main Course', 'Main dishes', NULL, 2, TRUE, '#EF4444', '🍖'),
    ('cat-desserts', 'Desserts', 'Sweet treats', NULL, 3, TRUE, '#EC4899', '🍰'),
    ('cat-drinks', 'Drinks', 'Beverages', NULL, 4, TRUE, '#3B82F6', '🍹'),
    ('cat-sides', 'Sides', 'Side dishes', NULL, 5, TRUE, '#F59E0B', '🍟');

-- Sample Sub-Categories
INSERT IGNORE INTO FoodMenuCategories (Id, Name, Description, ParentId, DisplayOrder, Active, Color, Icon)
VALUES
    ('subcat-soups', 'Soups', 'Hot and cold soups', 'cat-starters', 1, TRUE, '#22C55E', '🍜'),
    ('subcat-salads', 'Salads', 'Fresh salads', 'cat-starters', 2, TRUE, '#22C55E', '🥗'),
    ('subcat-chicken', 'Chicken', 'Chicken dishes', 'cat-mains', 1, TRUE, '#EF4444', '🍗'),
    ('subcat-beef', 'Beef', 'Beef dishes', 'cat-mains', 2, TRUE, '#EF4444', '🥩'),
    ('subcat-fish', 'Fish', 'Seafood dishes', 'cat-mains', 3, TRUE, '#EF4444', '🐟');

-- Sample Menu Items
INSERT IGNORE INTO FoodMenuItems (Id, CategoryId, Name, Description, Price, Color, DisplayOrder, IsFeatured, PreparationTime, VatRate, IsVatExempt, print_in_red)
VALUES
    ('item-1', 'cat-starters', 'Garlic Bread', 'Freshly baked with garlic butter', 4.50, '#22C55E', 1, FALSE, 5, 20.00, FALSE, FALSE),
    ('item-2', 'cat-starters', 'Soup of the Day', 'Ask your server for today\'s selection', 5.95, '#22C55E', 2, FALSE, 10, 20.00, FALSE, FALSE),
    ('item-3', 'cat-mains', 'Grilled Chicken', 'Served with seasonal vegetables', 14.95, '#EF4444', 1, TRUE, 20, 20.00, FALSE, FALSE),
    ('item-4', 'cat-mains', 'Fish & Chips', 'Beer-battered cod with hand-cut chips', 13.50, '#EF4444', 2, TRUE, 15, 20.00, FALSE, FALSE),
    ('item-5', 'cat-desserts', 'Chocolate Brownie', 'Warm brownie with vanilla ice cream', 6.95, '#EC4899', 1, FALSE, 5, 20.00, FALSE, FALSE);

-- Sample Predefined Notes (Kitchen)
INSERT IGNORE INTO PredefinedNotes (Id, NoteText, Category, Priority, DisplayOrder, Active, Color)
VALUES
    ('note-1', 'No onions', 'Allergy', 'normal', 1, TRUE, '#F59E0B'),
    ('note-2', 'No garlic', 'Allergy', 'normal', 2, TRUE, '#F59E0B'),
    ('note-3', 'Nut allergy - URGENT', 'Allergy', 'urgent', 3, TRUE, '#EF4444'),
    ('note-4', 'Gluten free', 'Allergy', 'high', 4, TRUE, '#F97316'),
    ('note-5', 'Well done', 'Cooking', 'normal', 5, TRUE, '#3B82F6'),
    ('note-6', 'Medium rare', 'Cooking', 'normal', 6, TRUE, '#3B82F6'),
    ('note-7', 'Extra sauce', 'Special', 'low', 7, TRUE, '#22C55E'),
    ('note-8', 'No salt', 'Special', 'normal', 8, TRUE, '#22C55E');

-- Sample Predefined Comments (Customer-Facing)
INSERT IGNORE INTO PredefinedComments (Id, CommentText, Category, DisplayOrder, Active, Color)
VALUES
    ('comment-1', 'Thank you for dining with us!', 'General', 1, TRUE, '#3B82F6'),
    ('comment-2', 'Enjoy your meal', 'General', 2, TRUE, '#3B82F6'),
    ('comment-3', 'Happy Birthday!', 'Special', 3, TRUE, '#EC4899'),
    ('comment-4', 'Happy Anniversary!', 'Special', 4, TRUE, '#EC4899'),
    ('comment-5', 'Please return the tray', 'Service', 5, TRUE, '#F59E0B');

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
