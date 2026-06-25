using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public class DatabaseSchemaService
    {
        private readonly DatabaseService _databaseService;

        public DatabaseSchemaService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task InitializeWebOrderTablesAsync()
        {
            try
            {
                await CreateWebOrderPermissionsTable();
                await CreateUserSessionsTable();
                await CreateWebOrderAccessLogsTable();
                await CreatePrintConfigurationsTable();
                await CreatePrintJobsTable();
                await CreateWebOrdersTable();
                await CreateWebOrderItemsTable();
                await SeedDefaultPermissions();

                Console.WriteLine(" All web order database tables initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Database schema initialization failed: {ex.Message}");
                throw;
            }
        }

        private async Task CreateWebOrderPermissionsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS web_order_permissions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    role ENUM('Admin', 'Manager', 'Staff') NOT NULL,
                    permission_name VARCHAR(50) NOT NULL,
                    is_granted BOOLEAN DEFAULT FALSE,
                    granted_by INT,
                    granted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    revoked_at DATETIME NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_user_id (user_id),
                    INDEX idx_role (role),
                    INDEX idx_permission_name (permission_name),
                    UNIQUE KEY unique_user_permission (user_id, permission_name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreateUserSessionsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS user_sessions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    username VARCHAR(100) NOT NULL,
                    role ENUM('Admin', 'Manager', 'Staff') NOT NULL,
                    session_start DATETIME DEFAULT CURRENT_TIMESTAMP,
                    last_activity DATETIME DEFAULT CURRENT_TIMESTAMP,
                    ip_address VARCHAR(45),
                    user_agent TEXT,
                    is_active BOOLEAN DEFAULT TRUE,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_user_id (user_id),
                    INDEX idx_is_active (is_active),
                    INDEX idx_last_activity (last_activity)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreateWebOrderAccessLogsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS web_order_access_logs (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    username VARCHAR(100) NOT NULL,
                    permission_name VARCHAR(50) NOT NULL,
                    action_description TEXT,
                    ip_address VARCHAR(45),
                    user_agent TEXT,
                    access_granted BOOLEAN DEFAULT TRUE,
                    denial_reason VARCHAR(255) NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_user_id (user_id),
                    INDEX idx_permission_name (permission_name),
                    INDEX idx_created_at (created_at),
                    INDEX idx_access_granted (access_granted)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreatePrintConfigurationsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS print_configurations (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    printer_name VARCHAR(255) NOT NULL,
                    printer_type ENUM('Thermal', 'Network', 'USB', 'System') NOT NULL,
                    connection_string VARCHAR(500) NOT NULL,
                    ip_address VARCHAR(45) NULL,
                    port INT NULL,
                    is_default BOOLEAN DEFAULT FALSE,
                    is_enabled BOOLEAN DEFAULT TRUE,
                    paper_width_mm INT DEFAULT 80,
                    cut_type ENUM('Full', 'Partial', 'None') DEFAULT 'Full',
                    auto_print_web_orders BOOLEAN DEFAULT TRUE,
                    print_customer_copy BOOLEAN DEFAULT FALSE,
                    last_test_print DATETIME NULL,
                    last_error TEXT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_printer_type (printer_type),
                    INDEX idx_is_enabled (is_enabled),
                    INDEX idx_is_default (is_default)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreatePrintJobsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS print_jobs (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    job_type ENUM('KitchenTicket', 'CustomerReceipt', 'OrderSummary') NOT NULL,
                    order_id INT NOT NULL,
                    web_order_id VARCHAR(50) NULL,
                    printer_id INT NULL,
                    print_content TEXT NOT NULL,
                    priority INT DEFAULT 1,
                    status ENUM('Pending', 'Printing', 'Completed', 'Failed', 'Cancelled') DEFAULT 'Pending',
                    retry_count INT DEFAULT 0,
                    max_retries INT DEFAULT 3,
                    scheduled_for DATETIME DEFAULT CURRENT_TIMESTAMP,
                    started_at DATETIME NULL,
                    completed_at DATETIME NULL,
                    error_message TEXT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_status (status),
                    INDEX idx_scheduled_for (scheduled_for),
                    INDEX idx_order_id (order_id),
                    INDEX idx_web_order_id (web_order_id),
                    INDEX idx_printer_id (printer_id),
                    FOREIGN KEY (printer_id) REFERENCES print_configurations(id) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreateWebOrdersTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS web_orders (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    web_order_id VARCHAR(50) NOT NULL UNIQUE,
                    local_order_id INT NULL,
                    customer_name VARCHAR(255) NOT NULL,
                    customer_email VARCHAR(255) NULL,
                    customer_phone VARCHAR(50) NULL,
                    order_date DATETIME NOT NULL,
                    total_amount DECIMAL(10,2) NOT NULL,
                    status ENUM('New', 'InKitchen', 'Preparing', 'Ready', 'Completed', 'Cancelled') DEFAULT 'New',
                    payment_status ENUM('Pending', 'Paid', 'Failed', 'Refunded') DEFAULT 'Pending',
                    special_instructions TEXT NULL,
                    delivery_type ENUM('Pickup', 'Delivery') DEFAULT 'Pickup',
                    delivery_address TEXT NULL,
                    estimated_ready_time DATETIME NULL,
                    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    last_updated DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_web_order_id (web_order_id),
                    INDEX idx_local_order_id (local_order_id),
                    INDEX idx_status (status),
                    INDEX idx_order_date (order_date),
                    INDEX idx_customer_phone (customer_phone),
                    FOREIGN KEY (local_order_id) REFERENCES orders(id) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task CreateWebOrderItemsTable()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS web_order_items (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    web_order_id VARCHAR(50) NOT NULL,
                    item_name VARCHAR(255) NOT NULL,
                    quantity INT NOT NULL,
                    unit_price DECIMAL(10,2) NOT NULL,
                    total_price DECIMAL(10,2) NOT NULL,
                    special_instructions TEXT NULL,
                    category VARCHAR(100) NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_web_order_id (web_order_id),
                    INDEX idx_item_name (item_name),
                    FOREIGN KEY (web_order_id) REFERENCES web_orders(web_order_id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";

            await ExecuteNonQueryAsync(sql);
        }

        private async Task SeedDefaultPermissions()
        {
            // Get all users to set up default permissions
            var users = await GetAllUsersAsync();

            foreach (var user in users)
            {
                await SetupDefaultPermissionsForUser(user.Id, user.Role);
            }
        }

        private async Task SetupDefaultPermissionsForUser(int userId, string role)
        {
            var permissions = GetDefaultPermissionsForRole(role);

            foreach (var (permission, isGranted) in permissions)
            {
                var sql = @"
                    INSERT IGNORE INTO web_order_permissions 
                    (user_id, role, permission_name, is_granted, granted_by, granted_at) 
                    VALUES (@userId, @role, @permission, @isGranted, 1, NOW())
                ";

                await ExecuteNonQueryAsync(sql, new MySqlParameter[]
                {
                    new("@userId", userId),
                    new("@role", role),
                    new("@permission", permission),
                    new("@isGranted", isGranted)
                });
            }
        }

        private (string Permission, bool IsGranted)[] GetDefaultPermissionsForRole(string role)
        {
            return role switch
            {
                "Admin" => new[]
                {
                    ("ViewOrders", true),
                    ("ViewOrderDetails", true),
                    ("ManageOrders", true),
                    ("UpdateOrderStatus", true),
                    ("ProcessRefunds", true),
                    ("AccessReports", true),
                    ("ExportData", true),
                    ("ConfigureSettings", true),
                    ("ManageUsers", true),
                    ("ViewAuditLogs", true),
                    ("ManagePrinters", true)
                },
                "Manager" => new[]
                {
                    ("ViewOrders", true),
                    ("ViewOrderDetails", true),
                    ("ManageOrders", true),
                    ("UpdateOrderStatus", true),
                    ("ProcessRefunds", false),
                    ("AccessReports", true),
                    ("ExportData", false),
                    ("ConfigureSettings", false),
                    ("ManageUsers", false),
                    ("ViewAuditLogs", false),
                    ("ManagePrinters", true)
                },
                "Staff" => new[]
                {
                    ("ViewOrders", false),
                    ("ViewOrderDetails", false),
                    ("ManageOrders", false),
                    ("UpdateOrderStatus", false),
                    ("ProcessRefunds", false),
                    ("AccessReports", false),
                    ("ExportData", false),
                    ("ConfigureSettings", false),
                    ("ManageUsers", false),
                    ("ViewAuditLogs", false),
                    ("ManagePrinters", false)
                },
                _ => new (string, bool)[] { }
            };
        }

        private async Task<List<(int Id, string Role)>> GetAllUsersAsync()
        {
            var users = new List<(int Id, string Role)>();

            var sql = "SELECT id, role FROM users";
            
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add((reader.GetInt32("id"), reader.GetString("role")));
            }

            return users;
        }

        private async Task ExecuteNonQueryAsync(string sql, MySqlParameter[]? parameters = null)
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async Task CreateIndexesForPerformanceAsync()
        {
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_web_orders_customer_search ON web_orders(customer_name, customer_phone)",
                "CREATE INDEX IF NOT EXISTS idx_web_orders_date_status ON web_orders(order_date, status)",
                "CREATE INDEX IF NOT EXISTS idx_print_jobs_queue ON print_jobs(status, scheduled_for, priority)",
                "CREATE INDEX IF NOT EXISTS idx_access_logs_user_date ON web_order_access_logs(user_id, created_at)",
                "CREATE INDEX IF NOT EXISTS idx_sessions_active ON user_sessions(user_id, is_active, last_activity)"
            };

            foreach (var index in indexes)
            {
                try
                {
                    await ExecuteNonQueryAsync(index);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not create index - {ex.Message}");
                }
            }
        }

        public async Task VerifySchemaIntegrityAsync()
        {
            var tables = new[]
            {
                "web_order_permissions",
                "user_sessions", 
                "web_order_access_logs",
                "print_configurations",
                "print_jobs",
                "web_orders",
                "web_order_items"
            };

            foreach (var table in tables)
            {
                var exists = await TableExistsAsync(table);
                if (!exists)
                {
                    throw new Exception($"Required table '{table}' does not exist");
                }
                Console.WriteLine($" Table '{table}' verified");
            }

            Console.WriteLine(" All web order schema tables verified successfully");
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            var sql = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = DATABASE() 
                AND table_name = @tableName
            ";

            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tableName", tableName);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
    }
}