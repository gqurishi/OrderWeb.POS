-- 014: Two-way reservation sync (POS create + upload queue + ACK retry).

ALTER TABLE cloud_reservations
    ADD COLUMN IF NOT EXISTS local_id VARCHAR(64) NULL AFTER cloud_id,
    ADD COLUMN IF NOT EXISTS upload_status VARCHAR(20) NOT NULL DEFAULT 'synced' AFTER pos_print_status;

CREATE INDEX IF NOT EXISTS idx_cloud_reservations_upload_status ON cloud_reservations (upload_status);
CREATE INDEX IF NOT EXISTS idx_cloud_reservations_local_id ON cloud_reservations (local_id);

CREATE TABLE IF NOT EXISTS reservation_pending_acks (
    id INT AUTO_INCREMENT PRIMARY KEY,
    cloud_reservation_id VARCHAR(64) NOT NULL,
    ack_status VARCHAR(32) NOT NULL DEFAULT 'seen',
    attempts INT NOT NULL DEFAULT 0,
    last_error VARCHAR(500) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_attempt_at DATETIME NULL,
    UNIQUE KEY ux_reservation_pending_acks_cloud (cloud_reservation_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
