using MySqlConnector;
using System.Data;
using System.Linq;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Database service using MariaDB/MySQL ONLY
/// This application uses MariaDB as the primary database
/// Host is selected by terminal setup. Mother uses localhost; child terminals use the mother terminal IP.
/// </summary>
public class DatabaseService
{
    public DatabaseService()
    {
        // DO NOT initialize database in constructor - it blocks app startup!
        // Initialize will be called separately when needed
        // _ = InitializeDatabaseAsync(); // REMOVED - was blocking!
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            
            // Add 5-second timeout
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.OpenAsync(cts.Token);
            
            // Test with a simple query
            using var command = new MySqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync();
            
            return result != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task<string> GetConnectionStatusAsync()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            await connection.OpenAsync();
            
            using var command = new MySqlCommand("SELECT VERSION()", connection);
            var version = await command.ExecuteScalarAsync();
            
            return $"Connected to MariaDB/MySQL version: {version}";
        }
        catch (Exception ex)
        {
            return $"Connection failed: {ex.Message}";
        }
    }

    public async Task<MySqlConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public async Task<bool> InitializeDatabaseAsync()
    {
        try
        {
            // Create database if not exists
            await CreateDatabaseIfNotExistsAsync();
            
            // Create tables
            return await CreateTablesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database initialization failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateDatabaseIfNotExistsAsync()
    {
        try
        {
            // Connect without specifying database to create it if needed
            var connectionStringWithoutDb = TerminalConfigurationService.GetPosConnectionString(includeDatabase: false, pooled: false);
            
            using var connection = new MySqlConnection(connectionStringWithoutDb);
            await connection.OpenAsync();
            
            var databaseName = TerminalConfigurationService.GetConfiguration().DatabaseName;
            using var command = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", connection);
            await command.ExecuteNonQueryAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database creation failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateTablesAsync()
    {
        try
        {
            using var connection = new MySqlConnection(GetConnectionString());
            await connection.OpenAsync();

            // Users table
            var createUsersTable = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    username VARCHAR(50) UNIQUE NOT NULL,
                    password_hash VARCHAR(255) NOT NULL,
                    role ENUM('admin', 'manager', 'user') DEFAULT 'user',
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB";

            // Orders table
            var createOrdersTable = @"
                CREATE TABLE IF NOT EXISTS orders (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id VARCHAR(100) UNIQUE NOT NULL,
                    order_number VARCHAR(50) NULL,
                    cloud_order_id VARCHAR(100) NULL,
                    customer_name VARCHAR(100),
                    customer_phone VARCHAR(20),
                    customer_email VARCHAR(255) NULL,
                    customer_address TEXT,
                    total_amount DECIMAL(10,2),
                    subtotal_amount DECIMAL(10,2) NULL,
                    discount_amount DECIMAL(10,2) DEFAULT 0,
                    delivery_fee DECIMAL(10,2) DEFAULT 0,
                    tax_amount DECIMAL(10,2) DEFAULT 0,
                    order_type ENUM('pickup', 'delivery', 'table') DEFAULT 'pickup',
                    source_channel ENUM('local', 'web') DEFAULT 'local',
                    table_session_id INT NULL,
                    payment_method VARCHAR(50) NULL,
                    special_instructions TEXT NULL,
                    scheduled_time TIMESTAMP NULL,
                    status ENUM('new', 'kitchen', 'preparing', 'ready', 'delivering', 'completed', 'cancelled') DEFAULT 'new',
                    local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
                    is_open BOOLEAN DEFAULT TRUE,
                    void_reason VARCHAR(255) NULL,
                    voided_at DATETIME NULL,
                    voided_by VARCHAR(100) NULL,
                    paid_at DATETIME NULL,
                    order_data JSON,
                    sync_status ENUM('synced', 'pending', 'failed') DEFAULT 'pending',
                    kitchen_time TIMESTAMP NULL,
                    preparing_time TIMESTAMP NULL,
                    ready_time TIMESTAMP NULL,
                    delivering_time TIMESTAMP NULL,
                    completed_time TIMESTAMP NULL,
                    updated_by_terminal_name VARCHAR(120) NULL,
                    updated_by_terminal_at DATETIME NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_orders_table_session_open (table_session_id, is_open)
                ) ENGINE=InnoDB";

            var createOrderPaymentsTable = @"
                CREATE TABLE IF NOT EXISTS order_payments (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    attempt_no INT NOT NULL DEFAULT 1,
                    payment_method ENUM('cash', 'card', 'gift_card', 'refund', 'tip_adjust') NOT NULL,
                    amount DECIMAL(10,2) NOT NULL,
                    currency_code CHAR(3) NOT NULL DEFAULT 'GBP',
                    status ENUM('attempted', 'approved', 'failed', 'voided') NOT NULL DEFAULT 'attempted',
                    reference VARCHAR(100) NULL,
                    tip_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
                    metadata_json JSON NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_by VARCHAR(100) NULL,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    INDEX idx_order_payments_order_created (order_id, created_at),
                    INDEX idx_order_payments_order_status (order_id, status),
                    INDEX idx_order_payments_method_created (payment_method, created_at)
                ) ENGINE=InnoDB";

            var createDeliveryZonesTable = @"
                CREATE TABLE IF NOT EXISTS delivery_zones (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(120) NOT NULL,
                    delivery_fee DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            var createDeliveryZonePostcodesTable = @"
                CREATE TABLE IF NOT EXISTS delivery_zone_postcodes (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    zone_id INT NOT NULL,
                    postcode VARCHAR(16) NOT NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY ux_delivery_zone_postcode (postcode),
                    INDEX idx_delivery_zone_postcodes_zone (zone_id),
                    CONSTRAINT fk_delivery_zone_postcodes_zone
                        FOREIGN KEY (zone_id) REFERENCES delivery_zones(id)
                        ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            var createDeliveryUnassignedPostcodesTable = @"
                CREATE TABLE IF NOT EXISTS delivery_unassigned_postcodes (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    postcode VARCHAR(16) NOT NULL,
                    request_count INT NOT NULL DEFAULT 1,
                    first_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    last_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY ux_delivery_unassigned_postcode (postcode)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            var createOrderEventsTable = @"
                CREATE TABLE IF NOT EXISTS order_events (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    event_type ENUM(
                        'created','line_added','line_removed','line_updated',
                        'sent','resend','send_failed',
                        'payment_attempt','payment_approved','payment_failed',
                        'split','void_requested','voided','reopened',
                        'table_transferred','table_merged',
                        'state_changed'
                    ) NOT NULL,
                    actor_type ENUM('user', 'system', 'manager') NOT NULL DEFAULT 'system',
                    actor_id VARCHAR(100) NULL,
                    actor_name VARCHAR(150) NULL,
                    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    payload_json JSON NULL,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    INDEX idx_order_events_order_time (order_id, event_at),
                    INDEX idx_order_events_type_time (event_type, event_at)
                ) ENGINE=InnoDB";

            var createOrderItemSendTrackingTable = @"
                CREATE TABLE IF NOT EXISTS order_item_send_tracking (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    order_item_id INT NOT NULL,
                    send_batch_id VARCHAR(36) NOT NULL,
                    station_type ENUM('kitchen', 'bar', 'receipt', 'other') NOT NULL DEFAULT 'kitchen',
                    print_group_id VARCHAR(36) NULL,
                    route_target VARCHAR(120) NULL,
                    send_status ENUM('queued', 'sent', 'printed', 'failed', 'retrying') NOT NULL DEFAULT 'queued',
                    sent_at DATETIME NULL,
                    printed_at DATETIME NULL,
                    failure_reason VARCHAR(255) NULL,
                    attempt_count INT NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    FOREIGN KEY (order_item_id) REFERENCES order_items(id) ON DELETE CASCADE,
                    INDEX idx_order_send_item_created (order_item_id, created_at),
                    INDEX idx_order_send_batch (send_batch_id),
                    INDEX idx_order_send_status_updated (send_status, updated_at)
                ) ENGINE=InnoDB";

            // Order items table
            var createOrderItemsTable = @"
                CREATE TABLE IF NOT EXISTS order_items (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    cloud_item_id INT NULL,
                    menu_item_id VARCHAR(100) NULL,
                    order_id INT,
                    item_name VARCHAR(100) NOT NULL,
                    quantity INT NOT NULL,
                    item_price DECIMAL(10,2) NULL,
                    special_instructions TEXT,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE
                ) ENGINE=InnoDB";

            // Settings table
            var createSettingsTable = @"
                CREATE TABLE IF NOT EXISTS settings (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    setting_key VARCHAR(100) UNIQUE NOT NULL,
                    setting_value TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB";

            // Create cloud configuration table
            var createCloudConfigTable = @"
                CREATE TABLE IF NOT EXISTS cloud_config (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    api_base_url VARCHAR(500) NOT NULL DEFAULT 'https://orderweb.net/api',
                    tenant_slug VARCHAR(255) NOT NULL DEFAULT '',
                    api_key VARCHAR(500) NOT NULL DEFAULT '',
                    websocket_url VARCHAR(500) NOT NULL DEFAULT '',
                    connection_timeout INT DEFAULT 30,
                    polling_interval_seconds INT DEFAULT 30,
                    max_retry_attempts INT DEFAULT 3,
                    is_enabled BOOLEAN DEFAULT FALSE,
                    is_api_tested BOOLEAN DEFAULT FALSE,
                    is_websocket_tested BOOLEAN DEFAULT FALSE,
                    api_test_result TEXT,
                    websocket_test_result TEXT,
                    last_api_test TIMESTAMP NULL,
                    last_websocket_test TIMESTAMP NULL,
                    auto_print_enabled BOOLEAN DEFAULT TRUE,
                    notifications_enabled BOOLEAN DEFAULT TRUE,
                    online_order_master_enabled BOOLEAN DEFAULT TRUE,
                    online_order_master_terminal_name VARCHAR(120) DEFAULT '',
                    last_sync TIMESTAMP NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    cloud_url VARCHAR(500) DEFAULT 'https://orderweb.net/api/pos'
                ) ENGINE=InnoDB";

            // Permission catalog table
            var createPermissionsTable = @"
                CREATE TABLE IF NOT EXISTS permissions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    permission_key VARCHAR(120) NOT NULL UNIQUE,
                    display_name VARCHAR(150) NOT NULL,
                    category VARCHAR(80) NOT NULL,
                    description VARCHAR(255) NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                ) ENGINE=InnoDB";

            // Role-level default permissions
            var createRolePermissionsTable = @"
                CREATE TABLE IF NOT EXISTS role_permissions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    role VARCHAR(20) NOT NULL,
                    permission_key VARCHAR(120) NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY uq_role_permission (role, permission_key)
                ) ENGINE=InnoDB";

            // User-level overrides (allow/deny) with higher priority than role defaults
            var createUserPermissionsTable = @"
                CREATE TABLE IF NOT EXISTS user_permissions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    permission_key VARCHAR(120) NOT NULL,
                    is_allowed BOOLEAN NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    UNIQUE KEY uq_user_permission (user_id, permission_key),
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                ) ENGINE=InnoDB";

            // Execute table creation commands
            using var command1 = new MySqlCommand(createUsersTable, connection);
            await command1.ExecuteNonQueryAsync();

            using var command2 = new MySqlCommand(createOrdersTable, connection);
            await command2.ExecuteNonQueryAsync();

            using var command3 = new MySqlCommand(createOrderItemsTable, connection);
            await command3.ExecuteNonQueryAsync();

            using var command4 = new MySqlCommand(createSettingsTable, connection);
            await command4.ExecuteNonQueryAsync();

            using var command5 = new MySqlCommand(createCloudConfigTable, connection);
            await command5.ExecuteNonQueryAsync();

            using var command6 = new MySqlCommand(createPermissionsTable, connection);
            await command6.ExecuteNonQueryAsync();

            using var command7 = new MySqlCommand(createRolePermissionsTable, connection);
            await command7.ExecuteNonQueryAsync();

            using var command8 = new MySqlCommand(createUserPermissionsTable, connection);
            await command8.ExecuteNonQueryAsync();

            using var command9 = new MySqlCommand(createOrderPaymentsTable, connection);
            await command9.ExecuteNonQueryAsync();
            
            using var command10 = new MySqlCommand(createOrderEventsTable, connection);
            await command10.ExecuteNonQueryAsync();
            
            using var deliveryZonesCommand = new MySqlCommand(createDeliveryZonesTable, connection);
            await deliveryZonesCommand.ExecuteNonQueryAsync();
            
            using var deliveryPostcodesCommand = new MySqlCommand(createDeliveryZonePostcodesTable, connection);
            await deliveryPostcodesCommand.ExecuteNonQueryAsync();
            
            using var unassignedPostcodesCommand = new MySqlCommand(createDeliveryUnassignedPostcodesTable, connection);
            await unassignedPostcodesCommand.ExecuteNonQueryAsync();

            try
            {
                using var alterOrderEventsCommand = new MySqlCommand(@"
                    ALTER TABLE order_events
                    MODIFY COLUMN event_type ENUM(
                        'created','line_added','line_removed','line_updated',
                        'sent','resend','send_failed',
                        'split','payment_attempt','payment_approved','payment_failed',
                        'void_requested','voided','reopened',
                        'table_transferred','table_merged',
                        'state_changed'
                    ) NOT NULL", connection);
                await alterOrderEventsCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order events enum update warning: {ex.Message}");
            }

            using var command11 = new MySqlCommand(createOrderItemSendTrackingTable, connection);
            await command11.ExecuteNonQueryAsync();

            try
            {
                using var dropFloorUniqueIndexCommand = new MySqlCommand("ALTER TABLE Floors DROP INDEX Name", connection);
                await dropFloorUniqueIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor name unique index compatibility warning: {ex.Message}");
            }

            try
            {
                using var createFloorNameIndexCommand = new MySqlCommand("CREATE INDEX IF NOT EXISTS idx_name ON Floors (Name)", connection);
                await createFloorNameIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor name index compatibility warning: {ex.Message}");
            }

            var alterOrdersLifecycleSchema = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS order_number VARCHAR(50) NULL,
                ADD COLUMN IF NOT EXISTS cloud_order_id VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS customer_email VARCHAR(255) NULL,
                ADD COLUMN IF NOT EXISTS subtotal_amount DECIMAL(10,2) NULL,
                ADD COLUMN IF NOT EXISTS discount_amount DECIMAL(10,2) DEFAULT 0,
                ADD COLUMN IF NOT EXISTS delivery_fee DECIMAL(10,2) DEFAULT 0,
                ADD COLUMN IF NOT EXISTS tax_amount DECIMAL(10,2) DEFAULT 0,
                ADD COLUMN IF NOT EXISTS order_type ENUM('pickup', 'delivery', 'table') DEFAULT 'pickup',
                ADD COLUMN IF NOT EXISTS source_channel ENUM('local', 'web') DEFAULT 'local',
                ADD COLUMN IF NOT EXISTS table_session_id INT NULL,
                ADD COLUMN IF NOT EXISTS payment_method VARCHAR(50) NULL,
                ADD COLUMN IF NOT EXISTS special_instructions TEXT NULL,
                ADD COLUMN IF NOT EXISTS scheduled_time TIMESTAMP NULL,
                ADD COLUMN IF NOT EXISTS local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
                ADD COLUMN IF NOT EXISTS is_open BOOLEAN DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS void_reason VARCHAR(255) NULL,
                ADD COLUMN IF NOT EXISTS voided_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS voided_by VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS paid_at DATETIME NULL";

            using (var alterLifecycleCommand = new MySqlCommand(alterOrdersLifecycleSchema, connection))
            {
                await alterLifecycleCommand.ExecuteNonQueryAsync();
            }

            try
            {
                using var addOrdersSessionIndexCommand = new MySqlCommand(
                    "CREATE INDEX IF NOT EXISTS idx_orders_table_session_open ON orders (table_session_id, is_open)",
                    connection);
                await addOrdersSessionIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order table session index warning: {ex.Message}");
            }

            var alterOrdersStatusEnum = @"
                ALTER TABLE orders
                MODIFY COLUMN status ENUM('new', 'kitchen', 'preparing', 'ready', 'delivering', 'completed', 'cancelled') DEFAULT 'new'";

            try
            {
                using var alterStatusCommand = new MySqlCommand(alterOrdersStatusEnum, connection);
                await alterStatusCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order status enum update warning: {ex.Message}");
            }

            var backfillLifecycleQuery = @"
                UPDATE orders
                SET local_lifecycle_state = CASE
                        WHEN LOWER(status) = 'completed' THEN 'paid'
                        WHEN LOWER(status) = 'cancelled' THEN 'voided'
                        ELSE 'active'
                    END,
                    is_open = CASE
                        WHEN LOWER(status) IN ('completed', 'cancelled') THEN 0
                        ELSE 1
                    END,
                    paid_at = CASE
                        WHEN LOWER(status) = 'completed' THEN COALESCE(paid_at, completed_time)
                        ELSE paid_at
                    END,
                    voided_at = CASE
                        WHEN LOWER(status) = 'cancelled' THEN COALESCE(voided_at, updated_at)
                        ELSE voided_at
                    END
                WHERE local_lifecycle_state IS NULL OR TRIM(local_lifecycle_state) = ''";

            using (var backfillLifecycleCommand = new MySqlCommand(backfillLifecycleQuery, connection))
            {
                await backfillLifecycleCommand.ExecuteNonQueryAsync();
            }

            var alterOrderItemsSchema = @"
                ALTER TABLE order_items
                ADD COLUMN IF NOT EXISTS cloud_item_id INT NULL,
                ADD COLUMN IF NOT EXISTS menu_item_id VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS item_price DECIMAL(10,2) NULL";

            using (var alterOrderItemsCommand = new MySqlCommand(alterOrderItemsSchema, connection))
            {
                await alterOrderItemsCommand.ExecuteNonQueryAsync();
            }

            var migrateUnitPriceQuery = @"
                UPDATE order_items
                SET item_price = COALESCE(item_price, unit_price)
                WHERE item_price IS NULL";

            try
            {
                using var migrateUnitPriceCommand = new MySqlCommand(migrateUnitPriceQuery, connection);
                await migrateUnitPriceCommand.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore if unit_price doesn't exist in this schema version.
            }

            var alterOrdersLiveUpdateTable = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS updated_by_terminal_name VARCHAR(120) NULL,
                ADD COLUMN IF NOT EXISTS updated_by_terminal_at DATETIME NULL";

            try
            {
                using var alterOrdersLiveUpdateCommand = new MySqlCommand(alterOrdersLiveUpdateTable, connection);
                await alterOrdersLiveUpdateCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Orders live update columns warning: {ex.Message}");
            }

            // Add new columns for direct database connection and OrderWeb.net integration if they don't exist
            var alterCloudConfigTable = @"
                ALTER TABLE cloud_config 
                ADD COLUMN IF NOT EXISTS db_host VARCHAR(255) DEFAULT '',
                ADD COLUMN IF NOT EXISTS db_name VARCHAR(255) DEFAULT '',
                ADD COLUMN IF NOT EXISTS db_username VARCHAR(255) DEFAULT '',
                ADD COLUMN IF NOT EXISTS db_password VARCHAR(500) DEFAULT '',
                ADD COLUMN IF NOT EXISTS db_port INT DEFAULT 3306,
                ADD COLUMN IF NOT EXISTS connection_type VARCHAR(50) DEFAULT 'api_polling',
                ADD COLUMN IF NOT EXISTS connection_string TEXT DEFAULT '',
                ADD COLUMN IF NOT EXISTS orderweb_enabled BOOLEAN DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS orderweb_connection_string TEXT DEFAULT '',
                ADD COLUMN IF NOT EXISTS restaurant_slug VARCHAR(255) DEFAULT '',
                ADD COLUMN IF NOT EXISTS direct_db_enabled BOOLEAN DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS online_order_master_enabled BOOLEAN DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS online_order_master_terminal_name VARCHAR(120) DEFAULT ''";

            try 
            {
                using var alterCommand = new MySqlCommand(alterCloudConfigTable, connection);
                await alterCommand.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1060) // Duplicate column name
            {
                // Columns already exist, ignore
                System.Diagnostics.Debug.WriteLine("Cloud config columns already exist");
            }

            // Add name column to users table if it doesn't exist
            var alterUsersTable = @"
                ALTER TABLE users 
                ADD COLUMN IF NOT EXISTS name VARCHAR(100) NOT NULL DEFAULT ''";

            try 
            {
                using var alterUsersCommand = new MySqlCommand(alterUsersTable, connection);
                await alterUsersCommand.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1060) // Duplicate column name
            {
                // Column already exists, ignore
                System.Diagnostics.Debug.WriteLine("Users name column already exists");
            }

            // Create default admin user if not exists
            await CreateDefaultAdminUserAsync(connection);

            // Seed permission catalog and role defaults
            await SeedDefaultPermissionsAsync(connection);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Table creation failed: {ex.Message}");
            return false;
        }
    }

    private async Task CreateDefaultAdminUserAsync(MySqlConnection connection)
    {
        try
        {
            // Check if admin user exists
            using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM users WHERE username = 'admin'", connection);
            var userCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

            if (userCount == 0)
            {
                // Create default admin user (password: admin123)
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword("admin123");
                using var insertCommand = new MySqlCommand(
                    "INSERT INTO users (name, username, password_hash, role) VALUES (@name, @username, @password, @role)", 
                    connection);
                
                insertCommand.Parameters.AddWithValue("@name", "Administrator");
                insertCommand.Parameters.AddWithValue("@username", "admin");
                insertCommand.Parameters.AddWithValue("@password", hashedPassword);
                insertCommand.Parameters.AddWithValue("@role", "admin");
                
                await insertCommand.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Default admin user creation failed: {ex.Message}");
        }
    }

    private async Task SeedDefaultPermissionsAsync(MySqlConnection connection)
    {
        var permissions = new (string Key, string Name, string Category, string Description)[]
        {
            ("dashboard.admin.view", "View Admin Dashboard", "dashboard", "Access admin dashboard page"),
            ("weborders.view", "View Web Orders", "orders", "Access online/web orders module"),
            ("giftcards.manage", "Manage Gift Cards", "payments", "Check and redeem gift cards"),
            ("loyalty.manage", "Manage Loyalty", "payments", "Lookup and update loyalty points"),
            ("reservation.manage", "Manage Reservations", "restaurant", "Access reservation workflow"),
            ("report.view", "View Reports", "reports", "Access reports module"),
            ("inventory.view", "View Inventory", "reports", "Access inventory module"),
            ("settings.manage_business", "Manage Business Settings", "settings", "Edit business settings"),
            ("users.manage", "Manage Users", "settings", "Create, edit, and delete users"),
            ("ordernumber.manage", "Manage Order Prefix", "settings", "Edit order numbering settings"),
            ("printers.manage", "Manage Printers", "printing", "Configure printer setup and groups"),
            ("foodmenu.manage", "Manage Food Menu", "menu", "Create/edit menu items and categories")
        };

        foreach (var permission in permissions)
        {
            using var insertPermission = new MySqlCommand(@"
                INSERT INTO permissions (permission_key, display_name, category, description)
                VALUES (@key, @name, @category, @description)
                ON DUPLICATE KEY UPDATE
                    display_name = VALUES(display_name),
                    category = VALUES(category),
                    description = VALUES(description)", connection);

            insertPermission.Parameters.AddWithValue("@key", permission.Key);
            insertPermission.Parameters.AddWithValue("@name", permission.Name);
            insertPermission.Parameters.AddWithValue("@category", permission.Category);
            insertPermission.Parameters.AddWithValue("@description", permission.Description);
            await insertPermission.ExecuteNonQueryAsync();
        }

        var roleDefaults = new Dictionary<string, string[]>
        {
            ["user"] = Array.Empty<string>(),
            ["manager"] = new[]
            {
                "weborders.view",
                "giftcards.manage",
                "loyalty.manage",
                "reservation.manage"
            },
            ["admin"] = permissions.Select(p => p.Key).ToArray()
        };

        foreach (var role in roleDefaults)
        {
            foreach (var permissionKey in role.Value)
            {
                using var insertRolePermission = new MySqlCommand(@"
                    INSERT INTO role_permissions (role, permission_key)
                    VALUES (@role, @permission)
                    ON DUPLICATE KEY UPDATE permission_key = VALUES(permission_key)", connection);

                insertRolePermission.Parameters.AddWithValue("@role", role.Key);
                insertRolePermission.Parameters.AddWithValue("@permission", permissionKey);
                await insertRolePermission.ExecuteNonQueryAsync();
            }
        }
    }

    public string GetConnectionString()
    {
        return TerminalConfigurationService.GetPosConnectionString();
    }

    public async Task UpdateOrderStatusAsync(int orderId, string status)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand(
                @"UPDATE orders
                  SET status = @status,
                      updated_by_terminal_name = @updatedByTerminalName,
                      updated_by_terminal_at = NOW(),
                      updated_at = NOW()
                  WHERE id = @orderId", 
                connection);
            
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@orderId", orderId);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update order status: {ex.Message}");
            throw;
        }
    }

    private static string GetCurrentTerminalName()
    {
        try
        {
            return TerminalConfigurationService.GetConfiguration().TerminalName;
        }
        catch
        {
            return "Terminal";
        }
    }

    // Cloud Configuration Management
    public async Task<Dictionary<string, string>> GetCloudConfigAsync()
    {
        var config = new Dictionary<string, string>();
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand("SELECT * FROM cloud_config LIMIT 1", connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                config["tenant_slug"] = reader.GetString(reader.GetOrdinal("tenant_slug"));
                config["api_key"] = reader.GetString(reader.GetOrdinal("api_key"));
                var cloudUrl = reader.GetString(reader.GetOrdinal("cloud_url"));
                config["cloud_url"] = cloudUrl;
                config["api_base_url"] = TryGetOptionalString(reader, "api_base_url") ?? cloudUrl;
                config["is_enabled"] = reader.GetBoolean(reader.GetOrdinal("is_enabled")).ToString();
                config["polling_interval_seconds"] = reader.GetInt32(reader.GetOrdinal("polling_interval_seconds")).ToString();
                config["auto_print_enabled"] = reader.GetBoolean(reader.GetOrdinal("auto_print_enabled")).ToString();
                config["online_order_master_enabled"] = TryGetOptionalBoolean(reader, "online_order_master_enabled", true).ToString();
                config["online_order_master_terminal_name"] = TryGetOptionalString(reader, "online_order_master_terminal_name") ?? "";
            }

            static string? TryGetOptionalString(MySqlDataReader reader, string columnName)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(columnName);
                    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    return null;
                }
            }

            static bool TryGetOptionalBoolean(MySqlDataReader reader, string columnName, bool fallback)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(columnName);
                    return reader.IsDBNull(ordinal) ? fallback : reader.GetBoolean(ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    return fallback;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get cloud config: {ex.Message}");
        }
        return config;
    }

    public async Task<bool> SaveCloudConfigAsync(string tenantSlug, string apiKey, string cloudUrl, 
        bool isEnabled, int pollingInterval, bool autoPrint)
    {
        return await SaveCloudConfigAsync(tenantSlug, apiKey, cloudUrl, isEnabled, pollingInterval, autoPrint,
            "", "", "", "", 0, "api_polling");
    }

    public async Task<bool> SaveCloudConfigAsync(string tenantSlug, string apiKey, string cloudUrl, 
        bool isEnabled, int pollingInterval, bool autoPrint, string dbHost, string dbName, 
        string dbUsername, string dbPassword, int dbPort, string connectionType)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            
            // Check if config exists
            using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM cloud_config", connection);
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
            
            var sql = count > 0 
                ? @"UPDATE cloud_config SET tenant_slug = @tenantSlug, api_key = @apiKey, 
                    cloud_url = @cloudUrl, is_enabled = @isEnabled, polling_interval_seconds = @pollingInterval,
                    auto_print_enabled = @autoPrint, db_host = @dbHost, db_name = @dbName, 
                    db_username = @dbUsername, db_password = @dbPassword, db_port = @dbPort,
                    connection_type = @connectionType, updated_at = CURRENT_TIMESTAMP"
                : @"INSERT INTO cloud_config (tenant_slug, api_key, cloud_url, is_enabled, 
                    polling_interval_seconds, auto_print_enabled, db_host, db_name, db_username, 
                    db_password, db_port, connection_type) 
                    VALUES (@tenantSlug, @apiKey, @cloudUrl, @isEnabled, @pollingInterval, @autoPrint,
                    @dbHost, @dbName, @dbUsername, @dbPassword, @dbPort, @connectionType)";
            
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantSlug", tenantSlug);
            command.Parameters.AddWithValue("@apiKey", apiKey);
            command.Parameters.AddWithValue("@cloudUrl", cloudUrl);
            command.Parameters.AddWithValue("@isEnabled", isEnabled);
            command.Parameters.AddWithValue("@pollingInterval", pollingInterval);
            command.Parameters.AddWithValue("@autoPrint", autoPrint);
            command.Parameters.AddWithValue("@dbHost", dbHost ?? "");
            command.Parameters.AddWithValue("@dbName", dbName ?? "");
            command.Parameters.AddWithValue("@dbUsername", dbUsername ?? "");
            command.Parameters.AddWithValue("@dbPassword", dbPassword ?? "");
            command.Parameters.AddWithValue("@dbPort", dbPort);
            command.Parameters.AddWithValue("@connectionType", connectionType ?? "api_polling");
            
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save cloud config: {ex.Message}");
            return false;
        }
    }

    // NEW: Cloud Configuration CRUD methods for CloudConfiguration model
    public async Task<CloudConfiguration?> GetCloudConfigurationAsync()
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand("SELECT * FROM cloud_config LIMIT 1", connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                return new CloudConfiguration
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    ApiBaseUrl = reader.GetString(reader.GetOrdinal("api_base_url")),
                    RestApiBaseUrl = reader.GetString(reader.GetOrdinal("api_base_url")),
                    TenantSlug = reader.GetString(reader.GetOrdinal("tenant_slug")),
                    ApiKey = reader.GetString(reader.GetOrdinal("api_key")),
                    WebSocketUrl = reader.GetString(reader.GetOrdinal("websocket_url")),
                    ConnectionTimeout = reader.GetInt32(reader.GetOrdinal("connection_timeout")),
                    PollingIntervalSeconds = reader.GetInt32(reader.GetOrdinal("polling_interval_seconds")),
                    MaxRetryAttempts = reader.GetInt32(reader.GetOrdinal("max_retry_attempts")),
                    IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
                    IsApiTested = reader.GetBoolean(reader.GetOrdinal("is_api_tested")),
                    IsWebSocketTested = reader.GetBoolean(reader.GetOrdinal("is_websocket_tested")),
                    ApiTestResult = reader.IsDBNull("api_test_result") ? null : reader.GetString(reader.GetOrdinal("api_test_result")),
                    WebSocketTestResult = reader.IsDBNull("websocket_test_result") ? null : reader.GetString(reader.GetOrdinal("websocket_test_result")),
                    LastApiTest = reader.IsDBNull("last_api_test") ? null : reader.GetDateTime(reader.GetOrdinal("last_api_test")),
                    LastWebSocketTest = reader.IsDBNull("last_websocket_test") ? null : reader.GetDateTime(reader.GetOrdinal("last_websocket_test")),
                    AutoPrintEnabled = reader.GetBoolean(reader.GetOrdinal("auto_print_enabled")),
                    NotificationsEnabled = reader.GetBoolean(reader.GetOrdinal("notifications_enabled")),
                    OnlineOrderMasterEnabled = GetOptionalBoolean(reader, "online_order_master_enabled", true),
                    OnlineOrderMasterTerminalName = GetOptionalString(reader, "online_order_master_terminal_name") ?? "",
                    LastSync = reader.IsDBNull("last_sync") ? null : reader.GetDateTime(reader.GetOrdinal("last_sync")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get cloud configuration: {ex.Message}");
            return null;
        }
    }

    private static string? GetOptionalString(MySqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static bool GetOptionalBoolean(MySqlDataReader reader, string columnName, bool fallback)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? fallback : reader.GetBoolean(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return fallback;
        }
    }

    private static async Task<string?> GetExistingCloudConfigValueAsync(MySqlConnection connection, string columnName)
    {
        try
        {
            using var command = new MySqlCommand($"SELECT {columnName} FROM cloud_config ORDER BY id LIMIT 1", connection);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveCloudConfigurationAsync(CloudConfiguration config)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" SaveCloudConfigurationAsync starting...");
            using var connection = await GetConnectionAsync();
            System.Diagnostics.Debug.WriteLine($" Database connection established");
            
            // Check if config exists
            using var checkCommand = new MySqlCommand("SELECT COUNT(*) FROM cloud_config", connection);
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
            System.Diagnostics.Debug.WriteLine($" Existing config count: {count}");
            
            // Use RestApiBaseUrl if available, fallback to ApiBaseUrl
            var apiBaseUrl = !string.IsNullOrEmpty(config.RestApiBaseUrl) 
                ? config.RestApiBaseUrl 
                : config.ApiBaseUrl;

            var existingMasterTerminalName = await GetExistingCloudConfigValueAsync(connection, "online_order_master_terminal_name");
            var onlineOrderMasterTerminalName = !string.IsNullOrWhiteSpace(config.OnlineOrderMasterTerminalName)
                ? config.OnlineOrderMasterTerminalName.Trim()
                : (!string.IsNullOrWhiteSpace(existingMasterTerminalName)
                    ? existingMasterTerminalName
                    : TerminalConfigurationService.GetConfiguration().TerminalName);
            
            var sql = count > 0 
                ? @"UPDATE cloud_config SET 
                    api_base_url = @apiBaseUrl,
                    tenant_slug = @tenantSlug,
                    api_key = @apiKey,
                    websocket_url = @websocketUrl,
                    connection_timeout = @connectionTimeout,
                    polling_interval_seconds = @pollingInterval,
                    max_retry_attempts = @maxRetryAttempts,
                    is_enabled = @isEnabled,
                    is_api_tested = @isApiTested,
                    is_websocket_tested = @isWebSocketTested,
                    api_test_result = @apiTestResult,
                    websocket_test_result = @websocketTestResult,
                    last_api_test = @lastApiTest,
                    last_websocket_test = @lastWebSocketTest,
                    auto_print_enabled = @autoPrint,
                    notifications_enabled = @notificationsEnabled,
                    online_order_master_enabled = @onlineOrderMasterEnabled,
                    online_order_master_terminal_name = @onlineOrderMasterTerminalName,
                    updated_at = CURRENT_TIMESTAMP
                    WHERE id = (SELECT MIN(id) FROM (SELECT id FROM cloud_config) as temp)"
                : @"INSERT INTO cloud_config (
                    api_base_url, tenant_slug, api_key, websocket_url,
                    connection_timeout, polling_interval_seconds, max_retry_attempts,
                    is_enabled, is_api_tested, is_websocket_tested,
                    api_test_result, websocket_test_result,
                    last_api_test, last_websocket_test,
                    auto_print_enabled, notifications_enabled,
                    online_order_master_enabled, online_order_master_terminal_name
                    ) VALUES (
                    @apiBaseUrl, @tenantSlug, @apiKey, @websocketUrl,
                    @connectionTimeout, @pollingInterval, @maxRetryAttempts,
                    @isEnabled, @isApiTested, @isWebSocketTested,
                    @apiTestResult, @websocketTestResult,
                    @lastApiTest, @lastWebSocketTest,
                    @autoPrint, @notificationsEnabled,
                    @onlineOrderMasterEnabled, @onlineOrderMasterTerminalName
                    )";
            
            System.Diagnostics.Debug.WriteLine($" SQL: {(count > 0 ? "UPDATE" : "INSERT")}");
            
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@apiBaseUrl", apiBaseUrl);
            command.Parameters.AddWithValue("@tenantSlug", config.TenantSlug);
            command.Parameters.AddWithValue("@apiKey", config.ApiKey);
            command.Parameters.AddWithValue("@websocketUrl", config.WebSocketUrl ?? "");
            command.Parameters.AddWithValue("@connectionTimeout", config.ConnectionTimeout);
            command.Parameters.AddWithValue("@pollingInterval", config.PollingIntervalSeconds);
            command.Parameters.AddWithValue("@maxRetryAttempts", config.MaxRetryAttempts);
            command.Parameters.AddWithValue("@isEnabled", config.IsEnabled);
            command.Parameters.AddWithValue("@isApiTested", config.IsApiTested);
            command.Parameters.AddWithValue("@isWebSocketTested", config.IsWebSocketTested);
            command.Parameters.AddWithValue("@apiTestResult", (object?)config.ApiTestResult ?? DBNull.Value);
            command.Parameters.AddWithValue("@websocketTestResult", (object?)config.WebSocketTestResult ?? DBNull.Value);
            command.Parameters.AddWithValue("@lastApiTest", (object?)config.LastApiTest ?? DBNull.Value);
            command.Parameters.AddWithValue("@lastWebSocketTest", (object?)config.LastWebSocketTest ?? DBNull.Value);
            command.Parameters.AddWithValue("@autoPrint", config.AutoPrintEnabled);
            command.Parameters.AddWithValue("@notificationsEnabled", config.NotificationsEnabled);
            command.Parameters.AddWithValue("@onlineOrderMasterEnabled", config.OnlineOrderMasterEnabled);
            command.Parameters.AddWithValue("@onlineOrderMasterTerminalName", onlineOrderMasterTerminalName);
            
            System.Diagnostics.Debug.WriteLine($" Executing database command...");
            var rowsAffected = await command.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($" Rows affected: {rowsAffected}");
            
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to save cloud configuration: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<bool> UpdateConnectionTestResultsAsync(bool isApiTested, string apiResult, bool isWebSocketTested, string websocketResult)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            using var command = new MySqlCommand(@"
                UPDATE cloud_config SET 
                    is_api_tested = @isApiTested,
                    api_test_result = @apiResult,
                    last_api_test = @lastApiTest,
                    is_websocket_tested = @isWebSocketTested,
                    websocket_test_result = @websocketResult,
                    last_websocket_test = @lastWebSocketTest,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = 1", connection);
            
            command.Parameters.AddWithValue("@isApiTested", isApiTested);
            command.Parameters.AddWithValue("@apiResult", apiResult);
            command.Parameters.AddWithValue("@lastApiTest", DateTime.UtcNow);
            command.Parameters.AddWithValue("@isWebSocketTested", isWebSocketTested);
            command.Parameters.AddWithValue("@websocketResult", websocketResult);
            command.Parameters.AddWithValue("@lastWebSocketTest", DateTime.UtcNow);
            
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update test results: {ex.Message}");
            return false;
        }
    }
}
