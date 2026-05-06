-- Menu Item Quick Notes System
-- Creates the MenuItemQuickNotes table to link items with up to 6 predefined notes
-- Run this script on the Pos-net database

-- ========================================
-- MenuItemQuickNotes Table
-- ========================================
CREATE TABLE IF NOT EXISTS MenuItemQuickNotes (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    MenuItemId VARCHAR(36) NOT NULL,
    NoteText VARCHAR(255) NOT NULL,
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_menuitem_quick_note_item (MenuItemId),
    INDEX idx_menuitem_quick_note_active (Active),
    INDEX idx_menuitem_quick_note_order (DisplayOrder),
    
    CONSTRAINT fk_menuitem_quick_note 
        FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) 
        ON DELETE CASCADE
);

-- ========================================
-- Sample Data (Optional)
-- ========================================
-- Add quick notes for Grilled Chicken (item-3)
INSERT IGNORE INTO MenuItemQuickNotes (Id, MenuItemId, NoteText, DisplayOrder, Active)
VALUES
    ('qn-1', 'item-3', 'No onions', 1, TRUE),
    ('qn-2', 'item-3', 'Extra sauce', 2, TRUE),
    ('qn-3', 'item-3', 'Well done', 3, TRUE),
    ('qn-4', 'item-3', 'No garlic', 4, TRUE);

-- Add quick notes for Fish & Chips (item-4)
INSERT IGNORE INTO MenuItemQuickNotes (Id, MenuItemId, NoteText, DisplayOrder, Active)
VALUES
    ('qn-5', 'item-4', 'No salt', 1, TRUE),
    ('qn-6', 'item-4', 'Extra chips', 2, TRUE),
    ('qn-7', 'item-4', 'Tartare sauce on side', 3, TRUE),
    ('qn-8', 'item-4', 'Mushy peas instead', 4, TRUE);

-- ========================================
-- Verification Query
-- ========================================
SELECT 
    mi.Name as MenuItem,
    GROUP_CONCAT(mqn.NoteText ORDER BY mqn.DisplayOrder SEPARATOR ', ') as QuickNotes
FROM FoodMenuItems mi
LEFT JOIN MenuItemQuickNotes mqn ON mi.Id = mqn.MenuItemId AND mqn.Active = TRUE
GROUP BY mi.Id, mi.Name
HAVING QuickNotes IS NOT NULL;
