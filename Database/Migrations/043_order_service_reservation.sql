-- 043: Administrator toggle for Reservations alongside Table / Collection / Delivery.

ALTER TABLE order_service_availability_settings
    ADD COLUMN IF NOT EXISTS reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
        AFTER delivery_enabled;

ALTER TABLE order_service_availability_events
    ADD COLUMN IF NOT EXISTS previous_reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
        AFTER new_delivery_enabled,
    ADD COLUMN IF NOT EXISTS new_reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
        AFTER previous_reservation_enabled;
