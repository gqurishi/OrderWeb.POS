-- 039: Admin-set idle logout minutes for User, Manager, and Cashier (Mother and Client).
-- Default 3. Allowed range 3 to 60 is enforced in the app.

ALTER TABLE business_info
    ADD COLUMN IF NOT EXISTS till_logout_minutes INT NOT NULL DEFAULT 3;
