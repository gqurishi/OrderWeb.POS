-- OrderWeb POS production schema
-- 003: Menu, comments, notes, components, meal deals, and compatibility menu tables.

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
    CONSTRAINT fk_foodmenu_cat_parent FOREIGN KEY (ParentId) REFERENCES FoodMenuCategories(Id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS FoodMenuItems (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    CategoryId VARCHAR(36) NOT NULL,
    Name VARCHAR(150) NOT NULL,
    Description TEXT,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    price_dine_in DECIMAL(10,2) DEFAULT NULL,
    price_takeaway DECIMAL(10,2) DEFAULT NULL,
    Color VARCHAR(20) DEFAULT '#3B82F6',
    DisplayOrder INT NOT NULL DEFAULT 0,
    IsFeatured BOOLEAN NOT NULL DEFAULT FALSE,
    PreparationTime INT DEFAULT 15,
    VatRate DECIMAL(5,2) DEFAULT 20.00,
    VatType VARCHAR(50) DEFAULT 'standard',
    IsVatExempt BOOLEAN NOT NULL DEFAULT FALSE,
    VatNotes TEXT,
    vat_config_type VARCHAR(20) DEFAULT 'standard',
    vat_category VARCHAR(20) DEFAULT 'HotFood',
    calculated_vat_rate DECIMAL(5,2) DEFAULT 20.00,
    Addons TEXT,
    Tags TEXT,
    print_in_red BOOLEAN NOT NULL DEFAULT FALSE,
    label_print_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    component_label_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_foodmenu_item_cat (CategoryId),
    INDEX idx_foodmenu_item_order (DisplayOrder),
    INDEX idx_foodmenu_item_featured (IsFeatured),
    INDEX idx_vat_config (vat_config_type),
    INDEX idx_vat_category (vat_category),
    CONSTRAINT fk_foodmenu_item_cat FOREIGN KEY (CategoryId) REFERENCES FoodMenuCategories(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ItemComments (
    Id VARCHAR(36) PRIMARY KEY,
    MenuItemId VARCHAR(36) NOT NULL,
    CommentId VARCHAR(36) NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_item_comment (MenuItemId, CommentId),
    INDEX idx_item_comments_item (MenuItemId),
    INDEX idx_item_comments_comment (CommentId),
    CONSTRAINT fk_item_comments_menuitem FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE,
    CONSTRAINT fk_item_comments_comment FOREIGN KEY (CommentId) REFERENCES PredefinedComments(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS MenuItemQuickNotes (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    MenuItemId VARCHAR(36) NOT NULL,
    NoteId VARCHAR(36) NOT NULL,
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_menu_item_note (MenuItemId, NoteId),
    INDEX idx_quick_notes_item (MenuItemId),
    INDEX idx_quick_notes_note (NoteId),
    CONSTRAINT fk_quick_notes_item FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE,
    CONSTRAINT fk_quick_notes_note FOREIGN KEY (NoteId) REFERENCES PredefinedNotes(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS MenuItemComponents (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    MenuItemId VARCHAR(36) NOT NULL,
    ComponentName VARCHAR(100) NOT NULL,
    ComponentPrice DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    ComponentType VARCHAR(20) NOT NULL DEFAULT 'HotFood',
    VatRate DECIMAL(5,2) NOT NULL DEFAULT 20.00,
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_component_item (MenuItemId),
    INDEX idx_component_order (MenuItemId, SortOrder),
    CONSTRAINT fk_component_menuitem FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ItemComponents (
    Id VARCHAR(36) PRIMARY KEY,
    MenuItemId VARCHAR(36) NOT NULL,
    ComponentName VARCHAR(100) NOT NULL,
    ComponentCost DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    VatRate DECIMAL(5,2) NOT NULL DEFAULT 20.00,
    ComponentType VARCHAR(20) NOT NULL DEFAULT 'HotFood',
    DisplayOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_item_components_item (MenuItemId),
    CONSTRAINT fk_item_components_menuitem FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS MealDeals (
    Id VARCHAR(36) PRIMARY KEY,
    Name VARCHAR(150) NOT NULL,
    Description TEXT NULL,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    Color VARCHAR(20) NOT NULL DEFAULT '#F59E0B',
    Categories JSON NOT NULL,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    DisplayOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_mealdeal_active (Active),
    INDEX idx_mealdeal_order (DisplayOrder)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS MenuCategories (
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
    INDEX idx_menu_cat_parent (ParentId),
    INDEX idx_menu_cat_active (Active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS MenuItems (
    Id VARCHAR(36) PRIMARY KEY DEFAULT (UUID()),
    CategoryId VARCHAR(36) NOT NULL,
    Name VARCHAR(150) NOT NULL,
    Description TEXT,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    Color VARCHAR(20) DEFAULT '#3B82F6',
    DisplayOrder INT NOT NULL DEFAULT 0,
    Active BOOLEAN NOT NULL DEFAULT TRUE,
    print_in_red TINYINT(1) DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_menu_items_category (CategoryId),
    INDEX idx_menu_items_active (Active),
    CONSTRAINT fk_menu_items_category FOREIGN KEY (CategoryId) REFERENCES MenuCategories(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

