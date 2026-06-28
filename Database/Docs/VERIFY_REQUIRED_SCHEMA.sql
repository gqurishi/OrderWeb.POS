-- OrderWeb POS production schema verification.
-- Run after 001-010 migrations on the Mother database.
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
    SELECT 'MenuCategories' UNION ALL
    SELECT 'MenuItems' UNION ALL
    SELECT 'orders' UNION ALL
    SELECT 'order_items' UNION ALL
    SELECT 'order_item_addons' UNION ALL
    SELECT 'order_payments' UNION ALL
    SELECT 'order_events' UNION ALL
    SELECT 'order_item_send_tracking' UNION ALL
    SELECT 'order_refunds' UNION ALL
    SELECT 'collection_customers' UNION ALL
    SELECT 'delivery_customers' UNION ALL
    SELECT 'order_number_settings' UNION ALL
    SELECT 'print_groups' UNION ALL
    SELECT 'network_printers' UNION ALL
    SELECT 'network_print_queue' UNION ALL
    SELECT 'printer_settings' UNION ALL
    SELECT 'cash_drawer_events' UNION ALL
    SELECT 'till_expenses' UNION ALL
    SELECT 'Floors' UNION ALL
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
    SELECT 'online_order_print_tracking' UNION ALL
    SELECT 'cloud_reservations' UNION ALL
    SELECT 'reservation_sync_state' UNION ALL
    SELECT 'orderweb_daily_report_sync_log' UNION ALL
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
