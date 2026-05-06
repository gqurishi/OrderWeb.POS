-- Collection Customers Table Migration
-- Stores customer information for collection orders

CREATE TABLE IF NOT EXISTS collection_customers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50) NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    last_order_date DATETIME NULL,
    INDEX idx_phone (phone_number),
    INDEX idx_name (name)
);

-- Add order_type and customer_id to orders table if not exists
ALTER TABLE orders 
ADD COLUMN IF NOT EXISTS order_type VARCHAR(50) DEFAULT 'dine-in',
ADD COLUMN IF NOT EXISTS customer_id INT NULL,
ADD COLUMN IF NOT EXISTS customer_name VARCHAR(255) NULL,
ADD COLUMN IF NOT EXISTS customer_phone VARCHAR(50) NULL;

-- Add index for collection orders
ALTER TABLE orders ADD INDEX IF NOT EXISTS idx_order_type (order_type);
ALTER TABLE orders ADD INDEX IF NOT EXISTS idx_customer_id (customer_id);
