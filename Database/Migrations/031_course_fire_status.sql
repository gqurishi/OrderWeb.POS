ALTER TABLE order_items
    ADD COLUMN IF NOT EXISTS course_type VARCHAR(30) NULL AFTER print_in_red,
    ADD COLUMN IF NOT EXISTS fired_at DATETIME NULL AFTER course_type,
    ADD COLUMN IF NOT EXISTS fired_by VARCHAR(150) NULL AFTER fired_at;

CREATE INDEX IF NOT EXISTS idx_order_items_course_fired
    ON order_items (order_id, course_type, fired_at);
