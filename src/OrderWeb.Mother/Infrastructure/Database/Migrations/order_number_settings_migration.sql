-- Order Number Settings Migration
-- Creates table to store order number prefix and daily counter

CREATE TABLE IF NOT EXISTS order_number_settings (
    id INT PRIMARY KEY AUTO_INCREMENT,
    order_prefix VARCHAR(3) NOT NULL DEFAULT 'KIT',
    daily_counter INT NOT NULL DEFAULT 0,
    counter_date DATE NOT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Insert default settings
INSERT INTO order_number_settings (order_prefix, daily_counter, counter_date)
VALUES ('KIT', 0, CURDATE())
ON DUPLICATE KEY UPDATE id=id;

-- Create index on counter_date for fast lookups
CREATE INDEX idx_counter_date ON order_number_settings(counter_date);
