-- Preserve the complete payment declaration received with OrderWeb orders.
-- These fields describe cloud settlement and remain separate from local POS
-- payment attempts and lifecycle state.

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS cash_tip_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER tax_amount,
    ADD COLUMN IF NOT EXISTS card_tip_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER cash_tip_amount,
    ADD COLUMN IF NOT EXISTS payment_status VARCHAR(32) NULL AFTER payment_method,
    ADD COLUMN IF NOT EXISTS amount_paid DECIMAL(10,2) NULL AFTER payment_status,
    ADD COLUMN IF NOT EXISTS payment_provider VARCHAR(64) NULL AFTER amount_paid,
    ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(150) NULL AFTER payment_provider,
    ADD COLUMN IF NOT EXISTS payment_currency VARCHAR(3) NULL AFTER payment_reference,
    ADD COLUMN IF NOT EXISTS voucher_code VARCHAR(100) NULL AFTER payment_currency;

CREATE INDEX IF NOT EXISTS idx_orders_cloud_payment
    ON orders (source_channel, payment_status, payment_method, created_at);
