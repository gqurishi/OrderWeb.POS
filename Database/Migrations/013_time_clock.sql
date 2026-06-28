-- 013: Staff time clock sessions (clock in/out from any terminal).

CREATE TABLE IF NOT EXISTS time_clock_sessions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    clock_in_at DATETIME NOT NULL,
    clock_out_at DATETIME NULL,
    terminal_in VARCHAR(100) NOT NULL DEFAULT '',
    terminal_out VARCHAR(100) NULL,
    business_date DATE NOT NULL,
    worked_minutes INT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'open',
    synced_at DATETIME NULL,
    adjustment_note VARCHAR(500) NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    INDEX idx_time_clock_user_status (user_id, status),
    INDEX idx_time_clock_business_date (business_date),
    INDEX idx_time_clock_clock_in (clock_in_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
