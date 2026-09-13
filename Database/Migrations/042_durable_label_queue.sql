-- Phase 10: claimed, retryable and idempotent durable label queue.

ALTER TABLE label_print_jobs
    MODIFY COLUMN status ENUM(
        'created', 'pending', 'claimed', 'printing', 'completed', 'failed',
        'retry_waiting', 'needs_attention', 'cancelled', 'outcome_unknown'
    ) NOT NULL DEFAULT 'created',
    ADD COLUMN idempotency_key CHAR(64) NULL AFTER client_request_id,
    ADD COLUMN send_revision INT NOT NULL DEFAULT 1 AFTER idempotency_key,
    ADD COLUMN claimed_by_instance VARCHAR(160) NULL AFTER max_retries,
    ADD COLUMN claim_token VARCHAR(36) NULL AFTER claimed_by_instance,
    ADD COLUMN claimed_at DATETIME NULL AFTER claim_token,
    ADD COLUMN next_attempt_at DATETIME NULL AFTER claimed_at,
    ADD COLUMN attention_at DATETIME NULL AFTER next_attempt_at,
    ADD COLUMN cancelled_at DATETIME NULL AFTER attention_at,
    ADD COLUMN cancelled_by VARCHAR(120) NULL AFTER cancelled_at,
    ADD COLUMN cancellation_reason VARCHAR(300) NULL AFTER cancelled_by;

UPDATE label_print_jobs
SET idempotency_key = LOWER(SHA2(CONCAT(
        COALESCE(order_id, 0), ':', COALESCE(order_item_id, 0), ':legacy:', id
    ), 256))
WHERE idempotency_key IS NULL;

ALTER TABLE label_print_jobs
    MODIFY COLUMN idempotency_key CHAR(64) NOT NULL,
    ADD UNIQUE KEY uq_label_job_idempotency (idempotency_key),
    ADD UNIQUE KEY uq_label_job_claim_token (claim_token),
    ADD INDEX idx_label_job_claim (status, next_attempt_at, created_at),
    ADD INDEX idx_label_job_printer_claim (printer_id, status, claimed_at);

CREATE TABLE IF NOT EXISTS label_print_job_events (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    job_id VARCHAR(36) NOT NULL,
    from_status VARCHAR(30) NULL,
    to_status VARCHAR(30) NOT NULL,
    actor VARCHAR(160) NOT NULL,
    message VARCHAR(500) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_label_job_event_job FOREIGN KEY (job_id) REFERENCES label_print_jobs(id) ON DELETE CASCADE,
    INDEX idx_label_job_events_job (job_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

UPDATE label_print_jobs SET status = 'pending' WHERE status = 'created';

