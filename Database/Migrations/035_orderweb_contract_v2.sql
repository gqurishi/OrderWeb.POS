-- Complete OrderWeb contract v2 storage. Additive only.

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS promo_code VARCHAR(100) NULL AFTER voucher_code,
    ADD COLUMN IF NOT EXISTS gift_card_number_masked VARCHAR(32) NULL AFTER promo_code,
    ADD COLUMN IF NOT EXISTS gift_card_amount_paid DECIMAL(10,2) NULL AFTER gift_card_number_masked,
    ADD COLUMN IF NOT EXISTS gift_card_remaining_balance DECIMAL(10,2) NULL AFTER gift_card_amount_paid,
    ADD COLUMN IF NOT EXISTS loyalty_points_earned INT NOT NULL DEFAULT 0 AFTER gift_card_remaining_balance,
    ADD COLUMN IF NOT EXISTS loyalty_points_redeemed INT NOT NULL DEFAULT 0 AFTER loyalty_points_earned,
    ADD COLUMN IF NOT EXISTS loyalty_points_discount DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER loyalty_points_redeemed,
    ADD COLUMN IF NOT EXISTS loyalty_balance_after INT NULL AFTER loyalty_points_discount;

ALTER TABLE order_items
    ADD COLUMN IF NOT EXISTS cloud_item_external_id VARCHAR(100) NULL AFTER cloud_item_id;

CREATE INDEX IF NOT EXISTS idx_order_items_cloud_external
    ON order_items (cloud_item_external_id);
