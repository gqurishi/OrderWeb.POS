using MySqlConnector;
using System.Data;

namespace POS_in_NET.Services;

/// <summary>
/// Database Migration Service - Upgrades from Pos-net to restaurant_local schema
/// Transforms basic POS structure into comprehensive restaurant management system
/// </summary>
public class DatabaseMigrationService
{
    private readonly string _oldConnectionString;
    private readonly string _newConnectionString;

    public DatabaseMigrationService()
    {
        // Old database connection
        _oldConnectionString = "Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;";
        
        // New database connection
        _newConnectionString = "Server=localhost;Database=restaurant_local;Uid=root;Pwd=root;Port=3306;";
    }

    /// <summary>
    /// Execute complete database migration from Pos-net to restaurant_local
    /// </summary>
    public async Task<bool> MigrateDatabaseAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("🚀 Starting database migration from Pos-net to restaurant_local...");

            // Step 1: Create new database
            await CreateNewDatabaseAsync();

            // Step 2: Create new table structure
            await CreateNewTableStructureAsync();

            // Step 3: Migrate existing data
            await MigrateExistingDataAsync();

            // Step 4: Create indexes for performance
            await CreateIndexesAsync();

            // Step 4.5: Create reporting views
            await CreateReportingViewsAsync();

            // Step 5: Insert default data
            await InsertDefaultDataAsync();

            System.Diagnostics.Debug.WriteLine("✅ Database migration completed successfully!");
            System.Diagnostics.Debug.WriteLine("📊 New database 'restaurant_local' is ready with comprehensive restaurant management structure");
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Database migration failed: {ex.Message}");
            return false;
        }
    }

    private async Task CreateNewDatabaseAsync()
    {
        var connectionStringWithoutDb = "Server=localhost;Uid=root;Pwd=root;Port=3306;";
        
        using var connection = new MySqlConnection(connectionStringWithoutDb);
        await connection.OpenAsync();

        var createDbCommand = @"
            CREATE DATABASE IF NOT EXISTS restaurant_local 
            CHARACTER SET utf8mb4 
            COLLATE utf8mb4_unicode_ci";

        using var command = new MySqlCommand(createDbCommand, connection);
        await command.ExecuteNonQueryAsync();
        
        System.Diagnostics.Debug.WriteLine("✅ Created database: restaurant_local");
    }

    private async Task CreateNewTableStructureAsync()
    {
        using var connection = new MySqlConnection(_newConnectionString);
        await connection.OpenAsync();

        // 1. Staff table (upgraded from users)
        var createStaffTable = @"
            CREATE TABLE IF NOT EXISTS staff (
                id INT AUTO_INCREMENT PRIMARY KEY,
                employee_id VARCHAR(50) UNIQUE NOT NULL,
                name VARCHAR(100) NOT NULL,
                username VARCHAR(50) UNIQUE NOT NULL,
                password_hash VARCHAR(255) NOT NULL,
                role ENUM('admin', 'manager', 'cashier', 'kitchen', 'delivery') DEFAULT 'cashier',
                phone VARCHAR(20),
                email VARCHAR(100),
                hourly_rate DECIMAL(8,2) DEFAULT 0.00,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 2. Customers table
        var createCustomersTable = @"
            CREATE TABLE IF NOT EXISTS customers (
                id INT AUTO_INCREMENT PRIMARY KEY,
                customer_code VARCHAR(50) UNIQUE,
                name VARCHAR(100) NOT NULL,
                phone VARCHAR(20) UNIQUE,
                email VARCHAR(100),
                address TEXT,
                city VARCHAR(50),
                postal_code VARCHAR(20),
                loyalty_points INT DEFAULT 0,
                total_orders INT DEFAULT 0,
                total_spent DECIMAL(12,2) DEFAULT 0.00,
                last_order_date TIMESTAMP NULL,
                is_active BOOLEAN DEFAULT TRUE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 3. Menu Items table
        var createMenuItemsTable = @"
            CREATE TABLE IF NOT EXISTS menu_items (
                id INT AUTO_INCREMENT PRIMARY KEY,
                item_code VARCHAR(50) UNIQUE NOT NULL,
                name VARCHAR(100) NOT NULL,
                description TEXT,
                category VARCHAR(50) NOT NULL,
                price DECIMAL(8,2) NOT NULL,
                price_dine_in DECIMAL(10,2) NULL,
                price_takeaway DECIMAL(10,2) NULL,
                cost_price DECIMAL(8,2) DEFAULT 0.00,
                is_available BOOLEAN DEFAULT TRUE,
                is_featured BOOLEAN DEFAULT FALSE,
                preparation_time INT DEFAULT 10, -- minutes
                calories INT DEFAULT 0,
                ingredients TEXT,
                allergens TEXT,
                image_url VARCHAR(500),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 4. Inventory table
        var createInventoryTable = @"
            CREATE TABLE IF NOT EXISTS inventory (
                id INT AUTO_INCREMENT PRIMARY KEY,
                item_code VARCHAR(50) UNIQUE NOT NULL,
                item_name VARCHAR(100) NOT NULL,
                category VARCHAR(50) NOT NULL,
                unit VARCHAR(20) NOT NULL, -- kg, pieces, liters, etc
                current_stock DECIMAL(10,2) DEFAULT 0.00,
                minimum_stock DECIMAL(10,2) DEFAULT 0.00,
                maximum_stock DECIMAL(10,2) DEFAULT 0.00,
                unit_cost DECIMAL(8,2) DEFAULT 0.00,
                supplier VARCHAR(100),
                supplier_contact VARCHAR(20),
                last_restock_date TIMESTAMP NULL,
                expiry_date DATE NULL,
                is_perishable BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 5. Local Orders table (walk-in, phone orders)
        var createLocalOrdersTable = @"
            CREATE TABLE IF NOT EXISTS local_orders (
                id INT AUTO_INCREMENT PRIMARY KEY,
                order_number VARCHAR(50) UNIQUE NOT NULL,
                customer_id INT NULL,
                customer_name VARCHAR(100),
                customer_phone VARCHAR(20),
                order_type ENUM('dine_in', 'takeaway', 'phone_order') NOT NULL,
                table_number VARCHAR(10) NULL,
                staff_id INT NOT NULL,
                total_amount DECIMAL(10,2) NOT NULL,
                tax_amount DECIMAL(8,2) DEFAULT 0.00,
                discount_amount DECIMAL(8,2) DEFAULT 0.00,
                payment_method ENUM('cash', 'card', 'mixed') NOT NULL,
                status ENUM('new', 'confirmed', 'preparing', 'ready', 'served', 'completed', 'cancelled') DEFAULT 'new',
                special_instructions TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL,
                FOREIGN KEY (staff_id) REFERENCES staff(id)
            ) ENGINE=InnoDB";

        // 6. Online Orders table (synced from cloud)
        var createOnlineOrdersTable = @"
            CREATE TABLE IF NOT EXISTS online_orders (
                id INT AUTO_INCREMENT PRIMARY KEY,
                cloud_order_id VARCHAR(100) UNIQUE NOT NULL,
                order_number VARCHAR(50) UNIQUE NOT NULL,
                customer_id INT NULL,
                customer_name VARCHAR(100) NOT NULL,
                customer_phone VARCHAR(20),
                customer_email VARCHAR(100),
                customer_address TEXT,
                order_type ENUM('pickup', 'delivery') DEFAULT 'pickup',
                total_amount DECIMAL(10,2) NOT NULL,
                subtotal_amount DECIMAL(10,2) NOT NULL,
                delivery_fee DECIMAL(8,2) DEFAULT 0.00,
                tax_amount DECIMAL(8,2) DEFAULT 0.00,
                payment_method VARCHAR(50),
                payment_status ENUM('pending', 'paid', 'failed', 'refunded') DEFAULT 'pending',
                scheduled_time TIMESTAMP NULL,
                status ENUM('new', 'confirmed', 'kitchen', 'preparing', 'ready', 'out_for_delivery', 'delivered', 'completed', 'cancelled') DEFAULT 'new',
                special_instructions TEXT,
                sync_status ENUM('synced', 'pending', 'failed') DEFAULT 'synced',
                source_platform VARCHAR(50) DEFAULT 'orderweb',
                kitchen_time TIMESTAMP NULL,
                preparing_time TIMESTAMP NULL,
                ready_time TIMESTAMP NULL,
                delivered_time TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL
            ) ENGINE=InnoDB";

        // 7. Local Order Items table
        var createLocalOrderItemsTable = @"
            CREATE TABLE IF NOT EXISTS local_order_items (
                id INT AUTO_INCREMENT PRIMARY KEY,
                order_id INT NOT NULL,
                menu_item_id INT NOT NULL,
                item_name VARCHAR(100) NOT NULL,
                quantity INT NOT NULL,
                unit_price DECIMAL(8,2) NOT NULL,
                base_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                vat_rate_applied DECIMAL(5,2) NOT NULL DEFAULT 0.00,
                vat_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                total_price DECIMAL(8,2) NOT NULL,
                special_instructions TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (order_id) REFERENCES local_orders(id) ON DELETE CASCADE,
                FOREIGN KEY (menu_item_id) REFERENCES menu_items(id)
            ) ENGINE=InnoDB";

        // 8. Online Order Items table
        var createOnlineOrderItemsTable = @"
            CREATE TABLE IF NOT EXISTS online_order_items (
                id INT AUTO_INCREMENT PRIMARY KEY,
                order_id INT NOT NULL,
                cloud_item_id INT NULL,
                menu_item_id INT NULL,
                item_name VARCHAR(100) NOT NULL,
                quantity INT NOT NULL,
                unit_price DECIMAL(8,2) NOT NULL,
                base_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                vat_rate_applied DECIMAL(5,2) NOT NULL DEFAULT 0.00,
                vat_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                total_price DECIMAL(8,2) NOT NULL,
                special_instructions TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (order_id) REFERENCES online_orders(id) ON DELETE CASCADE,
                FOREIGN KEY (menu_item_id) REFERENCES menu_items(id) ON DELETE SET NULL
            ) ENGINE=InnoDB";

        // 9. Shifts table
        var createShiftsTable = @"
            CREATE TABLE IF NOT EXISTS shifts (
                id INT AUTO_INCREMENT PRIMARY KEY,
                staff_id INT NOT NULL,
                shift_date DATE NOT NULL,
                start_time TIME NOT NULL,
                end_time TIME NULL,
                planned_hours DECIMAL(4,2) DEFAULT 8.00,
                actual_hours DECIMAL(4,2) NULL,
                break_minutes INT DEFAULT 30,
                hourly_rate DECIMAL(8,2) NOT NULL,
                total_pay DECIMAL(10,2) NULL,
                status ENUM('scheduled', 'started', 'on_break', 'completed', 'absent') DEFAULT 'scheduled',
                notes TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (staff_id) REFERENCES staff(id),
                UNIQUE KEY unique_staff_date (staff_id, shift_date)
            ) ENGINE=InnoDB";

        // 10. Payments table
        var createPaymentsTable = @"
            CREATE TABLE IF NOT EXISTS payments (
                id INT AUTO_INCREMENT PRIMARY KEY,
                transaction_id VARCHAR(100) UNIQUE NOT NULL,
                order_type ENUM('local', 'online') NOT NULL,
                order_id INT NOT NULL,
                payment_method ENUM('cash', 'card', 'digital_wallet', 'gift_card', 'store_credit') NOT NULL,
                amount DECIMAL(10,2) NOT NULL,
                currency VARCHAR(3) DEFAULT 'USD',
                status ENUM('pending', 'completed', 'failed', 'refunded') DEFAULT 'completed',
                card_last_four VARCHAR(4) NULL,
                card_type VARCHAR(20) NULL,
                processed_by_staff_id INT NOT NULL,
                reference_number VARCHAR(100),
                notes TEXT,
                processed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (processed_by_staff_id) REFERENCES staff(id)
            ) ENGINE=InnoDB";

        // 11. Printing Queue table
        var createPrintingQueueTable = @"
            CREATE TABLE IF NOT EXISTS printing_queue (
                id INT AUTO_INCREMENT PRIMARY KEY,
                printer_name VARCHAR(100) NOT NULL,
                printer_type ENUM('receipt', 'kitchen', 'bar', 'label') NOT NULL,
                order_type ENUM('local', 'online') NOT NULL,
                order_id INT NOT NULL,
                document_type ENUM('receipt', 'kitchen_ticket', 'delivery_label', 'invoice') NOT NULL,
                content TEXT NOT NULL,
                priority INT DEFAULT 1, -- 1=highest, 5=lowest
                status ENUM('pending', 'printing', 'printed', 'failed', 'cancelled') DEFAULT 'pending',
                retry_count INT DEFAULT 0,
                max_retries INT DEFAULT 3,
                error_message TEXT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                printed_at TIMESTAMP NULL
            ) ENGINE=InnoDB";

        // 12. Sync Log table
        var createSyncLogTable = @"
            CREATE TABLE IF NOT EXISTS sync_log (
                id INT AUTO_INCREMENT PRIMARY KEY,
                sync_type ENUM('orders', 'menu', 'customers', 'inventory', 'full_sync') NOT NULL,
                sync_direction ENUM('upload', 'download', 'bidirectional') NOT NULL,
                source_system VARCHAR(50) NOT NULL, -- 'orderweb', 'api', 'manual'
                records_processed INT DEFAULT 0,
                records_successful INT DEFAULT 0,
                records_failed INT DEFAULT 0,
                status ENUM('started', 'in_progress', 'completed', 'failed', 'cancelled') DEFAULT 'started',
                error_details TEXT NULL,
                sync_duration_seconds INT NULL,
                started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                completed_at TIMESTAMP NULL
            ) ENGINE=InnoDB";

        // 13. Settings table (keep existing)
        var createSettingsTable = @"
            CREATE TABLE IF NOT EXISTS settings (
                id INT AUTO_INCREMENT PRIMARY KEY,
                setting_key VARCHAR(100) UNIQUE NOT NULL,
                setting_value TEXT,
                category VARCHAR(50) DEFAULT 'general',
                description TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 14. Cloud Config table (enhanced)
        var createCloudConfigTable = @"
            CREATE TABLE IF NOT EXISTS cloud_config (
                id INT AUTO_INCREMENT PRIMARY KEY,
                tenant_slug VARCHAR(255) NOT NULL DEFAULT '',
                api_key VARCHAR(255) NOT NULL DEFAULT '',
                cloud_url VARCHAR(500) NOT NULL DEFAULT 'https://orderweb.net/api/pos',
                is_enabled BOOLEAN DEFAULT FALSE,
                polling_interval_seconds INT DEFAULT 30,
                auto_print_enabled BOOLEAN DEFAULT TRUE,
                last_sync TIMESTAMP NULL,
                db_host VARCHAR(255) DEFAULT '',
                db_name VARCHAR(255) DEFAULT '',
                db_username VARCHAR(255) DEFAULT '',
                db_password VARCHAR(500) DEFAULT '',
                db_port INT DEFAULT 3306,
                connection_type VARCHAR(50) DEFAULT 'api_polling',
                connection_string TEXT DEFAULT '',
                orderweb_enabled BOOLEAN DEFAULT FALSE,
                orderweb_connection_string TEXT DEFAULT '',
                restaurant_slug VARCHAR(255) DEFAULT '',
                direct_db_enabled BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB";

        // 15. Business Info table (enhanced)
        var createBusinessInfoTable = @"
            CREATE TABLE IF NOT EXISTS business_info (
                id INT AUTO_INCREMENT PRIMARY KEY,
                restaurant_name VARCHAR(255) NOT NULL DEFAULT 'Restaurant POS',
                business_type VARCHAR(50) DEFAULT 'restaurant',
                cuisine_type VARCHAR(100) DEFAULT '',
                address TEXT DEFAULT '',
                city VARCHAR(100) DEFAULT '',
                state VARCHAR(100) DEFAULT '',
                postal_code VARCHAR(20) DEFAULT '',
                country VARCHAR(100) DEFAULT '',
                phone_number VARCHAR(50) DEFAULT '',
                email VARCHAR(255) DEFAULT '',
                website VARCHAR(255) DEFAULT '',
                tax_code VARCHAR(100) DEFAULT '',
                tax_rate DECIMAL(5,2) DEFAULT 0.00,
                service_charge_rate DECIMAL(5,2) DEFAULT 0.00,
                currency VARCHAR(3) DEFAULT 'USD',
                timezone VARCHAR(50) DEFAULT 'UTC',
                opening_hours JSON DEFAULT NULL,
                social_media JSON DEFAULT NULL,
                logo_url VARCHAR(500) DEFAULT '',
                description TEXT DEFAULT '',
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                updated_by VARCHAR(100) DEFAULT ''
            ) ENGINE=InnoDB";

        // Execute all table creation commands
        var tables = new[]
        {
            ("staff", createStaffTable),
            ("customers", createCustomersTable),
            ("menu_items", createMenuItemsTable),
            ("inventory", createInventoryTable),
            ("local_orders", createLocalOrdersTable),
            ("online_orders", createOnlineOrdersTable),
            ("local_order_items", createLocalOrderItemsTable),
            ("online_order_items", createOnlineOrderItemsTable),
            ("shifts", createShiftsTable),
            ("payments", createPaymentsTable),
            ("printing_queue", createPrintingQueueTable),
            ("sync_log", createSyncLogTable),
            ("settings", createSettingsTable),
            ("cloud_config", createCloudConfigTable),
            ("business_info", createBusinessInfoTable)
        };

        foreach (var (tableName, sql) in tables)
        {
            using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($"✅ Created table: {tableName}");
        }
    }

    private async Task MigrateExistingDataAsync()
    {
        System.Diagnostics.Debug.WriteLine("📦 Migrating existing data...");

        // Migrate Users to Staff
        await MigrateUsersToStaffAsync();

        // Migrate Orders to Online Orders (assuming existing orders are online)
        await MigrateOrdersToOnlineOrdersAsync();

        // Migrate Order Items to Online Order Items
        await MigrateOrderItemsAsync();

        // Migrate Settings
        await MigrateSettingsAsync();

        // Migrate Cloud Config
        await MigrateCloudConfigAsync();

        // Migrate Business Info
        await MigrateBusinessInfoAsync();
    }

    private async Task MigrateUsersToStaffAsync()
    {
        try
        {
            using var oldConnection = new MySqlConnection(_oldConnectionString);
            using var newConnection = new MySqlConnection(_newConnectionString);
            
            await oldConnection.OpenAsync();
            await newConnection.OpenAsync();

            // Get existing users
            var selectUsersQuery = "SELECT id, username, password_hash, role, created_at, updated_at, name FROM users";
            using var selectCommand = new MySqlCommand(selectUsersQuery, oldConnection);
            using var reader = await selectCommand.ExecuteReaderAsync();

            var users = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                users.Add(new
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Username = reader.GetString(reader.GetOrdinal("username")),
                    PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
                    Role = reader.GetString(reader.GetOrdinal("role")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
                    Name = reader.IsDBNull("name") ? reader.GetString(reader.GetOrdinal("username")) : reader.GetString(reader.GetOrdinal("name"))
                });
            }

            // Insert into staff table
            foreach (var user in users)
            {
                var insertStaffQuery = @"
                    INSERT INTO staff (employee_id, name, username, password_hash, role, is_active, created_at, updated_at)
                    VALUES (@employeeId, @name, @username, @passwordHash, @role, TRUE, @createdAt, @updatedAt)";

                using var insertCommand = new MySqlCommand(insertStaffQuery, newConnection);
                insertCommand.Parameters.AddWithValue("@employeeId", $"EMP{user.Id:D4}");
                insertCommand.Parameters.AddWithValue("@name", user.Name);
                insertCommand.Parameters.AddWithValue("@username", user.Username);
                insertCommand.Parameters.AddWithValue("@passwordHash", user.PasswordHash);
                insertCommand.Parameters.AddWithValue("@role", user.Role);
                insertCommand.Parameters.AddWithValue("@createdAt", user.CreatedAt);
                insertCommand.Parameters.AddWithValue("@updatedAt", user.UpdatedAt);

                await insertCommand.ExecuteNonQueryAsync();
            }

            System.Diagnostics.Debug.WriteLine($"✅ Migrated {users.Count} users to staff table");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Error migrating users: {ex.Message}");
        }
    }

    private async Task MigrateOrdersToOnlineOrdersAsync()
    {
        try
        {
            using var oldConnection = new MySqlConnection(_oldConnectionString);
            using var newConnection = new MySqlConnection(_newConnectionString);
            
            await oldConnection.OpenAsync();
            await newConnection.OpenAsync();

            // Get existing orders
            var selectOrdersQuery = @"
                SELECT id, order_id, order_number, cloud_order_id, customer_name, customer_phone, customer_email, 
                       customer_address, total_amount, subtotal_amount, delivery_fee, tax_amount, order_type, 
                       payment_method, special_instructions, scheduled_time, status, order_data, sync_status,
                       kitchen_time, preparing_time, ready_time, delivering_time, completed_time, created_at, updated_at
                FROM orders";

            using var selectCommand = new MySqlCommand(selectOrdersQuery, oldConnection);
            using var reader = await selectCommand.ExecuteReaderAsync();

            var orders = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                orders.Add(new
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    OrderId = reader.IsDBNull("order_id") ? null : reader.GetString(reader.GetOrdinal("order_id")),
                    OrderNumber = reader.IsDBNull("order_number") ? $"ORD{reader.GetInt32(reader.GetOrdinal("id")):D6}" : reader.GetString(reader.GetOrdinal("order_number")),
                    CloudOrderId = reader.IsDBNull("cloud_order_id") ? reader.GetString(reader.GetOrdinal("order_id")) ?? $"CLOUD{reader.GetInt32(reader.GetOrdinal("id"))}" : reader.GetString(reader.GetOrdinal("cloud_order_id")),
                    CustomerName = reader.IsDBNull("customer_name") ? "Walk-in Customer" : reader.GetString(reader.GetOrdinal("customer_name")),
                    CustomerPhone = reader.IsDBNull("customer_phone") ? null : reader.GetString(reader.GetOrdinal("customer_phone")),
                    CustomerEmail = reader.IsDBNull("customer_email") ? null : reader.GetString(reader.GetOrdinal("customer_email")),
                    CustomerAddress = reader.IsDBNull("customer_address") ? null : reader.GetString(reader.GetOrdinal("customer_address")),
                    TotalAmount = reader.IsDBNull("total_amount") ? 0 : reader.GetDecimal(reader.GetOrdinal("total_amount")),
                    SubtotalAmount = reader.IsDBNull("subtotal_amount") ? 0 : reader.GetDecimal(reader.GetOrdinal("subtotal_amount")),
                    DeliveryFee = reader.IsDBNull("delivery_fee") ? 0 : reader.GetDecimal(reader.GetOrdinal("delivery_fee")),
                    TaxAmount = reader.IsDBNull("tax_amount") ? 0 : reader.GetDecimal(reader.GetOrdinal("tax_amount")),
                    OrderType = reader.IsDBNull("order_type") ? "pickup" : reader.GetString(reader.GetOrdinal("order_type")),
                    PaymentMethod = reader.IsDBNull("payment_method") ? "cash" : reader.GetString(reader.GetOrdinal("payment_method")),
                    SpecialInstructions = reader.IsDBNull("special_instructions") ? null : reader.GetString(reader.GetOrdinal("special_instructions")),
                    ScheduledTime = reader.IsDBNull("scheduled_time") ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("scheduled_time")),
                    Status = reader.IsDBNull("status") ? "new" : reader.GetString(reader.GetOrdinal("status")),
                    SyncStatus = reader.IsDBNull("sync_status") ? "synced" : reader.GetString(reader.GetOrdinal("sync_status")),
                    KitchenTime = reader.IsDBNull("kitchen_time") ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("kitchen_time")),
                    PreparingTime = reader.IsDBNull("preparing_time") ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("preparing_time")),
                    ReadyTime = reader.IsDBNull("ready_time") ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("ready_time")),
                    DeliveredTime = reader.IsDBNull("delivering_time") ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("delivering_time")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                });
            }

            // Insert into online_orders table
            foreach (var order in orders)
            {
                var insertOrderQuery = @"
                    INSERT INTO online_orders (
                        cloud_order_id, order_number, customer_name, customer_phone, customer_email, 
                        customer_address, order_type, total_amount, subtotal_amount, delivery_fee, 
                        tax_amount, payment_method, special_instructions, scheduled_time, status, 
                        sync_status, kitchen_time, preparing_time, ready_time, delivered_time, 
                        created_at, updated_at
                    ) VALUES (
                        @cloudOrderId, @orderNumber, @customerName, @customerPhone, @customerEmail,
                        @customerAddress, @orderType, @totalAmount, @subtotalAmount, @deliveryFee,
                        @taxAmount, @paymentMethod, @specialInstructions, @scheduledTime, @status,
                        @syncStatus, @kitchenTime, @preparingTime, @readyTime, @deliveredTime,
                        @createdAt, @updatedAt
                    )";

                using var insertCommand = new MySqlCommand(insertOrderQuery, newConnection);
                insertCommand.Parameters.AddWithValue("@cloudOrderId", order.CloudOrderId);
                insertCommand.Parameters.AddWithValue("@orderNumber", order.OrderNumber);
                insertCommand.Parameters.AddWithValue("@customerName", order.CustomerName);
                insertCommand.Parameters.AddWithValue("@customerPhone", order.CustomerPhone);
                insertCommand.Parameters.AddWithValue("@customerEmail", order.CustomerEmail);
                insertCommand.Parameters.AddWithValue("@customerAddress", order.CustomerAddress);
                insertCommand.Parameters.AddWithValue("@orderType", order.OrderType);
                insertCommand.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
                insertCommand.Parameters.AddWithValue("@subtotalAmount", order.SubtotalAmount);
                insertCommand.Parameters.AddWithValue("@deliveryFee", order.DeliveryFee);
                insertCommand.Parameters.AddWithValue("@taxAmount", order.TaxAmount);
                insertCommand.Parameters.AddWithValue("@paymentMethod", order.PaymentMethod);
                insertCommand.Parameters.AddWithValue("@specialInstructions", order.SpecialInstructions);
                insertCommand.Parameters.AddWithValue("@scheduledTime", order.ScheduledTime);
                insertCommand.Parameters.AddWithValue("@status", order.Status);
                insertCommand.Parameters.AddWithValue("@syncStatus", order.SyncStatus);
                insertCommand.Parameters.AddWithValue("@kitchenTime", order.KitchenTime);
                insertCommand.Parameters.AddWithValue("@preparingTime", order.PreparingTime);
                insertCommand.Parameters.AddWithValue("@readyTime", order.ReadyTime);
                insertCommand.Parameters.AddWithValue("@deliveredTime", order.DeliveredTime);
                insertCommand.Parameters.AddWithValue("@createdAt", order.CreatedAt);
                insertCommand.Parameters.AddWithValue("@updatedAt", order.UpdatedAt);

                await insertCommand.ExecuteNonQueryAsync();
            }

            System.Diagnostics.Debug.WriteLine($"✅ Migrated {orders.Count} orders to online_orders table");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Error migrating orders: {ex.Message}");
        }
    }

    private async Task MigrateOrderItemsAsync()
    {
        try
        {
            using var oldConnection = new MySqlConnection(_oldConnectionString);
            using var newConnection = new MySqlConnection(_newConnectionString);
            
            await oldConnection.OpenAsync();
            await newConnection.OpenAsync();

            // Get existing order items
            var selectItemsQuery = @"
                SELECT oi.id, oi.order_id, oi.cloud_item_id, oi.menu_item_id, oi.item_name, 
                       oi.quantity, oi.item_price, oi.special_instructions
                FROM order_items oi
                INNER JOIN orders o ON oi.order_id = o.id";

            using var selectCommand = new MySqlCommand(selectItemsQuery, oldConnection);
            using var reader = await selectCommand.ExecuteReaderAsync();

            var items = new List<dynamic>();
            while (await reader.ReadAsync())
            {
                items.Add(new
                {
                    OrderId = reader.GetInt32(reader.GetOrdinal("order_id")),
                    CloudItemId = reader.IsDBNull("cloud_item_id") ? (int?)null : reader.GetInt32(reader.GetOrdinal("cloud_item_id")),
                    MenuItemId = reader.IsDBNull("menu_item_id") ? (int?)null : reader.GetInt32(reader.GetOrdinal("menu_item_id")),
                    ItemName = reader.GetString(reader.GetOrdinal("item_name")),
                    Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
                    UnitPrice = reader.GetDecimal(reader.GetOrdinal("item_price")),
                    SpecialInstructions = reader.IsDBNull("special_instructions") ? null : reader.GetString(reader.GetOrdinal("special_instructions"))
                });
            }

            // Map old order IDs to new online_order IDs
            var orderIdMapping = new Dictionary<int, int>();
            var getOrderMappingQuery = "SELECT ROW_NUMBER() OVER (ORDER BY id) as new_id, id as old_id FROM orders";
            using var mappingCommand = new MySqlCommand(getOrderMappingQuery, oldConnection);
            using var mappingReader = await mappingCommand.ExecuteReaderAsync();
            
            while (await mappingReader.ReadAsync())
            {
                var oldId = mappingReader.GetInt32("old_id");
                var newId = mappingReader.GetInt32("new_id");
                orderIdMapping[oldId] = newId;
            }

            // Insert into online_order_items table
            foreach (var item in items)
            {
                if (!orderIdMapping.TryGetValue(item.OrderId, out int newOrderId))
                    continue;

                var insertItemQuery = @"
                    INSERT INTO online_order_items (
                        order_id, cloud_item_id, menu_item_id, item_name, quantity, 
                        unit_price, total_price, special_instructions
                    ) VALUES (
                        @orderId, @cloudItemId, @menuItemId, @itemName, @quantity,
                        @unitPrice, @totalPrice, @specialInstructions
                    )";

                using var insertCommand = new MySqlCommand(insertItemQuery, newConnection);
                insertCommand.Parameters.AddWithValue("@orderId", newOrderId);
                insertCommand.Parameters.AddWithValue("@cloudItemId", item.CloudItemId);
                insertCommand.Parameters.AddWithValue("@menuItemId", item.MenuItemId);
                insertCommand.Parameters.AddWithValue("@itemName", item.ItemName);
                insertCommand.Parameters.AddWithValue("@quantity", item.Quantity);
                insertCommand.Parameters.AddWithValue("@unitPrice", item.UnitPrice);
                insertCommand.Parameters.AddWithValue("@totalPrice", item.UnitPrice * item.Quantity);
                insertCommand.Parameters.AddWithValue("@specialInstructions", item.SpecialInstructions);

                await insertCommand.ExecuteNonQueryAsync();
            }

            System.Diagnostics.Debug.WriteLine($"✅ Migrated {items.Count} order items to online_order_items table");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Error migrating order items: {ex.Message}");
        }
    }

    private async Task MigrateSettingsAsync()
    {
        await CopyTableData("settings", "settings");
        System.Diagnostics.Debug.WriteLine("✅ Migrated settings table");
    }

    private async Task MigrateCloudConfigAsync()
    {
        await CopyTableData("cloud_config", "cloud_config");
        System.Diagnostics.Debug.WriteLine("✅ Migrated cloud_config table");
    }

    private async Task MigrateBusinessInfoAsync()
    {
        await CopyTableData("business_info", "business_info");
        System.Diagnostics.Debug.WriteLine("✅ Migrated business_info table");
    }

    private async Task CopyTableData(string sourceTable, string targetTable)
    {
        try
        {
            using var oldConnection = new MySqlConnection(_oldConnectionString);
            using var newConnection = new MySqlConnection(_newConnectionString);
            
            await oldConnection.OpenAsync();
            await newConnection.OpenAsync();

            // Get all data from source table
            var selectQuery = $"SELECT * FROM {sourceTable}";
            using var selectCommand = new MySqlCommand(selectQuery, oldConnection);
            using var adapter = new MySqlDataAdapter(selectCommand);
            var dataTable = new DataTable();
            adapter.Fill(dataTable);

            if (dataTable.Rows.Count == 0) return;

            // Build insert query for target table
            var columns = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
            var parameters = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}"));
            var insertQuery = $"INSERT INTO {targetTable} ({columns}) VALUES ({parameters})";

            foreach (DataRow row in dataTable.Rows)
            {
                using var insertCommand = new MySqlCommand(insertQuery, newConnection);
                foreach (DataColumn column in dataTable.Columns)
                {
                    insertCommand.Parameters.AddWithValue($"@{column.ColumnName}", row[column] ?? DBNull.Value);
                }
                await insertCommand.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Error copying {sourceTable}: {ex.Message}");
        }
    }

    private async Task CreateIndexesAsync()
    {
        using var connection = new MySqlConnection(_newConnectionString);
        await connection.OpenAsync();

        var indexes = new[]
        {
            // Performance indexes for orders
            "CREATE INDEX idx_online_orders_status ON online_orders(status)",
            "CREATE INDEX idx_online_orders_created_at ON online_orders(created_at)",
            "CREATE INDEX idx_online_orders_customer_phone ON online_orders(customer_phone)",
            "CREATE INDEX idx_local_orders_status ON local_orders(status)",
            "CREATE INDEX idx_local_orders_created_at ON local_orders(created_at)",
            
            // Inventory management indexes
            "CREATE INDEX idx_inventory_category ON inventory(category)",
            "CREATE INDEX idx_inventory_stock_level ON inventory(current_stock, minimum_stock)",
            
            // Menu items indexes
            "CREATE INDEX idx_menu_items_category ON menu_items(category)",
            "CREATE INDEX idx_menu_items_available ON menu_items(is_available)",
            
            // Customer management indexes
            "CREATE INDEX idx_customers_phone ON customers(phone)",
            "CREATE INDEX idx_customers_active ON customers(is_active)",
            
            // Shift management indexes
            "CREATE INDEX idx_shifts_date ON shifts(shift_date)",
            "CREATE INDEX idx_shifts_staff_date ON shifts(staff_id, shift_date)",
            
            // Printing queue indexes
            "CREATE INDEX idx_printing_queue_status ON printing_queue(status, priority)",
            
            // Sync log indexes
            "CREATE INDEX idx_sync_log_type_status ON sync_log(sync_type, status)"
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                using var command = new MySqlCommand(indexSql, connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Index creation warning: {ex.Message}");
            }
        }

        System.Diagnostics.Debug.WriteLine("✅ Created performance indexes");
    }

    private async Task CreateReportingViewsAsync()
    {
        using var connection = new MySqlConnection(_newConnectionString);
        await connection.OpenAsync();

        var createVatReportViewSql = @"
            CREATE OR REPLACE VIEW vat_report_by_date_order_type AS
            SELECT
                DATE(order_created_at) AS report_date,
                order_type,
                source,
                SUM(vat_amount) AS total_vat_amount,
                SUM(base_amount) AS total_net_amount,
                SUM(gross_amount) AS total_gross_amount,
                COUNT(DISTINCT order_id) AS order_count,
                COUNT(*) AS line_count
            FROM (
                SELECT
                    lo.id AS order_id,
                    lo.created_at AS order_created_at,
                    lo.order_type AS order_type,
                    'local' AS source,
                    loi.base_price AS base_amount,
                    loi.vat_amount AS vat_amount,
                    loi.total_price AS gross_amount
                FROM local_order_items loi
                INNER JOIN local_orders lo ON lo.id = loi.order_id

                UNION ALL

                SELECT
                    oo.id AS order_id,
                    oo.created_at AS order_created_at,
                    oo.order_type AS order_type,
                    'online' AS source,
                    ooi.base_price AS base_amount,
                    ooi.vat_amount AS vat_amount,
                    ooi.total_price AS gross_amount
                FROM online_order_items ooi
                INNER JOIN online_orders oo ON oo.id = ooi.order_id
            ) vat_lines
            GROUP BY DATE(order_created_at), order_type, source";

        try
        {
            using var viewCommand = new MySqlCommand(createVatReportViewSql, connection);
            await viewCommand.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine("✅ Created reporting view: vat_report_by_date_order_type");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Reporting view creation warning: {ex.Message}");
        }
    }

    private async Task InsertDefaultDataAsync()
    {
        using var connection = new MySqlConnection(_newConnectionString);
        await connection.OpenAsync();

        // Insert default menu categories and sample items
        var defaultMenuItems = @"
            INSERT IGNORE INTO menu_items (item_code, name, description, category, price, is_available) VALUES
            ('BURGER001', 'Classic Burger', 'Beef patty with lettuce, tomato, onion', 'Burgers', 12.99, TRUE),
            ('PIZZA001', 'Margherita Pizza', 'Fresh mozzarella, tomato sauce, basil', 'Pizza', 14.99, TRUE),
            ('DRINK001', 'Coca Cola', 'Classic cola drink', 'Beverages', 2.99, TRUE),
            ('FRIES001', 'French Fries', 'Golden crispy fries', 'Sides', 4.99, TRUE)";

        // Insert default inventory items
        var defaultInventory = @"
            INSERT IGNORE INTO inventory (item_code, item_name, category, unit, current_stock, minimum_stock, unit_cost) VALUES
            ('RAW001', 'Ground Beef', 'Meat', 'kg', 50.0, 10.0, 8.50),
            ('RAW002', 'Cheese', 'Dairy', 'kg', 20.0, 5.0, 12.00),
            ('RAW003', 'Tomatoes', 'Vegetables', 'kg', 30.0, 8.0, 3.50),
            ('RAW004', 'Potatoes', 'Vegetables', 'kg', 100.0, 25.0, 1.20)";

        // Insert default settings
        var defaultSettings = @"
            INSERT IGNORE INTO settings (setting_key, setting_value, category, description) VALUES
            ('restaurant_name', 'My Restaurant', 'business', 'Restaurant display name'),
            ('tax_rate', '8.5', 'business', 'Tax percentage rate'),
            ('service_charge', '0.0', 'business', 'Service charge percentage'),
            ('auto_print_kitchen', 'true', 'printing', 'Auto print kitchen orders'),
            ('auto_print_receipt', 'true', 'printing', 'Auto print customer receipts'),
            ('order_numbering', 'sequential', 'orders', 'Order number generation method')";

        try
        {
            using var menuCommand = new MySqlCommand(defaultMenuItems, connection);
            await menuCommand.ExecuteNonQueryAsync();

            using var inventoryCommand = new MySqlCommand(defaultInventory, connection);
            await inventoryCommand.ExecuteNonQueryAsync();

            using var settingsCommand = new MySqlCommand(defaultSettings, connection);
            await settingsCommand.ExecuteNonQueryAsync();

            System.Diagnostics.Debug.WriteLine("✅ Inserted default data (menu, inventory, settings)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Error inserting default data: {ex.Message}");
        }
    }

    /// <summary>
    /// Test new database connectivity
    /// </summary>
    public async Task<bool> TestNewDatabaseAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_newConnectionString);
            await connection.OpenAsync();

            var testQuery = "SELECT COUNT(*) as table_count FROM information_schema.tables WHERE table_schema = 'restaurant_local'";
            using var command = new MySqlCommand(testQuery, connection);
            var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());

            System.Diagnostics.Debug.WriteLine($"✅ New database test successful - {tableCount} tables found");
            return tableCount >= 10;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ New database test failed: {ex.Message}");
            return false;
        }
    }
}