-- ========================================
-- Reporting V1 Live Views Migration
-- Target DB: Pos-net (MariaDB)
-- Purpose:
-- 1) Ensure report-friendly indexes on live tables
-- 2) Build real-time reporting views from orders/order_items
-- 3) Support dashboard KPIs, VAT, source tabs, search, top sellers, CSV/PDF
-- ========================================

USE `Pos-net`;

-- ========================================
-- A) INDEXES FOR REPORT PERFORMANCE
-- ========================================

-- Orders filters: date range + source + order type + status
ALTER TABLE orders
ADD INDEX IF NOT EXISTS idx_orders_report_date_source_type_status (created_at, source_channel, order_type, status);

-- Frequently used lookups for order identity/search
ALTER TABLE orders
ADD INDEX IF NOT EXISTS idx_orders_report_search (order_number, customer_name, customer_phone);

-- Item aggregation lookups
ALTER TABLE order_items
ADD INDEX IF NOT EXISTS idx_order_items_report_order_menu (order_id, menu_item_id);

-- Addon aggregation lookups
ALTER TABLE order_item_addons
ADD INDEX IF NOT EXISTS idx_order_item_addons_report_item (order_item_id);


-- ========================================
-- B) CORE LIVE LINE VIEW (ALL CALCULATIONS)
-- ========================================
-- Notes:
-- - Uses proportional VAT allocation from orders.tax_amount across each line gross
--   so reporting works even when per-line VAT is not explicitly stored.
-- - If tax_amount is 0/NULL then VAT is 0 for all lines.
-- - Excludes cancelled orders by default.

CREATE OR REPLACE VIEW vw_report_order_lines_live AS
SELECT
    o.id AS order_db_id,
    o.order_id,
    o.order_number,
    o.cloud_order_id,
    o.created_at,
    DATE(o.created_at) AS business_date,
    o.source_channel,
    o.order_type,
    o.status,
    o.customer_name,
    o.customer_phone,

    oi.id AS order_item_id,
    oi.menu_item_id,
    oi.item_name,
    oi.quantity,
    COALESCE(oi.item_price, 0.00) AS unit_price,

    COALESCE(addons.addon_unit_total, 0.00) AS addon_unit_total,
    (COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) AS line_unit_gross,
    (COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0) AS line_gross,

    COALESCE(o.total_amount, 0.00) AS order_gross_total,
    COALESCE(o.tax_amount, 0.00) AS order_tax_total,

    CASE
        WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
            THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
                 / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00), 4)
        ELSE 0.0000
    END AS line_vat,

    CASE
        WHEN COALESCE(o.total_amount, 0.00) > 0 AND COALESCE(o.tax_amount, 0.00) > 0
            THEN ROUND(((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
                 - (((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0))
                 / COALESCE(o.total_amount, 1.00) * COALESCE(o.tax_amount, 0.00)), 4)
        ELSE ROUND((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0), 4)
    END AS line_net

FROM orders o
INNER JOIN order_items oi ON oi.order_id = o.id
LEFT JOIN (
    SELECT
        order_item_id,
        SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
    FROM order_item_addons
    GROUP BY order_item_id
) addons ON addons.order_item_id = oi.id
WHERE LOWER(COALESCE(o.status, '')) <> 'cancelled';


-- ========================================
-- C) DAILY KPI VIEW (FOR DASHBOARD CARDS)
-- ========================================

CREATE OR REPLACE VIEW vw_report_daily_kpis_live AS
SELECT
    business_date,
    source_channel,
    order_type,
    COUNT(DISTINCT order_db_id) AS order_count,
    ROUND(SUM(line_gross), 2) AS gross_sales,
    ROUND(SUM(line_net), 2) AS net_sales,
    ROUND(SUM(line_vat), 2) AS vat_amount,
    ROUND(CASE WHEN COUNT(DISTINCT order_db_id) > 0
        THEN SUM(line_gross) / COUNT(DISTINCT order_db_id)
        ELSE 0 END, 2) AS average_order_value
FROM vw_report_order_lines_live
GROUP BY business_date, source_channel, order_type;


-- ========================================
-- D) TOP SELLERS VIEW (FOR TOP 20 TABLE)
-- ========================================

CREATE OR REPLACE VIEW vw_report_top_items_live AS
SELECT
    business_date,
    source_channel,
    order_type,
    menu_item_id,
    item_name,
    SUM(quantity) AS total_quantity,
    ROUND(SUM(line_gross), 2) AS gross_sales,
    ROUND(SUM(line_net), 2) AS net_sales,
    ROUND(SUM(line_vat), 2) AS vat_amount
FROM vw_report_order_lines_live
GROUP BY business_date, source_channel, order_type, menu_item_id, item_name;


-- ========================================
-- E) ORDER HEADER VIEW (FOR SEARCH + EXPORT)
-- ========================================

CREATE OR REPLACE VIEW vw_report_orders_live AS
SELECT
    o.id AS order_db_id,
    o.order_id,
    o.order_number,
    o.cloud_order_id,
    o.created_at,
    DATE(o.created_at) AS business_date,
    o.source_channel,
    o.order_type,
    o.status,
    o.customer_name,
    o.customer_phone,
    COALESCE(o.subtotal_amount, 0.00) AS subtotal_amount,
    COALESCE(o.tax_amount, 0.00) AS tax_amount,
    COALESCE(o.delivery_fee, 0.00) AS delivery_fee,
    COALESCE(o.total_amount, 0.00) AS total_amount,
    COALESCE(o.payment_method, '') AS payment_method
FROM orders o
WHERE LOWER(COALESCE(o.status, '')) <> 'cancelled';


-- ========================================
-- F) VALIDATION QUERIES (MANUAL RUN)
-- ========================================
-- SELECT * FROM vw_report_daily_kpis_live ORDER BY business_date DESC LIMIT 20;
-- SELECT * FROM vw_report_top_items_live ORDER BY business_date DESC, total_quantity DESC LIMIT 20;
-- SELECT * FROM vw_report_orders_live ORDER BY created_at DESC LIMIT 20;
-- SELECT * FROM vw_report_order_lines_live ORDER BY created_at DESC LIMIT 20;
