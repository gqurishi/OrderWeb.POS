-- OrderWeb POS production schema verification.
-- Run after all bundled migrations on the Mother database.
-- The result set must be empty before the app is considered ready.

SELECT required.table_name AS missing_table
FROM (
    SELECT 'app_schema_version' AS table_name UNION ALL
    SELECT 'migration_history' UNION ALL
    SELECT 'settings' UNION ALL
    SELECT 'cloud_config' UNION ALL
    SELECT 'business_info' UNION ALL
    SELECT 'customer_data' UNION ALL
    SELECT 'pos_customer_sync_queue' UNION ALL
    SELECT 'users' UNION ALL
    SELECT 'user_activities' UNION ALL
    SELECT 'permissions' UNION ALL
    SELECT 'role_permissions' UNION ALL
    SELECT 'user_permissions' UNION ALL
    SELECT 'FoodMenuCategories' UNION ALL
    SELECT 'FoodMenuItems' UNION ALL
    SELECT 'PredefinedComments' UNION ALL
    SELECT 'PredefinedNotes' UNION ALL
    SELECT 'ItemComments' UNION ALL
    SELECT 'MenuItemQuickNotes' UNION ALL
    SELECT 'MenuItemComponents' UNION ALL
    SELECT 'ItemComponents' UNION ALL
    SELECT 'MealDeals' UNION ALL
    SELECT 'TastingMenus' UNION ALL
    SELECT 'MenuItemVariants' UNION ALL
    SELECT 'MenuCategories' UNION ALL
    SELECT 'MenuItems' UNION ALL
    SELECT 'orders' UNION ALL
    SELECT 'order_items' UNION ALL
    SELECT 'order_item_addons' UNION ALL
    SELECT 'order_payments' UNION ALL
    SELECT 'order_events' UNION ALL
    SELECT 'order_service_charge_events' UNION ALL
    SELECT 'order_item_send_tracking' UNION ALL
    SELECT 'order_refunds' UNION ALL
    SELECT 'collection_customers' UNION ALL
    SELECT 'delivery_customers' UNION ALL
    SELECT 'order_number_settings' UNION ALL
    SELECT 'table_service_charge_settings' UNION ALL
    SELECT 'table_service_charge_setting_events' UNION ALL
    SELECT 'order_service_availability_settings' UNION ALL
    SELECT 'order_service_availability_events' UNION ALL
    SELECT 'print_groups' UNION ALL
    SELECT 'network_printers' UNION ALL
    SELECT 'network_print_queue' UNION ALL
    SELECT 'printer_settings' UNION ALL
    SELECT 'cash_drawer_events' UNION ALL
    SELECT 'till_expenses' UNION ALL
    SELECT 'Floors' UNION ALL
    SELECT 'FloorBackgroundImages' UNION ALL
    SELECT 'RestaurantTables' UNION ALL
    SELECT 'TableSessions' UNION ALL
    SELECT 'SessionNotes' UNION ALL
    SELECT 'TableSessionEvents' UNION ALL
    SELECT 'terminal_health' UNION ALL
    SELECT 'terminal_pairings' UNION ALL
    SELECT 'terminal_events' UNION ALL
    SELECT 'offline_queue' UNION ALL
    SELECT 'pending_acks' UNION ALL
    SELECT 'heartbeat_log' UNION ALL
    SELECT 'order_received_log' UNION ALL
    SELECT 'orderweb_order_settlements' UNION ALL
    SELECT 'rider_dispatch_jobs' UNION ALL
    SELECT 'rider_dispatch_events' UNION ALL
    SELECT 'print_queue_cancellation_audit' UNION ALL
    SELECT 'order_kitchen_item_state' UNION ALL
    SELECT 'order_kitchen_revisions' UNION ALL
    SELECT 'order_kitchen_revision_items' UNION ALL
    SELECT 'online_order_print_tracking' UNION ALL
    SELECT 'cloud_reservations' UNION ALL
    SELECT 'reservation_sync_state' UNION ALL
    SELECT 'reservation_pending_acks' UNION ALL
    SELECT 'orderweb_daily_report_sync_log' UNION ALL
    SELECT 'local_order_deletion_audit' UNION ALL
    SELECT 'discount_events' UNION ALL
    SELECT 'z_report_print_log' UNION ALL
    SELECT 'ReportSnapshots' UNION ALL
    SELECT 'ReportVatBreakdown' UNION ALL
    SELECT 'ReportTopItems' UNION ALL
    SELECT 'ReportStaffPerformance' UNION ALL
    SELECT 'ReportDiscountAudit' UNION ALL
    SELECT 'ReportPaymentMethods' UNION ALL
    SELECT 'ReportOrderTypeAnalysis' UNION ALL
    SELECT 'delivery_zones' UNION ALL
    SELECT 'delivery_zone_postcodes' UNION ALL
    SELECT 'delivery_unassigned_postcodes' UNION ALL
    SELECT 'postcode_lookup_settings' UNION ALL
    SELECT 'sync_log' UNION ALL
    SELECT 'time_clock_sessions'
) required
LEFT JOIN information_schema.tables existing
    ON existing.table_schema = DATABASE()
   AND existing.table_name = required.table_name
WHERE existing.table_name IS NULL
ORDER BY required.table_name;

SELECT CONCAT(required.table_name, '.', required.column_name) AS missing_column
FROM (
    SELECT 'network_print_queue' AS table_name, 'cancelled_at' AS column_name UNION ALL
    SELECT 'network_print_queue', 'cancelled_by_user_id' UNION ALL
    SELECT 'network_print_queue', 'cancelled_by_name' UNION ALL
    SELECT 'network_print_queue', 'cancellation_reason' UNION ALL
    SELECT 'order_refunds', 'refunded_by_user_id' UNION ALL
    SELECT 'order_refunds', 'refunded_by_role' UNION ALL
    SELECT 'order_refunds', 'terminal_name' UNION ALL
    SELECT 'order_refunds', 'external_reference'
    UNION ALL SELECT 'users', 'is_archived'
    UNION ALL SELECT 'users', 'archived_at'
    UNION ALL SELECT 'users', 'archived_by_user_id'
    UNION ALL SELECT 'users', 'archived_original_username'
    UNION ALL SELECT 'table_service_charge_settings', 'is_enabled'
    UNION ALL SELECT 'table_service_charge_settings', 'percentage'
    UNION ALL SELECT 'table_service_charge_settings', 'classification'
    UNION ALL SELECT 'table_service_charge_settings', 'updated_by_user_id'
    UNION ALL SELECT 'table_service_charge_settings', 'updated_by_name'
    UNION ALL SELECT 'table_service_charge_settings', 'updated_at'
    UNION ALL SELECT 'order_service_availability_settings', 'table_enabled'
    UNION ALL SELECT 'order_service_availability_settings', 'collection_enabled'
    UNION ALL SELECT 'order_service_availability_settings', 'delivery_enabled'
    UNION ALL SELECT 'order_service_availability_settings', 'updated_by_user_id'
    UNION ALL SELECT 'order_service_availability_settings', 'updated_by_name'
    UNION ALL SELECT 'order_service_availability_settings', 'updated_at'
    UNION ALL SELECT 'orders', 'service_charge_percentage'
    UNION ALL SELECT 'orders', 'service_charge_basis'
    UNION ALL SELECT 'orders', 'service_charge_amount'
    UNION ALL SELECT 'orders', 'service_charge_status'
    UNION ALL SELECT 'orders', 'service_charge_classification'
    UNION ALL SELECT 'orders', 'service_charge_removal_reason'
    UNION ALL SELECT 'orders', 'service_charge_removed_by_user_id'
    UNION ALL SELECT 'orders', 'service_charge_removed_by_name'
    UNION ALL SELECT 'orders', 'service_charge_approved_by_user_id'
    UNION ALL SELECT 'orders', 'service_charge_approved_by_name'
    UNION ALL SELECT 'orders', 'service_charge_removed_at'
    UNION ALL SELECT 'orders', 'payment_status'
    UNION ALL SELECT 'orders', 'cash_tip_amount'
    UNION ALL SELECT 'orders', 'card_tip_amount'
    UNION ALL SELECT 'orders', 'promo_code'
    UNION ALL SELECT 'orders', 'gift_card_number_masked'
    UNION ALL SELECT 'orders', 'gift_card_amount_paid'
    UNION ALL SELECT 'orders', 'gift_card_remaining_balance'
    UNION ALL SELECT 'orders', 'loyalty_points_earned'
    UNION ALL SELECT 'orders', 'loyalty_points_redeemed'
    UNION ALL SELECT 'orders', 'loyalty_points_discount'
    UNION ALL SELECT 'orders', 'loyalty_balance_after'
    UNION ALL SELECT 'order_items', 'cloud_item_external_id'
    UNION ALL SELECT 'orders', 'amount_paid'
    UNION ALL SELECT 'orders', 'payment_provider'
    UNION ALL SELECT 'orders', 'payment_reference'
    UNION ALL SELECT 'orders', 'payment_currency'
    UNION ALL SELECT 'orders', 'voucher_code'
    UNION ALL SELECT 'network_printers', 'supports_two_color'
    UNION ALL SELECT 'order_items', 'print_in_red'
    UNION ALL SELECT 'order_items', 'course_type'
    UNION ALL SELECT 'order_items', 'fired_at'
    UNION ALL SELECT 'order_items', 'fired_by'
) required
LEFT JOIN information_schema.columns existing
    ON existing.table_schema = DATABASE()
   AND existing.table_name = required.table_name
   AND existing.column_name = required.column_name
WHERE existing.column_name IS NULL
ORDER BY required.table_name, required.column_name;

SELECT required.table_name AS missing_view
FROM (
    SELECT 'offline_queue_stats' AS table_name UNION ALL
    SELECT 'TableWithSessionInfo' UNION ALL
    SELECT 'vw_report_order_lines_live' UNION ALL
    SELECT 'vw_report_daily_kpis_live' UNION ALL
    SELECT 'vw_report_top_items_live' UNION ALL
    SELECT 'vw_report_orders_live'
) required
LEFT JOIN information_schema.views existing
    ON existing.table_schema = DATABASE()
   AND existing.table_name = required.table_name
WHERE existing.table_name IS NULL
ORDER BY required.table_name;
