-- OrderWeb POS production migration 008
-- Saved report snapshots, discount audit, Z report print audit, and live reporting views.

CREATE TABLE IF NOT EXISTS discount_events (
    id INT PRIMARY KEY AUTO_INCREMENT,
    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    order_id VARCHAR(100) NULL,
    order_number VARCHAR(50) NULL,
    table_session_id INT NULL,
    table_number VARCHAR(50) NULL,
    event_action VARCHAR(20) NOT NULL,
    discount_type VARCHAR(20) NOT NULL,
    subtotal_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    previous_discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    discount_percent DECIMAL(6,2) NOT NULL DEFAULT 0.00,
    total_after_discount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    reason VARCHAR(255) NULL,
    source_area VARCHAR(80) NULL,
    requested_by_user_id INT NULL,
    requested_by_name VARCHAR(150) NULL,
    requested_by_role VARCHAR(20) NULL,
    approved_by_user_id INT NULL,
    approved_by_name VARCHAR(150) NULL,
    approved_by_role VARCHAR(20) NULL,
    approval_required TINYINT(1) NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_discount_event_at (event_at),
    INDEX idx_discount_order_time (order_id, event_at),
    INDEX idx_discount_user_time (requested_by_user_id, event_at),
    INDEX idx_discount_action_time (event_action, event_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS z_report_print_log (
    id INT PRIMARY KEY AUTO_INCREMENT,
    report_date DATE NOT NULL,
    printed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    printed_by_user_id INT NULL,
    printed_by_name VARCHAR(150) NULL,
    printer_name VARCHAR(120) NULL,
    terminal_name VARCHAR(120) NULL,
    is_reprint TINYINT(1) NOT NULL DEFAULT 0,
    include_detail TINYINT(1) NOT NULL DEFAULT 0,
    order_count INT NOT NULL DEFAULT 0,
    gross_sales DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    success TINYINT(1) NOT NULL DEFAULT 1,
    error_message VARCHAR(500) NULL,
    report_reference VARCHAR(40) NULL,
    INDEX idx_z_report_print_date (report_date, printed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportSnapshots (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    ReportType VARCHAR(20) NOT NULL,
    StartDate DATETIME NOT NULL,
    EndDate DATETIME NOT NULL,
    GeneratedAt DATETIME NOT NULL,
    OrderCount INT NOT NULL DEFAULT 0,
    GrossSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    NetSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VatTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    DineInOrders INT NOT NULL DEFAULT 0,
    DeliveryOrders INT NOT NULL DEFAULT 0,
    PickupOrders INT NOT NULL DEFAULT 0,
    OnlineOrders INT NOT NULL DEFAULT 0,
    CashTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    CardTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    MobilePayTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    DiscountTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    DiscountCount INT NOT NULL DEFAULT 0,
    VoidTotal DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VoidCount INT NOT NULL DEFAULT 0,
    CancelledOrderCount INT NOT NULL DEFAULT 0,
    EstimatedCogs DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    EstimatedMargin DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    MarginPercent DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    AverageOrderValue DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    AveragePrepTime INT NOT NULL DEFAULT 0,
    PaymentSuccessRate DECIMAL(8,2) NOT NULL DEFAULT 100.00,
    CreatedByStaffId INT NULL,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    IsArchived TINYINT(1) NOT NULL DEFAULT 0,
    INDEX idx_report_snapshots_period (ReportType, StartDate, EndDate),
    INDEX idx_report_snapshots_generated (GeneratedAt),
    INDEX idx_report_snapshots_archived (IsArchived)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportVatBreakdown (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    VatRate DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    TaxableAmount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VatAmount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    ItemCount INT NOT NULL DEFAULT 0,
    VatCategoryName VARCHAR(120) NOT NULL DEFAULT '',
    CONSTRAINT fk_report_vat_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_vat_snapshot (SnapshotId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportTopItems (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    ItemId INT NULL,
    CategoryId INT NULL,
    ItemName VARCHAR(255) NOT NULL,
    CategoryName VARCHAR(255) NOT NULL DEFAULT '',
    TotalQuantity INT NOT NULL DEFAULT 0,
    GrossSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    NetSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VatAmount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VatRate DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    EstimatedCogs DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    Margin DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    MarginPercent DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    CONSTRAINT fk_report_top_items_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_top_items_snapshot (SnapshotId),
    INDEX idx_report_top_items_name (ItemName)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportStaffPerformance (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    StaffId INT NULL,
    StaffName VARCHAR(150) NOT NULL DEFAULT '',
    OrdersProcessed INT NOT NULL DEFAULT 0,
    TotalSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    DiscountsApplied DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    VoidsInitiated INT NOT NULL DEFAULT 0,
    AverageOrderValue DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    ErrorRate DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    CONSTRAINT fk_report_staff_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_staff_snapshot (SnapshotId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportDiscountAudit (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    DiscountReason VARCHAR(255) NOT NULL DEFAULT '',
    DiscountCount INT NOT NULL DEFAULT 0,
    TotalDiscountAmount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    AverageDiscountPercent DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    CONSTRAINT fk_report_discount_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_discount_snapshot (SnapshotId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportPaymentMethods (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    PaymentMethod VARCHAR(80) NOT NULL,
    TransactionCount INT NOT NULL DEFAULT 0,
    TotalAmount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    SuccessRate DECIMAL(8,2) NOT NULL DEFAULT 100.00,
    FailureCount INT NOT NULL DEFAULT 0,
    ProcessingFee DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    CONSTRAINT fk_report_payment_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_payment_snapshot (SnapshotId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ReportOrderTypeAnalysis (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SnapshotId INT NOT NULL,
    OrderType VARCHAR(40) NOT NULL,
    OrderCount INT NOT NULL DEFAULT 0,
    TotalSales DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    AverageOrderValue DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    AveragePrepTime INT NOT NULL DEFAULT 0,
    CancellationRate DECIMAL(8,2) NOT NULL DEFAULT 0.00,
    CONSTRAINT fk_report_order_type_snapshot
        FOREIGN KEY (SnapshotId) REFERENCES ReportSnapshots(Id)
        ON DELETE CASCADE,
    INDEX idx_report_order_type_snapshot (SnapshotId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

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
