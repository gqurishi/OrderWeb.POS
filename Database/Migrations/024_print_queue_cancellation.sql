-- Auditable cancellation for stale or unwanted physical print jobs.

ALTER TABLE network_print_queue
    MODIFY COLUMN status ENUM('pending', 'printing', 'completed', 'failed', 'cancelled')
        NOT NULL DEFAULT 'pending',
    ADD COLUMN cancelled_at DATETIME NULL AFTER completed_at,
    ADD COLUMN cancelled_by_user_id INT NULL AFTER cancelled_at,
    ADD COLUMN cancelled_by_name VARCHAR(120) NULL AFTER cancelled_by_user_id,
    ADD COLUMN cancellation_reason VARCHAR(255) NULL AFTER cancelled_by_name;

CREATE TABLE IF NOT EXISTS print_queue_cancellation_audit (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    cancelled_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cutoff_at DATETIME NOT NULL,
    scope VARCHAR(30) NOT NULL,
    printer_id INT NULL,
    job_count INT NOT NULL DEFAULT 0,
    cancelled_by_user_id INT NULL,
    cancelled_by_name VARCHAR(120) NOT NULL,
    reason VARCHAR(255) NOT NULL,
    INDEX idx_print_cancel_audit_time (cancelled_at),
    INDEX idx_print_cancel_audit_printer (printer_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
