-- Immutable kitchen deltas for additions, changes, quantity reductions, and voids.

CREATE TABLE IF NOT EXISTS order_kitchen_item_state (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    order_id INT NOT NULL,
    client_item_id VARCHAR(100) NOT NULL,
    item_name VARCHAR(180) NOT NULL,
    display_name VARCHAR(180) NULL,
    menu_item_id VARCHAR(100) NULL,
    variant_id VARCHAR(100) NULL,
    print_group_id VARCHAR(36) NULL,
    committed_quantity INT NOT NULL DEFAULT 0,
    notes TEXT NULL,
    modifiers TEXT NULL,
    addons_json JSON NULL,
    item_fingerprint CHAR(64) NOT NULL,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_kitchen_item_state (order_id, client_item_id),
    INDEX idx_kitchen_item_state_order (order_id),
    CONSTRAINT fk_kitchen_item_state_order FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_kitchen_revisions (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    revision_id VARCHAR(36) NOT NULL,
    order_id INT NOT NULL,
    revision_number INT NOT NULL,
    ticket_type ENUM('initial', 'addition', 'change', 'void', 'mixed') NOT NULL,
    status ENUM('queued', 'printing', 'printed', 'partial', 'failed', 'cancelled') NOT NULL DEFAULT 'queued',
    created_by VARCHAR(150) NULL,
    reason VARCHAR(255) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME NULL,
    failure_reason VARCHAR(255) NULL,
    UNIQUE KEY uq_kitchen_revision_id (revision_id),
    UNIQUE KEY uq_kitchen_revision_number (order_id, revision_number),
    INDEX idx_kitchen_revision_status (status, created_at),
    CONSTRAINT fk_kitchen_revision_order FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS order_kitchen_revision_items (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    line_id VARCHAR(36) NOT NULL,
    revision_id BIGINT NOT NULL,
    client_item_id VARCHAR(100) NOT NULL,
    action_type ENUM('new', 'add', 'void', 'change') NOT NULL,
    quantity INT NOT NULL,
    previous_quantity INT NOT NULL DEFAULT 0,
    current_quantity INT NOT NULL DEFAULT 0,
    item_name VARCHAR(180) NOT NULL,
    display_name VARCHAR(180) NULL,
    menu_item_id VARCHAR(100) NULL,
    variant_id VARCHAR(100) NULL,
    print_group_id VARCHAR(36) NULL,
    notes TEXT NULL,
    previous_notes TEXT NULL,
    modifiers TEXT NULL,
    addons_json JSON NULL,
    print_status ENUM('queued', 'printed', 'failed') NOT NULL DEFAULT 'queued',
    failure_reason VARCHAR(255) NULL,
    printed_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_kitchen_revision_line (line_id),
    INDEX idx_kitchen_revision_items_revision (revision_id),
    INDEX idx_kitchen_revision_items_status (print_status, created_at),
    CONSTRAINT fk_kitchen_revision_item_revision FOREIGN KEY (revision_id) REFERENCES order_kitchen_revisions(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
