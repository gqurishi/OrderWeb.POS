-- Phase 2 performance indexes for event-driven local order screens.
-- These match the Order History date/lifecycle and customer search query shapes.

CREATE INDEX IF NOT EXISTS idx_orders_local_history
    ON orders (source_channel, created_at, local_lifecycle_state, status, order_type);

CREATE INDEX IF NOT EXISTS idx_orders_customer_phone_created
    ON orders (customer_phone, created_at);
