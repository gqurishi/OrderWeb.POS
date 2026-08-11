ALTER TABLE network_printers
    ADD COLUMN IF NOT EXISTS supports_two_color BOOLEAN NOT NULL DEFAULT FALSE AFTER has_buzzer;

ALTER TABLE order_items
    ADD COLUMN IF NOT EXISTS print_in_red BOOLEAN NOT NULL DEFAULT FALSE AFTER print_group_id;
