ALTER TABLE cloud_reservations
    ADD COLUMN IF NOT EXISTS promo_code VARCHAR(64) NOT NULL DEFAULT '' AFTER customer_email;
