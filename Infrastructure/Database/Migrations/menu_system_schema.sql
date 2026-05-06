-- Phase 2: Menu Management and Order Taking System
-- Complete database schema for restaurant menu and ordering

-- 1. Menu Categories (Appetizers, Main Course, Beverages, Desserts)
CREATE TABLE IF NOT EXISTS MenuCategories (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL UNIQUE,
    Description TEXT,
    SortOrder INT NOT NULL DEFAULT 0,
    Icon VARCHAR(50), -- 🥗 🍖 🍹 🍰 etc.
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

-- Insert sample menu categories
INSERT IGNORE INTO MenuCategories (Name, Description, SortOrder, Icon) VALUES
('Appetizers', 'Start your meal with our delicious appetizers', 1, '🥗'),
('Soups & Salads', 'Fresh soups and crisp salads', 2, '🥣'),
('Main Course', 'Hearty entrees and signature dishes', 3, '🍖'),
('Pasta & Rice', 'Italian classics and rice dishes', 4, '🍝'),
('Beverages', 'Refreshing drinks and specialty beverages', 5, '🍹'),
('Desserts', 'Sweet endings to your perfect meal', 6, '🍰');

-- Insert sample menu items
INSERT IGNORE INTO MenuItems (CategoryId, Name, Description, Price, PrepTime, IsSpicy, IsVegetarian, SortOrder) VALUES
-- Appetizers
(1, 'Caesar Salad', 'Crisp romaine lettuce with parmesan, croutons and caesar dressing', 12.99, 5, FALSE, TRUE, 1),
(1, 'Buffalo Wings', '8 pieces of spicy buffalo wings with ranch dip', 15.99, 15, TRUE, FALSE, 2),
(1, 'Mozzarella Sticks', '6 golden fried mozzarella sticks with marinara sauce', 10.99, 8, FALSE, TRUE, 3),
(1, 'Loaded Nachos', 'Tortilla chips topped with cheese, jalapeños, sour cream', 13.99, 10, TRUE, TRUE, 4),

-- Soups & Salads  
(2, 'Tomato Basil Soup', 'Creamy tomato soup with fresh basil', 8.99, 5, FALSE, TRUE, 1),
(2, 'Greek Salad', 'Mixed greens, olives, feta cheese, cucumber, tomatoes', 14.99, 7, FALSE, TRUE, 2),
(2, 'Chicken Noodle Soup', 'Homemade chicken soup with vegetables and noodles', 9.99, 8, FALSE, FALSE, 3),

-- Main Course
(3, 'Grilled Salmon', 'Atlantic salmon with lemon herb butter, served with vegetables', 28.99, 20, FALSE, FALSE, 1),
(3, 'BBQ Ribs', 'Full rack of baby back ribs with BBQ sauce and coleslaw', 32.99, 25, FALSE, FALSE, 2),
(3, 'Chicken Parmesan', 'Breaded chicken breast with marinara and mozzarella', 24.99, 18, FALSE, FALSE, 3),
(3, 'Vegetarian Burger', 'Plant-based patty with avocado, lettuce, tomato', 19.99, 12, FALSE, TRUE, 4),

-- Pasta & Rice
(4, 'Spaghetti Carbonara', 'Classic Italian pasta with eggs, cheese, pancetta', 22.99, 15, FALSE, FALSE, 1),
(4, 'Vegetable Pad Thai', 'Thai rice noodles with vegetables and peanut sauce', 18.99, 12, TRUE, TRUE, 2),
(4, 'Chicken Fried Rice', 'Wok-fried rice with chicken, vegetables, soy sauce', 16.99, 10, FALSE, FALSE, 3),

-- Beverages
(5, 'Fresh Orange Juice', 'Freshly squeezed orange juice', 4.99, 2, FALSE, TRUE, 1),
(5, 'Iced Coffee', 'Cold brew coffee served over ice', 3.99, 2, FALSE, TRUE, 2),
(5, 'Soft Drinks', 'Coca-Cola, Pepsi, Sprite, Orange Fanta', 2.99, 1, FALSE, TRUE, 3),
(5, 'Craft Beer', 'Local craft beer selection', 6.99, 1, FALSE, FALSE, 4),

-- Desserts
(6, 'Chocolate Cake', 'Rich chocolate cake with chocolate frosting', 8.99, 5, FALSE, TRUE, 1),
(6, 'Cheesecake', 'New York style cheesecake with berry compote', 7.99, 3, FALSE, TRUE, 2),
(6, 'Ice Cream Sundae', 'Vanilla ice cream with chocolate sauce and whipped cream', 6.99, 3, FALSE, TRUE, 3);

-- Insert sample modifiers
INSERT IGNORE INTO ItemModifiers (Name, Description, PriceAdjustment, ModifierType, SortOrder) VALUES
-- Size options
('Small', 'Small portion', -2.00, 'Size', 1),
('Regular', 'Regular portion', 0.00, 'Size', 2),  
('Large', 'Large portion', 3.00, 'Size', 3),

-- Add-ons
('Extra Cheese', 'Additional cheese', 2.50, 'Addition', 10),
('Extra Bacon', 'Additional bacon strips', 3.00, 'Addition', 11),
('Avocado', 'Fresh avocado slices', 2.00, 'Addition', 12),
('Grilled Chicken', 'Add grilled chicken breast', 5.00, 'Addition', 13),

-- Removals/Substitutions
('No Onions', 'Remove onions', 0.00, 'Removal', 20),
('No Tomatoes', 'Remove tomatoes', 0.00, 'Removal', 21),
('Gluten-Free Bread', 'Substitute with gluten-free bread', 1.50, 'Substitution', 22),
('Side Salad Instead', 'Replace fries with side salad', 0.00, 'Substitution', 23);

-- Link modifiers to appropriate menu items (sample)
INSERT IGNORE INTO MenuItemModifiers (MenuItemId, ModifierId, IsDefault) VALUES
-- Caesar Salad can have chicken, size options
(1, 2, TRUE), -- Regular size default
(1, 7, FALSE), -- Can add grilled chicken

-- Buffalo Wings size options  
(2, 1, FALSE), -- Small (6 wings)
(2, 2, TRUE),  -- Regular (8 wings) - default
(2, 3, FALSE), -- Large (12 wings)

-- Burgers can have many add-ons
(10, 4, FALSE), -- Extra cheese
(10, 5, FALSE), -- Extra bacon  
(10, 6, FALSE), -- Avocado
(10, 9, FALSE), -- No onions
(10, 10, FALSE); -- No tomatoes

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
        WHEN mi.IsSpicy = TRUE THEN '🌶️ '
        ELSE ''
    END AS SpiceIndicator,
    CASE 
        WHEN mi.IsVegetarian = TRUE THEN '🌱 '
        WHEN mi.IsVegan = TRUE THEN '🌿 '
        ELSE ''
    END AS DietIndicator
FROM MenuItems mi
INNER JOIN MenuCategories mc ON mi.CategoryId = mc.Id
WHERE mi.IsAvailable = TRUE AND mc.IsActive = TRUE
ORDER BY mc.SortOrder, mi.SortOrder;

SELECT 'Menu management database schema created successfully!' AS Result;