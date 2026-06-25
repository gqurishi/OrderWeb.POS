-- Production cleanup: remove demo/test business data while keeping users, permissions,
-- terminal setup, cloud configuration, printer configuration, and core settings.
--
-- Run this against the active POS database before going live.

DELIMITER $$

DROP PROCEDURE IF EXISTS truncate_table_if_exists $$
CREATE PROCEDURE truncate_table_if_exists(IN p_table_name VARCHAR(128))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = DATABASE()
          AND table_name = p_table_name
    ) THEN
        SET @truncate_sql = CONCAT('TRUNCATE TABLE `', REPLACE(p_table_name, '`', '``'), '`');
        PREPARE truncate_stmt FROM @truncate_sql;
        EXECUTE truncate_stmt;
        DEALLOCATE PREPARE truncate_stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS delete_sample_setting_if_exists $$
CREATE PROCEDURE delete_sample_setting_if_exists(IN p_setting_name VARCHAR(128), IN p_sample_value VARCHAR(255))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = DATABASE()
          AND table_name = 'settings'
    ) THEN
        DELETE FROM settings
        WHERE setting_key = p_setting_name
          AND setting_value = p_sample_value;
    END IF;
END $$

DELIMITER ;

SET FOREIGN_KEY_CHECKS = 0;

-- Reports and report sync/print history
CALL truncate_table_if_exists('ReportDiscountAudit');
CALL truncate_table_if_exists('ReportOrderTypeAnalysis');
CALL truncate_table_if_exists('ReportPaymentMethods');
CALL truncate_table_if_exists('ReportStaffPerformance');
CALL truncate_table_if_exists('ReportTopItems');
CALL truncate_table_if_exists('ReportVatBreakdown');
CALL truncate_table_if_exists('ReportSnapshots');
CALL truncate_table_if_exists('z_report_print_log');
CALL truncate_table_if_exists('orderweb_daily_report_sync_log');

-- Orders, payments, lifecycle events, refunds, and print tracking
CALL truncate_table_if_exists('order_item_send_tracking');
CALL truncate_table_if_exists('order_item_addons');
CALL truncate_table_if_exists('order_items');
CALL truncate_table_if_exists('order_events');
CALL truncate_table_if_exists('order_payments');
CALL truncate_table_if_exists('order_refunds');
CALL truncate_table_if_exists('orders');
CALL truncate_table_if_exists('OrderItemModifiers');
CALL truncate_table_if_exists('OrderItems');
CALL truncate_table_if_exists('CustomerOrders');
CALL truncate_table_if_exists('online_order_items');
CALL truncate_table_if_exists('online_orders');
CALL truncate_table_if_exists('order_received_log');
CALL truncate_table_if_exists('offline_queue');
CALL truncate_table_if_exists('sync_log');

-- Menu, quick notes, components, and old migrated menu/inventory tables
CALL truncate_table_if_exists('MenuItemQuickNotes');
CALL truncate_table_if_exists('MenuItemComponents');
CALL truncate_table_if_exists('MenuItemModifiers');
CALL truncate_table_if_exists('MenuItems');
CALL truncate_table_if_exists('MenuCategories');
CALL truncate_table_if_exists('ItemModifiers');
CALL truncate_table_if_exists('ItemComments');
CALL truncate_table_if_exists('ItemComponents');
CALL truncate_table_if_exists('FoodMenuItems');
CALL truncate_table_if_exists('FoodMenuCategories');
CALL truncate_table_if_exists('PredefinedNotes');
CALL truncate_table_if_exists('PredefinedComments');
CALL truncate_table_if_exists('MealDeals');
CALL truncate_table_if_exists('menu_items');
CALL truncate_table_if_exists('inventory');

-- Customers, loyalty, gift-card test/business records
CALL truncate_table_if_exists('collection_customers');
CALL truncate_table_if_exists('delivery_customers');
CALL truncate_table_if_exists('loyalty_customers');
CALL truncate_table_if_exists('gift_cards');
CALL truncate_table_if_exists('gift_card_transactions');

-- Table sessions, reservations, restaurant layout sample rows
CALL truncate_table_if_exists('TableSessionEvents');
CALL truncate_table_if_exists('SessionNotes');
CALL truncate_table_if_exists('TableSessions');
CALL truncate_table_if_exists('Reservations');
CALL truncate_table_if_exists('RestaurantTables');
CALL truncate_table_if_exists('Floors');

-- Cash drawer, till, terminal/event telemetry generated during testing
CALL truncate_table_if_exists('cash_drawer_audits');
CALL truncate_table_if_exists('cash_drawer_events');
CALL truncate_table_if_exists('till_expenses');
CALL truncate_table_if_exists('discount_audits');
CALL truncate_table_if_exists('discount_events');
CALL truncate_table_if_exists('terminal_events');
CALL truncate_table_if_exists('terminal_health');

SET FOREIGN_KEY_CHECKS = 1;

CALL delete_sample_setting_if_exists('restaurant_name', 'My Restaurant');

DROP PROCEDURE IF EXISTS truncate_table_if_exists;
DROP PROCEDURE IF EXISTS delete_sample_setting_if_exists;
