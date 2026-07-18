using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;

namespace POS_in_NET.Services;

/// <summary>
/// Direct database connection to OrderWeb.net tenant database for INSTANT order delivery
/// This replaces API polling with real-time database access for zero-latency order delivery
/// </summary>
public class OrderWebDirectDatabaseService
{
    private static readonly TimeSpan DirectDatabasePollInterval = TimeSpan.FromSeconds(5);
    private readonly DatabaseService _localDatabase;
    private readonly OrderService _orderService;
    private readonly ReceiptService _receiptService;
    private string? _orderWebConnectionString;
    private Timer? _realTimeMonitorTimer;
    private DateTime _lastOrderCheck = DateTime.MinValue;
    private bool _isMonitoring = false;
    private readonly object _monitoringLock = new object();

    // Events for real-time notifications
    public event Action? OnNewOrdersDetected;

    // Public properties for monitoring
    public bool IsMonitoring => _realTimeMonitorTimer != null;
    public DateTime? LastSyncTime { get; private set; }
    public string ConnectionStatus { get; private set; } = "Not Connected";

    public OrderWebDirectDatabaseService(
        DatabaseService localDatabase,
        OrderService orderService,
        ReceiptService receiptService)
    {
        _localDatabase = localDatabase;
        _orderService = orderService;
        _receiptService = receiptService;
        
        System.Diagnostics.Debug.WriteLine(" OrderWebDirectDatabaseService initialized for INSTANT database access!");
    }

    /// <summary>
    /// Configure connection to OrderWeb.net tenant database
    /// </summary>
    public async Task<bool> ConfigureDatabaseConnectionAsync(
        string host, 
        string database, 
        string username, 
        string password, 
        int port = 3306)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" Configuring database connection...");
            System.Diagnostics.Debug.WriteLine($"   Host: {host}");
            System.Diagnostics.Debug.WriteLine($"   Database: {database}");
            System.Diagnostics.Debug.WriteLine($"   Username: {username}");
            System.Diagnostics.Debug.WriteLine($"   Port: {port}");
            
            // Handle special characters in password by using MySqlConnectionStringBuilder
            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Database = database,
                UserID = username,
                Password = password,
                Port = (uint)port,
                ConnectionTimeout = 5,      // 5 second timeout
                DefaultCommandTimeout = 5,  // 5 second command timeout
                AllowUserVariables = true,
                UseCompression = false
            };
            
            _orderWebConnectionString = builder.ConnectionString;
            
            // Test the connection with timeout
            var testResult = await TestOrderWebDatabaseConnectionAsync();
            if (testResult.Success)
            {
                // Save connection details to local database
                await SaveConnectionConfigAsync(host, database, username, password, port);
                ConnectionStatus = "Connected";
                System.Diagnostics.Debug.WriteLine(" Direct database connection configured successfully!");
                return true;
            }
            else
            {
                ConnectionStatus = $"Failed: {testResult.ErrorMessage}";
                System.Diagnostics.Debug.WriteLine($" Database connection failed: {testResult.ErrorMessage}");
                _orderWebConnectionString = null; // Clear failed connection
                return false;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($" Error configuring database connection: {ex.Message}");
            _orderWebConnectionString = null; // Clear failed connection
            return false;
        }
    }

    /// <summary>
    /// Configure connection using a full connection string (supports MySQL URLs and standard connection strings)
    /// </summary>
    public async Task<bool> ConfigureDatabaseConnectionFromStringAsync(string connectionString)
    {
        try
        {
            // Parse different connection string formats
            string finalConnectionString = "";
            
            if (connectionString.StartsWith("mysql://"))
            {
                // Parse MySQL URL format: mysql://username:password@host:port/database
                finalConnectionString = ParseMySqlUrl(connectionString);
            }
            else if (connectionString.Contains("Server=") || connectionString.Contains("Host="))
            {
                // Standard MySQL connection string format
                finalConnectionString = connectionString;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(" Unsupported connection string format");
                ConnectionStatus = "Unsupported connection string format";
                return false;
            }

            _orderWebConnectionString = finalConnectionString;
            
            System.Diagnostics.Debug.WriteLine($" Testing parsed connection string");
            
            // Test the connection
            var testResult = await TestOrderWebDatabaseConnectionAsync();
            if (testResult.Success)
            {
                ConnectionStatus = "Connected";
                System.Diagnostics.Debug.WriteLine(" Connection string configured successfully!");
                return true;
            }
            else
            {
                ConnectionStatus = $"Failed: {testResult.ErrorMessage}";
                System.Diagnostics.Debug.WriteLine($" Connection failed: {testResult.ErrorMessage}");
                return false;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($" Error configuring connection string: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parse MySQL URL format to standard connection string
    /// </summary>
    private string ParseMySqlUrl(string mysqlUrl)
    {
        try
        {
            // mysql://username:password@host:port/database
            var uri = new Uri(mysqlUrl);
            
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 3306;
            var database = uri.AbsolutePath.TrimStart('/');
            var username = uri.UserInfo.Split(':')[0];
            var password = uri.UserInfo.Contains(':') ? uri.UserInfo.Split(':', 2)[1] : "";

            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Database = database,
                UserID = username,
                Password = password,
                Port = (uint)port,
                ConnectionTimeout = 10,
                DefaultCommandTimeout = 30,
                AllowUserVariables = true,
                UseCompression = false
            };

            System.Diagnostics.Debug.WriteLine($" Parsed MySQL URL:");
            System.Diagnostics.Debug.WriteLine($"   Host: {host}");
            System.Diagnostics.Debug.WriteLine($"   Database: {database}");
            System.Diagnostics.Debug.WriteLine($"   Username: {username}");
            System.Diagnostics.Debug.WriteLine($"   Port: {port}");
            
            return builder.ConnectionString;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error parsing MySQL URL: {ex.Message}");
            throw new ArgumentException($"Invalid MySQL URL format: {ex.Message}");
        }
    }

    /// <summary>
    /// Start real-time order monitoring with backup database polling
    /// </summary>
    public async Task StartRealTimeMonitoringAsync()
    {
        if (string.IsNullOrEmpty(_orderWebConnectionString))
        {
            System.Diagnostics.Debug.WriteLine(" Cannot start monitoring - database connection not configured");
            return;
        }

        lock (_monitoringLock)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
        }

        // Stop any existing monitoring
        StopRealTimeMonitoring();

        // Reset monitoring state
        _lastOrderCheck = DateTime.UtcNow.AddMinutes(-5); // Check last 5 minutes initially

        System.Diagnostics.Debug.WriteLine($" Starting direct database monitoring ({DirectDatabasePollInterval.TotalSeconds:F0}s interval)");

        // Start immediate check, then backup monitoring.
        _ = Task.Run(async () => await MonitorForNewOrdersAsync());

        _realTimeMonitorTimer = new Timer(async _ => await MonitorForNewOrdersAsync(),
            null, DirectDatabasePollInterval, DirectDatabasePollInterval);

        ConnectionStatus = "Real-time monitoring active";
        System.Diagnostics.Debug.WriteLine(" Real-time order monitoring started successfully!");
    }

    /// <summary>
    /// Stop real-time order monitoring
    /// </summary>
    public void StopRealTimeMonitoring()
    {
        _realTimeMonitorTimer?.Dispose();
        _realTimeMonitorTimer = null;
        _isMonitoring = false;
        ConnectionStatus = "Monitoring stopped";
        System.Diagnostics.Debug.WriteLine(" Real-time order monitoring stopped");
    }

    /// <summary>
    /// Monitor OrderWeb.net database for new orders in real-time
    /// </summary>
    private async Task MonitorForNewOrdersAsync()
    {
        if (!_isMonitoring) return;

        lock (_monitoringLock)
        {
            if (!_isMonitoring) return;
        }

        try
        {
            var monitorStart = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($" REAL-TIME CHECK at {monitorStart:HH:mm:ss.fff}");

            // Get tenant configuration
            var config = await _localDatabase.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");

            if (string.IsNullOrEmpty(tenantSlug))
            {
                System.Diagnostics.Debug.WriteLine(" No tenant slug configured");
                return;
            }

            // Query OrderWeb.net database directly for new orders
            var newOrders = await GetNewOrdersFromOrderWebAsync(tenantSlug, _lastOrderCheck);

            if (newOrders.Any())
            {
                var processingStart = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($" INSTANT ORDERS DETECTED: {newOrders.Count} new orders!");

                // Process each order immediately
                foreach (var order in newOrders)
                {
                    await ProcessInstantOrderAsync(order);
                }

                // Update last check time to newest order
                _lastOrderCheck = newOrders.Max(o => o.CreatedAt);
                LastSyncTime = DateTime.Now;

                var processingDuration = (DateTime.Now - processingStart).TotalMilliseconds;
                var totalDuration = (DateTime.Now - monitorStart).TotalMilliseconds;
                
                System.Diagnostics.Debug.WriteLine($" INSTANT DELIVERY COMPLETE: Processing {processingDuration:F0}ms | Total {totalDuration:F0}ms");

                // Notify UI immediately
                OnNewOrdersDetected?.Invoke();
            }
            else
            {
                var checkDuration = (DateTime.Now - monitorStart).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($" No new orders - Check completed in {checkDuration:F0}ms");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error during real-time monitoring: {ex.Message}");
            ConnectionStatus = $"Monitoring error: {ex.Message}";
        }
    }

    /// <summary>
    /// Get new orders directly from OrderWeb.net database
    /// </summary>
    private async Task<List<CloudOrderResponse>> GetNewOrdersFromOrderWebAsync(string tenantSlug, DateTime since)
    {
        var orders = new List<CloudOrderResponse>();

        try
        {
            using var connection = new MySqlConnection(_orderWebConnectionString);
            await connection.OpenAsync();

            // Query for new orders with all related data in one optimized query
            var sql = @"
                SELECT 
                    o.id,
                    o.orderNumber,
                    o.customerName,
                    o.customerPhone,
                    o.customerEmail,
                    o.address as customerAddress,
                    o.total,
                    o.subtotal,
                    o.deliveryFee,
                    o.tax,
                    o.orderType,
                    o.paymentMethod,
                    o.paymentStatus,
                    o.voucherCode,
                    o.specialInstructions,
                    o.scheduledTime,
                    o.createdAt,
                    o.status,
                    oi.id as item_id,
                    oi.quantity,
                    oi.selectedAddons,
                    oi.specialInstructions as item_instructions,
                    mi.name as item_name,
                    mi.price as item_price,
                    t.slug as tenant_slug
                FROM orders o
                INNER JOIN tenants t ON o.tenant_id = t.id
                LEFT JOIN order_items oi ON o.id = oi.orderId
                LEFT JOIN menu_items mi ON oi.menuItemId = mi.id
                WHERE t.slug = @tenantSlug
                AND o.createdAt > @sinceTime
                AND o.status = 'confirmed'
                ORDER BY o.createdAt DESC, oi.id";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@tenantSlug", tenantSlug);
            command.Parameters.AddWithValue("@sinceTime", since);

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            var orderDict = new Dictionary<string, CloudOrderResponse>();

            while (await reader.ReadAsync())
            {
                var orderId = Convert.ToString(reader["id"]);

                // Create order if not exists
                if (!orderDict.ContainsKey(orderId))
                {
                    var order = new CloudOrderResponse
                    {
                        Id = orderId,
                        OrderNumber = reader["orderNumber"] == DBNull.Value ? "" : Convert.ToString(reader["orderNumber"]),
                        CustomerName = reader["customerName"] == DBNull.Value ? "" : Convert.ToString(reader["customerName"]),
                        CustomerPhone = reader["customerPhone"] == DBNull.Value ? "" : Convert.ToString(reader["customerPhone"]),
                        CustomerEmail = reader["customerEmail"] == DBNull.Value ? "" : Convert.ToString(reader["customerEmail"]),
                        Address = reader["customerAddress"] == DBNull.Value ? "" : Convert.ToString(reader["customerAddress"]),
                        Total = reader["total"] == DBNull.Value ? "0" : Convert.ToDecimal(reader["total"]).ToString("F2"),
                        Subtotal = reader["subtotal"] == DBNull.Value ? "0" : Convert.ToDecimal(reader["subtotal"]).ToString("F2"),
                        DeliveryFee = reader["deliveryFee"] == DBNull.Value ? "0" : Convert.ToDecimal(reader["deliveryFee"]).ToString("F2"),
                        Tax = reader["tax"] == DBNull.Value ? "0" : Convert.ToDecimal(reader["tax"]).ToString("F2"),
                        OrderType = reader["orderType"] == DBNull.Value ? "pickup" : Convert.ToString(reader["orderType"]),
                        PaymentMethod = reader["paymentMethod"] == DBNull.Value ? null : Convert.ToString(reader["paymentMethod"]),
                        PaymentStatus = reader["paymentStatus"] == DBNull.Value ? null : Convert.ToString(reader["paymentStatus"]),
                        VoucherCode = reader["voucherCode"] == DBNull.Value ? null : Convert.ToString(reader["voucherCode"]),
                        SpecialInstructions = reader["specialInstructions"] == DBNull.Value ? "" : Convert.ToString(reader["specialInstructions"]),
                        ScheduledTime = reader["scheduledTime"] == DBNull.Value ? null : Convert.ToDateTime(reader["scheduledTime"]),
                        CreatedAt = Convert.ToDateTime(reader["createdAt"]),
                        Items = new List<CloudOrderItem>()
                    };

                    // DEBUG: Log payment information from database
                    System.Diagnostics.Debug.WriteLine($" OrderWeb DB - Order {order.OrderNumber}:");
                    System.Diagnostics.Debug.WriteLine($"   PaymentMethod: '{order.PaymentMethod}'");
                    System.Diagnostics.Debug.WriteLine($"   PaymentStatus: '{order.PaymentStatus}'");
                    System.Diagnostics.Debug.WriteLine($"   VoucherCode: '{order.VoucherCode}'");

                    orderDict[orderId] = order;
                }

                // Add item if exists
                if (reader["item_id"] != DBNull.Value)
                {
                    var item = new CloudOrderItem
                    {
                        Id = reader["item_id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["item_id"]),
                        Name = reader["item_name"] == DBNull.Value ? "Unknown Item" : Convert.ToString(reader["item_name"]),
                        Price = reader["item_price"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["item_price"]),
                        Quantity = Convert.ToInt32(reader["quantity"]),
                        SpecialInstructions = reader["item_instructions"] == DBNull.Value ? "" : Convert.ToString(reader["item_instructions"]),
                        SelectedAddons = new List<CloudOrderAddon>()
                    };

                    // Parse addons from JSON if exists
                    if (reader["selectedAddons"] != DBNull.Value)
                    {
                        var addonsJson = Convert.ToString(reader["selectedAddons"]);
                        if (!string.IsNullOrEmpty(addonsJson))
                        {
                            try
                            {
                                var addons = JsonSerializer.Deserialize<List<CloudOrderAddon>>(addonsJson);
                                if (addons != null)
                                {
                                    item.SelectedAddons = addons;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error parsing addons JSON: {ex.Message}");
                            }
                        }
                    }

                    orderDict[orderId].Items.Add(item);
                }
            }

            orders = orderDict.Values.ToList();
            System.Diagnostics.Debug.WriteLine($" Retrieved {orders.Count} new orders directly from OrderWeb.net database");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error querying OrderWeb.net database: {ex.Message}");
            throw;
        }

        return orders;
    }

    /// <summary>
    /// Process order received instantly from database
    /// </summary>
    private async Task ProcessInstantOrderAsync(CloudOrderResponse order)
    {
        try
        {
            // Check if we already have this order locally
            if (await OrderExistsLocallyAsync(order.Id))
            {
                System.Diagnostics.Debug.WriteLine($" Order {order.OrderNumber} already exists - updating payment info");
                
                // Update existing order with latest payment information from OrderWeb.net
                await UpdateOrderPaymentInfoAsync(order);
                return;
            }
            
            // Convert to local order format using existing logic
            var localOrder = ConvertCloudOrderToLocal(order);

            // Save to local database
            var saveResult = await _orderService.SaveOrderAsync(localOrder);

            if (saveResult.Success)
            {
                System.Diagnostics.Debug.WriteLine($" INSTANT ORDER CREATED: {order.OrderNumber} - Ready for kitchen!");

                // Auto-print if enabled
                var config = await _localDatabase.GetCloudConfigAsync();
                var autoPrintEnabled = config.GetValueOrDefault("auto_print_enabled", "True") == "True";
                
                if (autoPrintEnabled)
                {
                    _ = Task.Run(async () => await _receiptService.PrintReceiptAsync(order));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error processing instant order {order.OrderNumber}: {ex.Message}");
        }
    }

    /// <summary>
    /// Update payment information for existing order
    /// </summary>
    private async Task UpdateOrderPaymentInfoAsync(CloudOrderResponse cloudOrder)
    {
        try
        {
            using var connection = await _localDatabase.GetConnectionAsync();
            using var command = connection.CreateCommand();

            command.CommandText = @"
                UPDATE orders 
                SET payment_method = @paymentMethod
                WHERE (cloud_order_id = @cloudOrderId OR order_number = @orderNumber)
                  AND LOWER(COALESCE(local_lifecycle_state, 'active')) NOT IN ('paid', 'voided')";

            command.Parameters.AddWithValue("@paymentMethod", OnlineOrderPaymentHelper.GetStorageMethod(cloudOrder.PaymentMethod ?? "cash"));
            command.Parameters.AddWithValue("@cloudOrderId", cloudOrder.Id);
            command.Parameters.AddWithValue("@orderNumber", cloudOrder.OrderNumber);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                System.Diagnostics.Debug.WriteLine($" Updated payment method for {cloudOrder.OrderNumber}: '{cloudOrder.PaymentMethod}'");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error updating payment info for {cloudOrder.OrderNumber}: {ex.Message}");
        }
    }

    /// <summary>
    /// Test connection to database and discover table structure
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> TestOrderWebDatabaseConnectionAsync()
    {
        if (string.IsNullOrEmpty(_orderWebConnectionString))
        {
            return (false, "Database connection not configured");
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($" Testing connection...");
            
            // Create connection with timeout using CancellationToken
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 5 second timeout
            
            using var connection = new MySqlConnection(_orderWebConnectionString);
            
            // Try to open connection with timeout
            try
            {
                await connection.OpenAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return (false, "Connection timeout - Cannot reach database server (Check host/port/firewall)");
            }

            System.Diagnostics.Debug.WriteLine(" Database connection established successfully!");

            // Discover available tables (with timeout)
            using var tablesCommand = new MySqlCommand("SHOW TABLES", connection);
            tablesCommand.CommandTimeout = 5; // 5 second timeout
            
            using var tablesReader = await tablesCommand.ExecuteReaderAsync(cts.Token);
            
            var tables = new List<string>();
            while (await tablesReader.ReadAsync(cts.Token))
            {
                tables.Add(tablesReader.GetString(0));
            }
            await tablesReader.CloseAsync();

            System.Diagnostics.Debug.WriteLine($" Available tables: {string.Join(", ", tables)}");

            // Look for order-related tables
            var orderTables = tables.Where(t => t.ToLower().Contains("order")).ToList();
            if (orderTables.Any())
            {
                System.Diagnostics.Debug.WriteLine($" Order-related tables found: {string.Join(", ", orderTables)}");
                
                // Test querying the first order table
                var firstOrderTable = orderTables.First();
                using var testCommand = new MySqlCommand($"SELECT COUNT(*) FROM `{firstOrderTable}` LIMIT 1", connection);
                testCommand.CommandTimeout = 5;
                var count = await testCommand.ExecuteScalarAsync(cts.Token);
                System.Diagnostics.Debug.WriteLine($" {firstOrderTable} contains {count} records");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(" No order-related tables found. Available tables listed above.");
            }

            return (true, $"Connection successful! Found {tables.Count} tables");
        }
        catch (MySqlException ex)
        {
            var errorMsg = ex.Number switch
            {
                1045 => "Access denied - Wrong username or password",
                1049 => "Unknown database - Database name doesn't exist",
                2003 => "Can't connect to server - Check host and port",
                _ => $"MySQL Error {ex.Number}: {ex.Message}"
            };
            System.Diagnostics.Debug.WriteLine($" {errorMsg}");
            return (false, errorMsg);
        }
        catch (OperationCanceledException)
        {
            var errorMsg = "Operation timeout - Connection took too long";
            System.Diagnostics.Debug.WriteLine($" {errorMsg}");
            return (false, errorMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Connection error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($" {errorMsg}");
            return (false, errorMsg);
        }
    }

    /// <summary>
    /// Check if order already exists in local database
    /// </summary>
    private async Task<bool> OrderExistsLocallyAsync(string orderWebOrderId)
    {
        try
        {
            using var connection = await _localDatabase.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM orders WHERE order_id = @orderId OR cloud_order_id = @orderId";
            command.Parameters.AddWithValue("@orderId", orderWebOrderId);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking if order exists locally: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Convert OrderWeb.net order to local order format
    /// </summary>
    private Order ConvertCloudOrderToLocal(CloudOrderResponse cloudOrder)
    {
        // Parse financial data
        decimal.TryParse(cloudOrder.Total, out var total);
        decimal.TryParse(cloudOrder.Subtotal, out var subtotal);
        decimal.TryParse(cloudOrder.DeliveryFee, out var deliveryFee);
        decimal.TryParse(cloudOrder.Tax, out var tax);

        var localOrder = new Order
        {
            // IDs and identification
            OrderId = cloudOrder.Id,
            OrderNumber = cloudOrder.OrderNumber,
            CloudOrderId = cloudOrder.Id,

            // Customer information
            CustomerName = cloudOrder.CustomerName ?? "Online Customer",
            CustomerPhone = cloudOrder.CustomerPhone,
            CustomerEmail = cloudOrder.CustomerEmail,
            CustomerAddress = cloudOrder.Address,

            // Financial information
            TotalAmount = total,
            SubtotalAmount = subtotal,
            DeliveryFee = deliveryFee,
            TaxAmount = tax,

            // Order details
            OrderType = cloudOrder.OrderType,
            SourceChannel = "web",
            PaymentMethod = OnlineOrderPaymentHelper.GetStorageMethod(cloudOrder.PaymentMethod),
            ScheduledTime = cloudOrder.ScheduledTime,
            SpecialInstructions = cloudOrder.SpecialInstructions,

            // Status and timing
            Status = OrderStatus.New,
            LocalLifecycleState = LocalLifecycleState.Active,
            IsOpen = true,
            SyncStatus = Models.SyncStatus.Synced,
            CreatedAt = cloudOrder.CreatedAt,
            UpdatedAt = DateTime.Now,
            KitchenTime = DateTime.Now,
            PaymentStatus = OnlineOrderPaymentHelper.ToPaymentStatus(cloudOrder.PaymentMethod, cloudOrder.PaymentStatus),

            // Initialize items list
            Items = new List<Models.OrderItem>()
        };

        // Convert order items
        if (cloudOrder.Items != null)
        {
            foreach (var cloudItem in cloudOrder.Items)
            {
                var localItem = new Models.OrderItem
                {
                    OrderId = cloudOrder.Id,
                    CloudItemId = cloudItem.Id,
                    ItemName = cloudItem.Name ?? "Unknown Item",
                    Quantity = cloudItem.Quantity,
                    ItemPrice = cloudItem.Price,
                    SpecialInstructions = cloudItem.SpecialInstructions,
                    Addons = new List<Models.OrderItemAddon>()
                };

                // Convert addons
                if (cloudItem.SelectedAddons != null)
                {
                    foreach (var cloudAddon in cloudItem.SelectedAddons)
                    {
                        var localAddon = new Models.OrderItemAddon
                        {
                            AddonId = cloudAddon.Id,
                            AddonName = cloudAddon.Name ?? "Unknown Addon",
                            AddonPrice = cloudAddon.Price,
                            Quantity = 1
                        };

                        localItem.Addons.Add(localAddon);
                    }
                }

                localOrder.Items.Add(localItem);
            }
        }

        ApplyOrderTotalFallback(localOrder);
        return localOrder;
    }

    private static void ApplyOrderTotalFallback(Order order)
    {
        var itemTotal = order.Items.Sum(item => item.TotalPrice);
        if (itemTotal <= 0)
        {
            return;
        }

        if (order.SubtotalAmount <= 0)
        {
            order.SubtotalAmount = itemTotal;
        }

        if (order.TotalAmount <= 0)
        {
            order.TotalAmount = itemTotal + Math.Max(0, order.DeliveryFee);
        }
    }

    /// <summary>
    /// Save database connection configuration
    /// </summary>
    private async Task SaveConnectionConfigAsync(string host, string database, string username, string password, int port)
    {
        try
        {
            using var connection = await _localDatabase.GetConnectionAsync();
            using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT OR REPLACE INTO cloud_config 
                (tenant_slug, api_key, cloud_url, is_enabled, polling_interval_seconds, auto_print_enabled, 
                 db_host, db_name, db_username, db_password, db_port, connection_type)
                VALUES 
                (@tenant, @api, @url, @enabled, @interval, @print, @host, @database, @username, @password, @port, 'direct_database')";

            var config = await _localDatabase.GetCloudConfigAsync();
            command.Parameters.AddWithValue("@tenant", config.GetValueOrDefault("tenant_slug", ""));
            command.Parameters.AddWithValue("@api", config.GetValueOrDefault("api_key", ""));
            command.Parameters.AddWithValue("@url", config.GetValueOrDefault("cloud_url", ""));
            command.Parameters.AddWithValue("@enabled", true);
            command.Parameters.AddWithValue("@interval", 1); // Not used with direct database
            command.Parameters.AddWithValue("@print", config.GetValueOrDefault("auto_print_enabled", "True") == "True");
            command.Parameters.AddWithValue("@host", host);
            command.Parameters.AddWithValue("@database", database);
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@password", password);
            command.Parameters.AddWithValue("@port", port);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving database connection config: {ex.Message}");
        }
    }

    /// <summary>
    /// Load database connection from configuration
    /// </summary>
    public async Task LoadConnectionFromConfigAsync()
    {
        try
        {
            var config = await _localDatabase.GetCloudConfigAsync();
            
            var host = config.GetValueOrDefault("db_host", "");
            var database = config.GetValueOrDefault("db_name", "");
            var username = config.GetValueOrDefault("db_username", "");
            var password = config.GetValueOrDefault("db_password", "");
            var portStr = config.GetValueOrDefault("db_port", "3306");

            if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(database) && 
                !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                int.TryParse(portStr, out var port);
                if (port == 0) port = 3306;

                await ConfigureDatabaseConnectionAsync(host, database, username, password, port);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading database connection config: {ex.Message}");
        }
    }

    /// <summary>
    /// Fix all existing orders by updating payment method from OrderWeb.net database
    /// This directly updates the local POS database with correct payment information
    /// </summary>
    public async Task<(int Updated, string Message)> FixAllOrderPaymentMethodsAsync()
    {
        int updatedCount = 0;
        try
        {
            System.Diagnostics.Debug.WriteLine(" Starting payment method fix for all orders...");

            // FIRST: Ensure payment_method column exists in orders table
            try
            {
                using var connection = await _localDatabase.GetConnectionAsync();
                using var checkCommand = connection.CreateCommand();
                
                // Check if column exists
                var schemaName = TerminalConfigurationService.GetActiveDatabaseName();
                checkCommand.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = @schemaName 
                    AND TABLE_NAME = 'orders' 
                    AND COLUMN_NAME = 'payment_method'";
                checkCommand.Parameters.AddWithValue("@schemaName", schemaName);
                
                var columnExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
                
                if (!columnExists)
                {
                    System.Diagnostics.Debug.WriteLine(" payment_method column doesn't exist - adding it now...");
                    
                    using var alterCommand = connection.CreateCommand();
                    alterCommand.CommandText = @"
                        ALTER TABLE orders 
                        ADD COLUMN payment_method VARCHAR(50) NULL AFTER order_type";
                    
                    await alterCommand.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine(" payment_method column added successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" payment_method column already exists");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error checking/adding payment_method column: {ex.Message}");
                return (0, $"Database schema error: {ex.Message}");
            }

            // USE API TO GET ORDERS (not direct database connection)
            System.Diagnostics.Debug.WriteLine(" Fetching orders from OrderWeb.net API...");
            
            // Get config
            var config = await _localDatabase.GetCloudConfigAsync();
            var apiKey = config.GetValueOrDefault("api_key", "");
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tenantSlug))
            {
                return (0, "API credentials not configured");
            }

            // Fetch orders from API
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Use the SAME endpoint that's working for regular sync
            var apiUrl = $"https://orderweb.net/api/{tenantSlug}";
            
            System.Diagnostics.Debug.WriteLine($" API Request: {apiUrl}");
            
            var response = await httpClient.GetAsync(apiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                return (0, $"API error: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            
            // DEBUG: Log first 500 characters of JSON to see structure
            System.Diagnostics.Debug.WriteLine($" API response received ({json.Length} characters)");
            
            // Try to deserialize - might fail if wrong structure
            List<CloudOrderResponse>? apiOrders = null;
            
            try
            {
                // Try direct array first
                apiOrders = System.Text.Json.JsonSerializer.Deserialize<List<CloudOrderResponse>>(json, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // Try wrapped in response object
                try
                {
                    var wrapper = System.Text.Json.JsonSerializer.Deserialize<OrderWebApiResponse>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    apiOrders = wrapper?.Orders ?? wrapper?.PendingOrders;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" JSON deserialization failed: {ex.Message}");
                    return (0, $"Failed to parse API response: {ex.Message}");
                }
            }

            if (apiOrders == null || apiOrders.Count == 0)
            {
                return (0, "No orders found from API");
            }

            System.Diagnostics.Debug.WriteLine($" Found {apiOrders.Count} orders from API");

            // DEBUG: Log payment info for first order
            if (apiOrders.Count > 0)
            {
                var firstOrder = apiOrders[0];
                System.Diagnostics.Debug.WriteLine($" First order debug:");
                System.Diagnostics.Debug.WriteLine($"   OrderNumber: {firstOrder.OrderNumber}");
                System.Diagnostics.Debug.WriteLine($"   PaymentMethod: '{firstOrder.PaymentMethod}'");
                System.Diagnostics.Debug.WriteLine($"   PaymentStatus: '{firstOrder.PaymentStatus}'");
                System.Diagnostics.Debug.WriteLine($"   VoucherCode: '{firstOrder.VoucherCode}'");
            }

            // Update each order in local database
            foreach (var apiOrder in apiOrders)
            {
                try
                {
                    // Log each order's payment info
                    System.Diagnostics.Debug.WriteLine($" Processing {apiOrder.OrderNumber}: PaymentMethod='{apiOrder.PaymentMethod}'");
                    
                    using var connection = await _localDatabase.GetConnectionAsync();
                    using var command = connection.CreateCommand();

                    command.CommandText = @"
                        UPDATE orders 
                        SET payment_method = @paymentMethod
                        WHERE order_number = @orderNumber";

                    command.Parameters.AddWithValue("@paymentMethod", apiOrder.PaymentMethod ?? "cash");
                    command.Parameters.AddWithValue("@orderNumber", apiOrder.OrderNumber);

                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        updatedCount++;
                        System.Diagnostics.Debug.WriteLine($" Fixed {apiOrder.OrderNumber}: {apiOrder.PaymentMethod}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($" No rows updated for {apiOrder.OrderNumber} (not in local database)");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" Error updating {apiOrder.OrderNumber}: {ex.Message}");
                }
            }

            var message = $"Updated {updatedCount} out of {apiOrders.Count} orders";
            System.Diagnostics.Debug.WriteLine($" {message}");
            return (updatedCount, message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error fixing payment methods: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            return (0, $"Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopRealTimeMonitoring();
    }
}
