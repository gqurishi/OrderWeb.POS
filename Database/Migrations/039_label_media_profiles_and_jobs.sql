-- Phase 5: durable label media profiles and label-job history.
-- Toshiba executable drivers are never stored in the database.

ALTER TABLE network_printers
    ADD COLUMN module_identifier VARCHAR(80) NULL AFTER protocol,
    ADD COLUMN last_successful_physical_test DATETIME NULL AFTER last_seen,
    ADD COLUMN firmware_version VARCHAR(100) NULL AFTER last_successful_physical_test;

CREATE TABLE IF NOT EXISTS label_media_profiles (
    id VARCHAR(36) PRIMARY KEY,
    profile_name VARCHAR(120) NOT NULL,
    manufacturer VARCHAR(50) NOT NULL DEFAULT 'toshiba',
    model_code VARCHAR(100) NOT NULL DEFAULT 'b-fv4d-gs14',
    width_mm DECIMAL(7,2) NOT NULL,
    height_mm DECIMAL(7,2) NOT NULL,
    gap_mm DECIMAL(7,2) NOT NULL DEFAULT 0,
    sensor_type ENUM('gap', 'blackmark', 'continuous') NOT NULL,
    darkness INT NOT NULL DEFAULT 0,
    speed_ips INT NOT NULL DEFAULT 4,
    horizontal_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0,
    vertical_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0,
    finishing_mode ENUM('tearoff', 'cut') NOT NULL DEFAULT 'tearoff',
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_label_media_profile_name (profile_name),
    INDEX idx_label_media_profiles_active_model (is_active, manufacturer, model_code),
    CONSTRAINT chk_label_media_width CHECK (width_mm > 0 AND width_mm <= 108),
    CONSTRAINT chk_label_media_height CHECK (height_mm > 0 AND height_mm <= 1000),
    CONSTRAINT chk_label_media_gap CHECK (
        (sensor_type = 'continuous' AND gap_mm = 0)
        OR (sensor_type <> 'continuous' AND gap_mm > 0)
    ),
    CONSTRAINT chk_label_media_darkness CHECK (darkness BETWEEN -10 AND 10),
    CONSTRAINT chk_label_media_speed CHECK (speed_ips BETWEEN 2 AND 6),
    CONSTRAINT chk_label_media_offsets CHECK (
        horizontal_offset_mm BETWEEN -10 AND 10
        AND vertical_offset_mm BETWEEN -10 AND 10
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE network_printers
    ADD COLUMN label_media_profile_id VARCHAR(36) NULL AFTER label_profile,
    ADD CONSTRAINT fk_network_printer_label_media_profile
        FOREIGN KEY (label_media_profile_id) REFERENCES label_media_profiles(id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS label_print_jobs (
    id VARCHAR(36) PRIMARY KEY,
    printer_id INT NOT NULL,
    media_profile_id VARCHAR(36) NOT NULL,
    order_id INT NULL,
    order_item_id INT NULL,
    source_terminal VARCHAR(120) NOT NULL,
    client_request_id VARCHAR(100) NULL,
    job_type ENUM('item', 'component', 'test') NOT NULL,
    printable_name_snapshot VARCHAR(300) NOT NULL,
    payload_snapshot JSON NOT NULL,
    label_template_version VARCHAR(40) NOT NULL,
    quantity_copies INT NOT NULL DEFAULT 1,
    generated_tpcl_data LONGBLOB NULL,
    generated_payload_sha256 CHAR(64) NULL,
    status ENUM('pending', 'printing', 'completed', 'failed', 'cancelled', 'outcome_unknown') NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    max_retries INT NOT NULL DEFAULT 5,
    error_message TEXT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    attempt_at DATETIME NULL,
    completed_at DATETIME NULL,
    reprint_of_job_id VARCHAR(36) NULL,
    CONSTRAINT fk_label_jobs_printer FOREIGN KEY (printer_id) REFERENCES network_printers(id),
    CONSTRAINT fk_label_jobs_media_profile FOREIGN KEY (media_profile_id) REFERENCES label_media_profiles(id),
    CONSTRAINT fk_label_jobs_order FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE SET NULL,
    CONSTRAINT fk_label_jobs_order_item FOREIGN KEY (order_item_id) REFERENCES order_items(id) ON DELETE SET NULL,
    CONSTRAINT fk_label_jobs_reprint FOREIGN KEY (reprint_of_job_id) REFERENCES label_print_jobs(id) ON DELETE SET NULL,
    CONSTRAINT chk_label_job_name CHECK (CHAR_LENGTH(TRIM(printable_name_snapshot)) BETWEEN 1 AND 300),
    CONSTRAINT chk_label_job_copies CHECK (quantity_copies BETWEEN 1 AND 99),
    CONSTRAINT chk_label_job_retry CHECK (retry_count >= 0 AND max_retries BETWEEN 0 AND 20),
    UNIQUE KEY uq_label_job_client_request (source_terminal, client_request_id),
    INDEX idx_label_jobs_worker (status, created_at),
    INDEX idx_label_jobs_printer_status (printer_id, status, created_at),
    INDEX idx_label_jobs_order_item (order_id, order_item_id),
    INDEX idx_label_jobs_reprint (reprint_of_job_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO label_media_profiles
    (id, profile_name, width_mm, height_mm, gap_mm, sensor_type, darkness, speed_ips,
     horizontal_offset_mm, vertical_offset_mm, finishing_mode, is_active)
VALUES
    ('toshiba-60x40-container', 'Toshiba 60 × 40 mm Container', 60, 40, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('toshiba-51x30-compact', 'Toshiba 51 × 30 mm Compact', 51, 30, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1),
    ('toshiba-80x50-delivery', 'Toshiba 80 × 50 mm Delivery', 80, 50, 3, 'gap', 0, 4, 0, 0, 'tearoff', 1)
ON DUPLICATE KEY UPDATE
    width_mm = VALUES(width_mm),
    height_mm = VALUES(height_mm),
    updated_at = CURRENT_TIMESTAMP;

UPDATE network_printers
SET technology = 'label',
    manufacturer = 'toshiba',
    model_code = 'b-fv4d-gs14',
    module_identifier = 'toshiba_tpcl',
    resolution_dpi = 203,
    label_media_profile_id = COALESCE(label_media_profile_id, 'toshiba-60x40-container')
WHERE printer_type = 'label'
  AND LOWER(manufacturer) = 'toshiba'
  AND model_code IN ('B-FV4D-GS14-QM-R', 'b-fv4d-gs14');
