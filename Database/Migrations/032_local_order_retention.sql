-- 032: Audited rolling two-year cleanup for local POS orders after confirmed cloud reporting.

ALTER TABLE local_order_deletion_audit
    MODIFY COLUMN action_type ENUM('web_cache_purge', 'admin_test_delete', 'local_retention_purge') NOT NULL;

CREATE INDEX IF NOT EXISTS idx_orders_local_retention
    ON orders (source_channel, created_at, sync_status, is_open);
