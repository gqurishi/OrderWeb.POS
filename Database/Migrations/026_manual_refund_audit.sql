-- 026: Add accountable audit details for manual refunds processed outside the POS.

ALTER TABLE order_refunds
    ADD COLUMN IF NOT EXISTS refunded_by_user_id INT NULL AFTER refunded_by,
    ADD COLUMN IF NOT EXISTS refunded_by_role VARCHAR(20) NULL AFTER refunded_by_user_id,
    ADD COLUMN IF NOT EXISTS terminal_name VARCHAR(120) NULL AFTER refunded_by_role,
    ADD COLUMN IF NOT EXISTS external_reference VARCHAR(120) NULL AFTER terminal_name,
    ADD INDEX IF NOT EXISTS idx_order_refunds_user_time (refunded_by_user_id, refunded_at),
    ADD INDEX IF NOT EXISTS idx_order_refunds_external_reference (external_reference);
