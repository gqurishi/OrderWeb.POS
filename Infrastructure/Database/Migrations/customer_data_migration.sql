-- Unified customer data for collection and delivery orders
CREATE TABLE IF NOT EXISTS customer_data (
    id INT AUTO_INCREMENT PRIMARY KEY,
    order_types VARCHAR(20) NOT NULL DEFAULT 'collection',
    name VARCHAR(255) NOT NULL,
    phone_number VARCHAR(50) NOT NULL,
    full_address TEXT NULL,
    city VARCHAR(100) NULL,
    county VARCHAR(100) NULL,
    postcode VARCHAR(20) NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    last_collection_order_date DATETIME NULL,
    last_delivery_order_date DATETIME NULL,
    INDEX idx_customer_phone (phone_number),
    INDEX idx_customer_name (name),
    INDEX idx_customer_postcode (postcode),
    INDEX idx_customer_order_types (order_types)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
