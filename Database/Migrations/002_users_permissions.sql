-- OrderWeb POS production schema
-- 002: Users, login activity, permission catalog, and role defaults.

CREATE TABLE IF NOT EXISTS users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NULL,
    username VARCHAR(50) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    role VARCHAR(20) NOT NULL DEFAULT 'user',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_activities (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NULL,
    action VARCHAR(50) NOT NULL,
    details TEXT,
    ip_address VARCHAR(45),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
    INDEX idx_user_activities_user_time (user_id, created_at),
    INDEX idx_user_activities_action_time (action, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS permissions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    permission_key VARCHAR(120) NOT NULL UNIQUE,
    display_name VARCHAR(150) NOT NULL,
    category VARCHAR(80) NOT NULL,
    description VARCHAR(255) NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS role_permissions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    role VARCHAR(20) NOT NULL,
    permission_key VARCHAR(120) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_role_permission (role, permission_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_permissions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    permission_key VARCHAR(120) NOT NULL,
    is_allowed BOOLEAN NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_user_permission (user_id, permission_key),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO permissions (permission_key, display_name, category, description)
VALUES
    ('dashboard.admin.view', 'View Admin Dashboard', 'dashboard', 'Access admin dashboard page'),
    ('weborders.view', 'View Web Orders', 'orders', 'Access online/web orders module'),
    ('giftcards.manage', 'Manage Gift Cards', 'payments', 'Check and redeem gift cards'),
    ('loyalty.manage', 'Manage Loyalty', 'payments', 'Lookup and update loyalty points'),
    ('reservation.manage', 'Manage Reservations', 'restaurant', 'Access reservation workflow'),
    ('report.view', 'View Reports', 'reports', 'Access reports module'),
    ('inventory.view', 'View Inventory', 'reports', 'Access inventory module'),
    ('settings.manage_business', 'Manage Business Settings', 'settings', 'Edit business settings'),
    ('users.manage', 'Manage Users', 'settings', 'Create, edit, and delete users'),
    ('ordernumber.manage', 'Manage Order Prefix', 'settings', 'Edit order numbering settings'),
    ('printers.manage', 'Manage Printers', 'printing', 'Configure printer setup and groups'),
    ('foodmenu.manage', 'Manage Food Menu', 'menu', 'Create/edit menu items and categories')
ON DUPLICATE KEY UPDATE
    display_name = VALUES(display_name),
    category = VALUES(category),
    description = VALUES(description);

INSERT INTO role_permissions (role, permission_key)
SELECT 'admin', permission_key FROM permissions
ON DUPLICATE KEY UPDATE permission_key = VALUES(permission_key);

INSERT INTO role_permissions (role, permission_key)
VALUES
    ('manager', 'weborders.view'),
    ('manager', 'giftcards.manage'),
    ('manager', 'loyalty.manage'),
    ('manager', 'reservation.manage')
ON DUPLICATE KEY UPDATE permission_key = VALUES(permission_key);

