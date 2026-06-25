-- Phase 2: Menu Management and Order Taking System
-- Complete database schema for restaurant menu and ordering

-- 1. Menu Categories (Appetizers, Main Course, Beverages, Desserts)
CREATE TABLE IF NOT EXISTS MenuCategories (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL UNIQUE,
    Description TEXT,
    SortOrder INT NOT NULL DEFAULT 0,
    Icon VARCHAR(50), --     etc.
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    INDEX idx_category_active (IsActive, SortOrder),
    INDEX idx_category_sort (SortOrder)
);

-- 2. Menu Items (Individual dishes/products)
CREATE TABLE IF NOT EXISTS MenuItems (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    CategoryId INT NOT NULL,
    Name VARCHAR(150) NOT NULL,
    Description TEXT,
    Price DECIMAL(10,2) NOT NULL,
    ImagePath VARCHAR(255), -- Path to item photo
    PrepTime INT DEFAULT 15, -- Minutes to prepare
    Calories INT, -- Nutritional info
    IsSpicy BOOLEAN DEFAULT FALSE,
    IsVegetarian BOOLEAN DEFAULT FALSE,
    IsVegan BOOLEAN DEFAULT FALSE,
    IsGlutenFree BOOLEAN DEFAULT FALSE,
    IsAvailable BOOLEAN NOT NULL DEFAULT TRUE,
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    FOREIGN KEY (CategoryId) REFERENCES MenuCategories(Id) ON DELETE CASCADE,
    INDEX idx_item_category (CategoryId, IsAvailable, SortOrder),
    INDEX idx_item_available (IsAvailable),
    FULLTEXT(Name, Description)
);

-- 3. Item Modifiers/Add-ons (Extra cheese, No onions, Large size)
CREATE TABLE IF NOT EXISTS ItemModifiers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Description VARCHAR(255),
    PriceAdjustment DECIMAL(8,2) NOT NULL DEFAULT 0.00, -- +2.50 for extra cheese, 0.00 for no onions
    ModifierType ENUM('Addition', 'Substitution', 'Removal', 'Size') NOT NULL DEFAULT 'Addition',
    IsRequired BOOLEAN DEFAULT FALSE, -- Must select one option (like size: small/medium/large)
    SortOrder INT NOT NULL DEFAULT 0,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    INDEX idx_modifier_type (ModifierType, IsActive),
    INDEX idx_modifier_active (IsActive, SortOrder)
);

-- 4. Link modifiers to menu items (which modifiers apply to which items)
CREATE TABLE IF NOT EXISTS MenuItemModifiers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    MenuItemId INT NOT NULL,
    ModifierId INT NOT NULL,
    IsDefault BOOLEAN DEFAULT FALSE, -- Default selection for this modifier
    
    FOREIGN KEY (MenuItemId) REFERENCES MenuItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (ModifierId) REFERENCES ItemModifiers(Id) ON DELETE CASCADE,
    UNIQUE KEY unique_item_modifier (MenuItemId, ModifierId),
    INDEX idx_item_modifiers (MenuItemId)
);

-- 5. Customer Orders
CREATE TABLE IF NOT EXISTS CustomerOrders (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    OrderNumber VARCHAR(20) NOT NULL UNIQUE, -- ORD001, ORD002, etc.
    TableSessionId INT, -- Link to table session (can be NULL for takeout)
    OrderType ENUM('Dine-in', 'Takeout', 'Delivery') NOT NULL DEFAULT 'Dine-in',
    CustomerName VARCHAR(100),
    CustomerPhone VARCHAR(20),
    Subtotal DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    TaxAmount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    TaxRate DECIMAL(5,4) NOT NULL DEFAULT 0.0000, -- 8.25% = 0.0825
    DiscountAmount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    TotalAmount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    OrderStatus ENUM('New', 'Confirmed', 'Preparing', 'Ready', 'Served', 'Paid', 'Cancelled') NOT NULL DEFAULT 'New',
    PaymentStatus ENUM('Pending', 'Paid', 'Partial', 'Refunded') NOT NULL DEFAULT 'Pending',
    PaymentMethod ENUM('Cash', 'Card', 'Gift Card', 'Split') DEFAULT NULL,
    SpecialInstructions TEXT,
    OrderDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EstimatedReadyTime DATETIME, -- When food should be ready
    ActualReadyTime DATETIME, -- When food was actually ready
    ServedTime DATETIME, -- When food was served to customer
    PaidTime DATETIME, -- When payment was completed
    CreatedBy VARCHAR(100), -- Staff member who took the order
    
    FOREIGN KEY (TableSessionId) REFERENCES TableSessions(Id) ON DELETE SET NULL,
    INDEX idx_order_status (OrderStatus, OrderDate),
    INDEX idx_order_table (TableSessionId),
    INDEX idx_order_date (OrderDate),
    INDEX idx_order_number (OrderNumber)
);

-- 6. Order Items (Individual items within an order)
CREATE TABLE IF NOT EXISTS OrderItems (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    OrderId INT NOT NULL,
    MenuItemId INT NOT NULL,
    Quantity INT NOT NULL DEFAULT 1,
    UnitPrice DECIMAL(10,2) NOT NULL, -- Price at time of order (may differ from current menu price)
    TotalPrice DECIMAL(10,2) NOT NULL, -- UnitPrice * Quantity + modifiers
    SpecialInstructions TEXT, -- Item-specific instructions
    ItemStatus ENUM('Ordered', 'Preparing', 'Ready', 'Served') NOT NULL DEFAULT 'Ordered',
    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (OrderId) REFERENCES CustomerOrders(Id) ON DELETE CASCADE,
    FOREIGN KEY (MenuItemId) REFERENCES MenuItems(Id),
    INDEX idx_order_items (OrderId),
    INDEX idx_item_status (ItemStatus)
);

-- 7. Order Item Modifiers (Applied modifiers for each order item)
CREATE TABLE IF NOT EXISTS OrderItemModifiers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    OrderItemId INT NOT NULL,
    ModifierId INT NOT NULL,
    ModifierName VARCHAR(100) NOT NULL, -- Store name at time of order
    PriceAdjustment DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    
    FOREIGN KEY (OrderItemId) REFERENCES OrderItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (ModifierId) REFERENCES ItemModifiers(Id),
    INDEX idx_order_item_modifiers (OrderItemId)
);

-- 8. Payment Transactions
CREATE TABLE IF NOT EXISTS PaymentTransactions (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    OrderId INT NOT NULL,
    TransactionType ENUM('Payment', 'Refund', 'Tip') NOT NULL DEFAULT 'Payment',
    PaymentMethod ENUM('Cash', 'Card', 'Gift Card') NOT NULL,
    Amount DECIMAL(10,2) NOT NULL,
    AmountReceived DECIMAL(10,2), -- For cash payments
    ChangeGiven DECIMAL(10,2), -- For cash payments
    TipAmount DECIMAL(10,2) DEFAULT 0.00,
    CardLastFour VARCHAR(4), -- Last 4 digits of card
    TransactionReference VARCHAR(100), -- External payment reference
    ProcessedBy VARCHAR(100), -- Staff member who processed payment
    TransactionDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Notes TEXT,
    
    FOREIGN KEY (OrderId) REFERENCES CustomerOrders(Id) ON DELETE CASCADE,
    INDEX idx_payment_order (OrderId),
    INDEX idx_payment_date (TransactionDate),
    INDEX idx_payment_method (PaymentMethod)
);

-- Create view for complete menu with category info
CREATE OR REPLACE VIEW MenuItemsWithCategory AS
SELECT 
    mi.Id,
    mi.Name,
    mi.Description,
    mi.Price,
    mi.ImagePath,
    mi.PrepTime,
    mi.Calories,
    mi.IsSpicy,
    mi.IsVegetarian,
    mi.IsVegan,
    mi.IsGlutenFree,
    mi.IsAvailable,
    mi.SortOrder AS ItemSortOrder,
    mc.Id AS CategoryId,
    mc.Name AS CategoryName,
    mc.Icon AS CategoryIcon,
    mc.SortOrder AS CategorySortOrder,
    CASE 
        WHEN mi.IsSpicy = TRUE THEN ' '
        ELSE ''
    END AS SpiceIndicator,
    CASE 
        WHEN mi.IsVegetarian = TRUE THEN ' '
        WHEN mi.IsVegan = TRUE THEN ' '
        ELSE ''
    END AS DietIndicator
FROM MenuItems mi
INNER JOIN MenuCategories mc ON mi.CategoryId = mc.Id
WHERE mi.IsAvailable = TRUE AND mc.IsActive = TRUE
ORDER BY mc.SortOrder, mi.SortOrder;

SELECT 'Menu management database schema created successfully!' AS Result;
