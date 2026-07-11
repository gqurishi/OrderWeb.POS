using System.Text.Json;
using System.Text;
using System.Net;
using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using MyFirstMauiApp.Services;

namespace POS_in_NET.Services;

public class CloudOrderService
{
    private const int DefaultOrderSyncLimit = 100;
    private const int BackfillOrderSyncLimit = 100;
    private const int MaxOrderSyncLimit = 100;
    private const int BackfillDays = 7;
    private const string OrderSettlementOperationType = "order_settlement";
    private static readonly bool EnableVerboseCloudPayloadLogging = false;

    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private readonly OrderService _orderService;
    private readonly ReceiptService _receiptService;
    private readonly OrderWebApiClient _orderWebApiClient;
    private OnlineOrderAutoPrintService? _autoPrintService;
    private Timer? _pollingTimer;
    private Timer? _ackRetryTimer;
    private readonly SemaphoreSlim _ackRetryGate = new(1, 1);
    private bool _isPolling = false;
    private bool _isBackupPollingEnabled = false;
    private bool _isAckRetryEnabled = false;
    private DateTime _lastSyncTime;
    private string? _lastModifiedHeader;
    private readonly object _pollingLock = new object();
    private OrderWebWebSocketService? _webSocketService;
    private string? _deviceId;
    
    // Live update event - Used by WebSocket and backup polling for UI refresh.
    public event Action? OnOrdersUpdated;
    
    // Public properties for status monitoring
    public bool IsPolling => _isBackupPollingEnabled;
    public bool IsAckRetryActive => _isAckRetryEnabled;
    public DateTime LastSyncTime => _lastSyncTime;
    
    public CloudOrderService(
        DatabaseService databaseService, 
        OrderService orderService,
        ReceiptService receiptService,
        OrderWebApiClient orderWebApiClient)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // Reasonable timeout for 15-second polling
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive"); // Keep connections alive
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        
        _databaseService = databaseService;
        _orderService = orderService;
        _receiptService = receiptService;
        _orderWebApiClient = orderWebApiClient;
        _lastSyncTime = DateTime.UtcNow.AddHours(-24); // Start from 24 hours ago
        
        System.Diagnostics.Debug.WriteLine("========================================");
        System.Diagnostics.Debug.WriteLine("CloudOrderService initialized");
        System.Diagnostics.Debug.WriteLine("Mode: DUAL DELIVERY (WebSocket + REST)");
        System.Diagnostics.Debug.WriteLine("Polling: Every 15 seconds (backup)");
        System.Diagnostics.Debug.WriteLine("Print ACK: Enabled");
        System.Diagnostics.Debug.WriteLine("ACK Retry: Every 60 seconds");
        System.Diagnostics.Debug.WriteLine("========================================");
    }

    /// <summary>
    /// Set the auto-print service for online orders (lazy injection to avoid circular dependency)
    /// </summary>
    public void SetAutoPrintService(OnlineOrderAutoPrintService autoPrintService)
    {
        _autoPrintService = autoPrintService;
        System.Diagnostics.Debug.WriteLine("CloudOrderService linked to OnlineOrderAutoPrintService");
    }
    
    /// <summary>
    /// Set the WebSocket service for status monitoring
    /// </summary>
    public void SetWebSocketService(OrderWebWebSocketService webSocketService)
    {
        _webSocketService = webSocketService;
        System.Diagnostics.Debug.WriteLine("CloudOrderService linked to WebSocket for status monitoring");
    }

    /// <summary>
    /// Start polling for orders based on cloud configuration
    /// </summary>
    public async Task StartPollingAsync()
    {
        var readiness = await EnsureBackupPollingReadyAsync();
        if (!readiness.Success)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud polling skipped: {readiness.Message}");
            return;
        }

        await PollForOrdersAsync();
    }

    /// <summary>
    /// Stop polling for orders
    /// </summary>
    public void StopPolling()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isBackupPollingEnabled = false;
        _isPolling = false;
        System.Diagnostics.Debug.WriteLine("Cloud order polling stopped");
    }

    public async Task<(bool Success, string Message)> RunBackupPollingOnceAsync()
    {
        var readiness = await EnsureBackupPollingReadyAsync();
        if (!readiness.Success)
        {
            return readiness;
        }

        await PollForOrdersAsync();
        return (true, "Backup order polling checked.");
    }

    private async Task<(bool Success, string Message)> EnsureBackupPollingReadyAsync()
    {
        var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
        if (!onlineMasterCheck.Allowed)
        {
            _isBackupPollingEnabled = false;
            return (false, onlineMasterCheck.Reason);
        }

        var config = await _databaseService.GetCloudConfigAsync();

        if (!config.ContainsKey("is_enabled") || config["is_enabled"] != "True")
        {
            _isBackupPollingEnabled = false;
            return (false, "Cloud polling is disabled.");
        }

        if (string.IsNullOrEmpty(config.GetValueOrDefault("tenant_slug")) ||
            string.IsNullOrEmpty(config.GetValueOrDefault("api_key")))
        {
            _isBackupPollingEnabled = false;
            return (false, "Cloud configuration incomplete.");
        }

        _pollingTimer?.Dispose();
        _pollingTimer = null;
        if (!_isBackupPollingEnabled)
        {
            _lastModifiedHeader = null;
            System.Diagnostics.Debug.WriteLine(" Backup polling enabled (managed by background sync)");
        }

        _isBackupPollingEnabled = true;
        return (true, "Backup polling ready.");
    }

    /// <summary>
    /// Manually fetch orders from OrderWeb.net (for immediate sync with UI refresh)
    /// </summary>
    public async Task<(int NewOrders, int TotalOrders, string Message)> FetchOrdersAsync()
    {
        System.Diagnostics.Debug.WriteLine(" Manual sync initiated by user - WILL trigger UI refresh");
        
        try
        {
            // Manual sync should backfill recent OrderWeb history, not just new live orders.
            var syncResult = await SyncLastSevenDaysAsync();
            
            if (!syncResult.Success)
            {
                return (0, 0, syncResult.Message);
            }
            
            // Get today's order count from OrderService
            var todayOrders = await _orderService.GetOrdersAsync();
            var todayCount = todayOrders.Count(o => o.CreatedAt.Date == DateTime.Today && o.SyncStatus == Models.SyncStatus.Synced);
            
            // MANUAL SYNC ALWAYS REFRESHES UI
            System.Diagnostics.Debug.WriteLine($" Manual sync complete - Found {syncResult.OrdersFound} orders from OrderWeb.net");
            OnOrdersUpdated?.Invoke();
            
            return (syncResult.OrdersFound, todayCount, syncResult.Message);
        }
        catch (Exception ex)
        {
            return (0, 0, $"Sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Poll OrderWeb.net for new orders with smart change detection
    /// </summary>
    private async Task PollForOrdersAsync()
    {
        var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
        if (!roleCheck.Allowed)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud order polling skipped: {roleCheck.Reason}");
            return;
        }

        // Prevent concurrent polling for consistency
        lock (_pollingLock)
        {
            if (_isPolling) return;
            _isPolling = true;
        }
        
        var pollStartTime = DateTime.Now;
        
        try
        {
            // Get configuration from database
            var sharedConfig = await _orderWebApiClient.GetConfigAsync();
            if (sharedConfig == null)
            {
                _isPolling = false;
                return;
            }

            var tenantSlug = sharedConfig.TenantSlug;
            var apiKey = sharedConfig.ApiKey;
            var cloudUrl = sharedConfig.ApiBaseUrl;
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                _isPolling = false;
                return;
            }

            string endpoint = BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: DefaultOrderSyncLimit, status: "confirmed");
            
            System.Diagnostics.Debug.WriteLine($" Backup polling check: {endpoint}");
            System.Diagnostics.Debug.WriteLine($"    Restaurant: {tenantSlug}");
            System.Diagnostics.Debug.WriteLine($"    API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...{apiKey.Substring(Math.Max(0, apiKey.Length - 4))}");
            
            // CRITICAL: Clear ALL headers first to avoid "multiple values" error
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            _orderWebApiClient.ApplyAuthHeaders(request, apiKey);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

            if (!string.IsNullOrEmpty(_lastModifiedHeader))
            {
                try
                {
                    request.Headers.TryAddWithoutValidation("If-Modified-Since", _lastModifiedHeader);
                }
                catch
                {
                    // Ignore if header can't be added
                }
            }

            System.Diagnostics.Debug.WriteLine($" Backup polling check: {endpoint}");
            var apiStartTime = DateTime.Now;
            var response = await _orderWebApiClient.SendAsync(request);
            var apiDuration = (DateTime.Now - apiStartTime).TotalMilliseconds;
            
            System.Diagnostics.Debug.WriteLine($" API Response: Status={response.StatusCode}, Duration={apiDuration:F0}ms");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Polling response in {apiDuration:F0}ms | Content: {jsonContent.Length} chars");
                if (EnableVerboseCloudPayloadLogging)
                {
                    System.Diagnostics.Debug.WriteLine($" API RESPONSE: {jsonContent}");
                }
                
                var parseStart = DateTime.Now;
                var apiResponse = JsonSerializer.Deserialize<OrderWebApiResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var parseDuration = (DateTime.Now - parseStart).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($" JSON parsed in {parseDuration:F0}ms");
                
                // DEBUG: Log API response structure
                System.Diagnostics.Debug.WriteLine($" API Response Details:");
                System.Diagnostics.Debug.WriteLine($"   Success: {apiResponse?.Success}");
                System.Diagnostics.Debug.WriteLine($"   Orders count: {apiResponse?.Orders?.Count ?? 0}");
                System.Diagnostics.Debug.WriteLine($"   PendingOrders count: {apiResponse?.PendingOrders?.Count ?? 0}");

                // Check both Orders (new API) and PendingOrders (old API) for compatibility
                var ordersToProcess = apiResponse?.Orders?.Any() == true ? apiResponse.Orders : apiResponse?.PendingOrders ?? new List<CloudOrderResponse>();
                
                System.Diagnostics.Debug.WriteLine($" Orders to process: {ordersToProcess.Count}");
                
                if (apiResponse?.Success == true && ordersToProcess.Any())
                {
                    var processStart = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine($" Found {ordersToProcess.Count} orders from API");
                    
                    // Process and save orders to database with UI refresh
                    int newOrdersCount = await ProcessNewOrdersAsync(ordersToProcess);
                    
                    var processDuration = (DateTime.Now - processStart).TotalMilliseconds;
                    var totalDuration = (DateTime.Now - pollStartTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($" Polling complete: Processing {processDuration:F0}ms | Total {totalDuration:F0}ms");
                    System.Diagnostics.Debug.WriteLine($" New orders saved: {newOrdersCount}, Total processed: {ordersToProcess.Count}");
                    
                    //  TRIGGER UI REFRESH IF NEW ORDERS WERE SAVED
                    if (newOrdersCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($" {newOrdersCount} NEW orders detected - triggering UI refresh!");
                        OnOrdersUpdated?.Invoke();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(" No new orders (all already existed in database)");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" No new orders from backend polling");
                }
                
                _lastSyncTime = DateTime.Now; // Update successful sync time
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Failed to poll orders: {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(" Polling timeout - network may be slow");
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($" Network error during polling: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error during polling: {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    /// <summary>
    /// Process new orders received from cloud
    /// Returns the count of NEW orders that were actually saved (excludes duplicates)
    /// </summary>
    private async Task<int> ProcessNewOrdersAsync(List<CloudOrderResponse> cloudOrders)
    {
        int newOrdersCount = 0;

        foreach (var cloudOrder in cloudOrders)
        {
            if (await ProcessIncomingCloudOrderAsync(cloudOrder, notifyUi: false))
            {
                newOrdersCount++;
            }
        }
        
        return newOrdersCount;
    }

    public async Task<(bool Success, string Message)> ProcessWebhookOrderAsync(JsonElement root)
    {
        if (!root.TryGetProperty("order", out var orderElement))
        {
            return (false, "Webhook order payload missing 'order' field.");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cloudOrder = JsonSerializer.Deserialize<CloudOrderResponse>(orderElement.GetRawText(), options);
        if (cloudOrder == null || string.IsNullOrWhiteSpace(cloudOrder.Id))
        {
            return (false, "Invalid order payload.");
        }

        var saved = await ProcessIncomingCloudOrderAsync(cloudOrder);
        return saved
            ? (true, $"Order {cloudOrder.OrderNumber} received.")
            : (true, $"Order {cloudOrder.OrderNumber} already on till.");
    }

    public async Task<bool> ProcessIncomingCloudOrderAsync(CloudOrderResponse cloudOrder, bool notifyUi = true)
    {
        var config = await _databaseService.GetCloudConfigAsync();
        var autoPrintEnabled = config.GetValueOrDefault("auto_print_enabled", "True") == "True";

        try
        {
            if (cloudOrder == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(cloudOrder.Id))
            {
                System.Diagnostics.Debug.WriteLine($" Ignoring cloud order with missing id: {cloudOrder.OrderNumber}");
                return false;
            }

            if (await OrderAlreadyExistsAsync(cloudOrder.Id))
            {
                System.Diagnostics.Debug.WriteLine($"Order {cloudOrder.OrderNumber} ({cloudOrder.Id}) already exists");

                if (autoPrintEnabled && !await OrderHasPrintJobsAsync(cloudOrder.Id))
                {
                    System.Diagnostics.Debug.WriteLine($" Existing web order {cloudOrder.OrderNumber} has no print jobs; queueing now");
                    _ = AutoPrintOrderAsync(cloudOrder);
                }

                return false;
            }

            var localOrder = await ConvertCloudOrderToLocalAsync(cloudOrder);
            var saveResult = await _orderService.SaveOrderAsync(localOrder);

            if (!saveResult.Success)
            {
                System.Diagnostics.Debug.WriteLine($" Failed to save cloud order {cloudOrder.OrderNumber}: {saveResult.Message}");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($" Created local order from cloud order {cloudOrder.OrderNumber} ({cloudOrder.Id})");

            _ = SendOrderReceivedAsync(cloudOrder.Id, "queued_for_print");

            if (autoPrintEnabled)
            {
                _ = AutoPrintOrderAsync(cloudOrder);
            }

            if (notifyUi)
            {
                OnOrdersUpdated?.Invoke();
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing cloud order {cloudOrder.OrderNumber}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> OrderHasPrintJobsAsync(string orderId)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(DISTINCT job_type)
                FROM network_print_queue
                WHERE order_id = @orderId
                  AND job_type IN ('online_receipt', 'takeaway_ticket')";
            command.Parameters.AddWithValue("@orderId", orderId);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count >= 2;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking print jobs for {orderId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if order already exists locally
    /// </summary>
    private async Task<bool> OrderAlreadyExistsAsync(string cloudOrderId)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM orders WHERE order_id = @orderId";
            command.Parameters.AddWithValue("@orderId", cloudOrderId);
            
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking if order exists: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Smart payment method detection with multiple fallbacks
    /// Checks multiple fields from OrderWeb.net to determine the correct payment method
    /// </summary>
    private string DeterminePaymentMethod(CloudOrderResponse cloudOrder)
    {
        // Log ALL payment-related fields from OrderWeb.net
        System.Diagnostics.Debug.WriteLine($" PAYMENT DEBUG for {cloudOrder.OrderNumber}:");
        System.Diagnostics.Debug.WriteLine($"   PaymentMethod: '{cloudOrder.PaymentMethod}'");
        System.Diagnostics.Debug.WriteLine($"   PaymentStatus: '{cloudOrder.PaymentStatus}'");
        System.Diagnostics.Debug.WriteLine($"   VoucherCode: '{cloudOrder.VoucherCode}'");
        
        // Priority 1: Check if voucher/gift card is used
        if (!string.IsNullOrWhiteSpace(cloudOrder.VoucherCode))
        {
            System.Diagnostics.Debug.WriteLine($" DETECTED: Gift Card (has voucher code: {cloudOrder.VoucherCode})");
            return "voucher";
        }
        
        // Priority 2: Use PaymentMethod if it exists and is not generic
        if (!string.IsNullOrWhiteSpace(cloudOrder.PaymentMethod))
        {
            var method = cloudOrder.PaymentMethod.ToLower().Trim();
            
            // If it's already a specific method, use it
            if (method == "voucher" || method == "cash" || method == "card" || method == "gift_card")
            {
                System.Diagnostics.Debug.WriteLine($" Using PaymentMethod: '{method}'");
                return OnlineOrderPaymentHelper.GetStorageMethod(method);
            }
            
            // If it's generic "online" or "online_payment", we need to be smarter
            if (method.Contains("online") || method.Contains("payment"))
            {
                System.Diagnostics.Debug.WriteLine($" Generic payment method detected: '{method}' - checking PaymentStatus...");
                
                // Check PaymentStatus for clues
                if (!string.IsNullOrWhiteSpace(cloudOrder.PaymentStatus))
                {
                    var status = cloudOrder.PaymentStatus.ToLower();
                    if (status == "paid")
                    {
                        // If paid online, it's likely card unless voucher is used
                        System.Diagnostics.Debug.WriteLine($" INFERRED: Card (paid online, no voucher)");
                        return "card";
                    }
                }
                
                // Default for online payments when OrderWeb does not expose the card processor name.
                System.Diagnostics.Debug.WriteLine($" DEFAULTING to: online payment (specific provider not supplied)");
                return "online";
            }
            
            System.Diagnostics.Debug.WriteLine($" Using PaymentMethod as-is: '{method}'");
            return OnlineOrderPaymentHelper.GetStorageMethod(method);
        }
        
        // Priority 3: Default fallback
        System.Diagnostics.Debug.WriteLine($" NO payment info found - defaulting to: cash");
        return "cash";
    }

    /// <summary>
    /// Convert cloud order format to local Order model
    /// </summary>
    private async Task<Order> ConvertCloudOrderToLocalAsync(CloudOrderResponse cloudOrder)
    {
        // Parse financial data
        decimal.TryParse(cloudOrder.Total, out var total);
        decimal.TryParse(cloudOrder.Subtotal, out var subtotal);
        decimal.TryParse(cloudOrder.DeliveryFee, out var deliveryFee);
        decimal.TryParse(cloudOrder.Tax, out var tax);
        
        var localOrder = new Order
        {
            // IDs and identification - Use Id (UUID) as the unique order identifier
            OrderId = cloudOrder.Id, // UUID from OrderWeb.net
            OrderNumber = cloudOrder.OrderNumber, // Display order number (e.g. KIT-3763)
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
            
            // PAYMENT METHOD - requested method on arrival; actual method replaces it when POS payment closes.
            PaymentMethod = DeterminePaymentMethod(cloudOrder),
            
            ScheduledTime = cloudOrder.ScheduledTime,
            SpecialInstructions = cloudOrder.SpecialInstructions,
            
            // Status and timing
            Status = OrderStatus.New,
            LocalLifecycleState = LocalLifecycleState.Active,
            IsOpen = true,
            SyncStatus = Models.SyncStatus.Synced, // Already synced from cloud
            CreatedAt = cloudOrder.CreatedAt,
            UpdatedAt = DateTime.Now,
            KitchenTime = DateTime.Now, // Send to kitchen immediately
            PaymentStatus = OnlineOrderPaymentHelper.ToPaymentStatus(cloudOrder.PaymentMethod, cloudOrder.PaymentStatus),
            
            // Initialize items list
            Items = new List<Models.OrderItem>()
        };

        // DEBUG: Log payment method value received from API
        System.Diagnostics.Debug.WriteLine($" Order {cloudOrder.OrderNumber} - PaymentMethod: '{cloudOrder.PaymentMethod}', PaymentStatus: '{cloudOrder.PaymentStatus}', VoucherCode: '{cloudOrder.VoucherCode}'");
        System.Diagnostics.Debug.WriteLine($" Final saved payment method: '{localOrder.PaymentMethod}'");

        // Convert order items with proper pricing and addons
        if (cloudOrder.Items != null)
        {
            foreach (var cloudItem in cloudOrder.Items)
            {
                var inferredVariant = string.IsNullOrWhiteSpace(cloudItem.VariantId) && string.IsNullOrWhiteSpace(cloudItem.VariantName)
                    ? await InferVariantAsync(cloudItem.MenuItemId, cloudItem.Price)
                    : null;

                var variantId = !string.IsNullOrWhiteSpace(cloudItem.VariantId) ? cloudItem.VariantId : inferredVariant?.Id;
                var variantName = !string.IsNullOrWhiteSpace(cloudItem.VariantName) ? cloudItem.VariantName : inferredVariant?.Name;
                var displayName = !string.IsNullOrWhiteSpace(cloudItem.DisplayName)
                    ? cloudItem.DisplayName
                    : BuildCloudDisplayName(cloudItem.Name, variantName);

                var localItem = new Models.OrderItem
                {
                    OrderId = cloudOrder.Id, // Use UUID, not OrderNumber
                    CloudItemId = cloudItem.Id,
                    MenuItemId = cloudItem.MenuItemId,
                    VariantId = variantId,
                    VariantName = variantName,
                    DisplayName = displayName,
                    ItemName = cloudItem.Name ?? "Unknown Item",
                    Quantity = cloudItem.Quantity,
                    ItemPrice = cloudItem.Price, // Now properly mapping price!
                    SpecialInstructions = cloudItem.SpecialInstructions,
                    Addons = new List<Models.OrderItemAddon>()
                };
                
                // Convert addons with proper pricing
                if (cloudItem.SelectedAddons != null)
                {
                    foreach (var cloudAddon in cloudItem.SelectedAddons)
                    {
                        var localAddon = new Models.OrderItemAddon
                        {
                            AddonId = cloudAddon.Id,
                            AddonName = cloudAddon.Name ?? "Unknown Addon",
                            AddonPrice = cloudAddon.Price, // Now properly mapping addon price!
                            Quantity = 1 // Default addon quantity
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

    private static async Task<MyFirstMauiApp.Models.FoodMenu.MenuItemVariant?> InferVariantAsync(string? menuItemId, decimal? price)
    {
        try
        {
            return await new MenuItemService().InferVariantByPriceAsync(menuItemId, price);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Variant inference warning: {ex.Message}");
            return null;
        }
    }

    private static string? BuildCloudDisplayName(string? itemName, string? variantName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(variantName)
            ? itemName
            : $"{itemName.Trim()} ({variantName.Trim()})";
    }

    /// <summary>
    /// Auto-print order if enabled
    /// </summary>
    /// <summary>
    /// Auto-print order receipt using OnlineOrderAutoPrintService for network printers
    /// Falls back to legacy ReceiptService if no Online/Takeaway printers configured
    /// </summary>
    private async Task AutoPrintOrderAsync(CloudOrderResponse cloudOrder)
    {
        try
        {
            if (_autoPrintService == null)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-print service not available for order {cloudOrder.OrderNumber}");
                return;
            }

            var result = await _autoPrintService.PrintOnlineOrderAsync(cloudOrder);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"Queued OrderWeb auto-print for {cloudOrder.OrderNumber} via network printers");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Network auto-print queue failed for {cloudOrder.OrderNumber}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error auto-printing receipt for order {cloudOrder.OrderNumber}: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> QueueWebOrderPrintAsync(Order order)
    {
        if (_autoPrintService == null)
        {
            return (false, "Auto-print service is not available");
        }

        try
        {
            var cloudOrder = ConvertLocalOrderToCloud(order);
            var result = await _autoPrintService.PrintOnlineOrderAsync(cloudOrder);
            return result.Success
                ? (true, "Receipt and kitchen ticket queued")
                : (false, result.ErrorMessage ?? "Print queue failed");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static CloudOrderResponse ConvertLocalOrderToCloud(Order order)
    {
        return new CloudOrderResponse
        {
            Id = !string.IsNullOrWhiteSpace(order.CloudOrderId) ? order.CloudOrderId! : order.OrderId,
            OrderNumber = order.OrderNumber ?? order.OrderId,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            CustomerEmail = order.CustomerEmail,
            Address = order.CustomerAddress,
            Total = order.TotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            Subtotal = order.SubtotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            DeliveryFee = order.DeliveryFee.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            Tax = order.TaxAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            OrderType = order.OrderType,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.LocalLifecycleState == LocalLifecycleState.Paid
                || order.PaidAt.HasValue
                || !OnlineOrderPaymentHelper.IsDeferredPaymentMethod(order.PaymentMethod)
                    ? "paid"
                    : "pending",
            SpecialInstructions = order.SpecialInstructions,
            ScheduledTime = order.ScheduledTime,
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(item => new CloudOrderItem
            {
                Id = item.CloudItemId ?? item.Id,
                MenuItemId = item.MenuItemId,
                VariantId = item.VariantId,
                VariantName = item.VariantName,
                DisplayName = item.DisplayName,
                Name = item.ItemName,
                Quantity = item.Quantity,
                Price = item.ItemPrice ?? 0m,
                SpecialInstructions = item.SpecialInstructions,
                SelectedAddons = item.Addons.Select(addon => new CloudOrderAddon
                {
                    Id = addon.AddonId,
                    Name = addon.AddonName,
                    Price = addon.AddonPrice ?? 0m
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>
    /// Send confirmation to cloud that order was received
    /// </summary>
    private async Task ConfirmOrderReceivedAsync(string cloudOrderId)
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var cloudUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api/pos/pull-orders");
            
            // Correct OrderWeb.net confirm endpoint structure
            var endpoint = $"{cloudUrl.TrimEnd('/')}/{tenantSlug}/orders/{cloudOrderId}/confirm";
            
            _httpClient.DefaultRequestHeaders.Clear();
            // OrderWeb.net REST API uses X-API-Key header, not Bearer token
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var confirmationData = new
            {
                receivedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                posSystemId = "POS-in-NET",
                status = "received"
            };

            var json = JsonSerializer.Serialize(confirmationData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Confirmed order {cloudOrderId} with cloud");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Failed to confirm order {cloudOrderId}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error confirming order {cloudOrderId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Test connection to cloud API with specific parameters
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string cloudUrl, string tenantSlug, string apiKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cloudUrl) || string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(apiKey))
                return (false, "Missing required parameters");

            var endpoint = BuildIntegrationEndpoint(cloudUrl, tenantSlug);
            
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            var response = await client.GetAsync(endpoint);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, "Connection successful");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                return (false, BuildPullOrdersError(response.StatusCode, responseContent));
            }
            else
            {
                return (false, BuildPullOrdersError(response.StatusCode, responseContent));
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timeout - check URL and network");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Test connection to cloud API
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var apiBaseUrl = config.GetValueOrDefault("api_base_url", "");
            var cloudUrl = !string.IsNullOrWhiteSpace(apiBaseUrl)
                ? apiBaseUrl
                : config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
            var result = await TestConnectionAsync(cloudUrl, tenantSlug, apiKey);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeApiBaseUrl(string? apiBaseUrl, string tenantSlug)
    {
        var baseUrl = (apiBaseUrl ?? "https://orderweb.net/api").Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(tenantSlug) && baseUrl.EndsWith($"/{tenantSlug}", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^(tenantSlug.Length + 1)];
        }

        return OrderWebApiClient.NormalizeApiBaseUrl(baseUrl);
    }

    private static string BuildPullOrdersEndpoint(
        string? apiBaseUrl,
        string tenantSlug,
        int limit = DefaultOrderSyncLimit,
        string? since = null,
        string? status = null)
    {
        var baseUrl = NormalizeApiBaseUrl(apiBaseUrl, tenantSlug);
        var endpoint = $"{baseUrl}/pos/pull-orders?tenant={Uri.EscapeDataString(tenantSlug)}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            endpoint += $"&status={Uri.EscapeDataString(status)}";
        }

        if (!string.IsNullOrWhiteSpace(since))
        {
            endpoint += $"&since={Uri.EscapeDataString(since)}";
        }

        return endpoint;
    }

    private static string BuildIntegrationEndpoint(string? apiBaseUrl, string tenantSlug)
    {
        var baseUrl = NormalizeApiBaseUrl(apiBaseUrl, tenantSlug);
        return $"{baseUrl}/pos/integration?tenant={Uri.EscapeDataString(tenantSlug)}";
    }

    private async Task<OrderPullResult> PullOrdersFromOrderWebAsync(string endpoint, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        _orderWebApiClient.ApplyAuthHeaders(request, apiKey);
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        System.Diagnostics.Debug.WriteLine($" Making sync request: {endpoint}");
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        System.Diagnostics.Debug.WriteLine($" API Response Status: {response.StatusCode}");
        System.Diagnostics.Debug.WriteLine($" Response Headers: {response.Headers}");

        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($" Sync failed: {response.StatusCode} - {content}");
            return OrderPullResult.Failed(response.StatusCode, BuildPullOrdersError(response.StatusCode, content));
        }

        if (EnableVerboseCloudPayloadLogging)
        {
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($" API RESPONSE ({content.Length} chars):");
            System.Diagnostics.Debug.WriteLine($"First 500 chars: {content.Substring(0, Math.Min(500, content.Length))}");
            System.Diagnostics.Debug.WriteLine("========================================");
        }

        var apiResponse = JsonSerializer.Deserialize<OrderWebApiResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var orders = ExtractOrders(apiResponse);
        System.Diagnostics.Debug.WriteLine($" API Response Parsed:");
        System.Diagnostics.Debug.WriteLine($"   Success: {apiResponse?.Success}");
        System.Diagnostics.Debug.WriteLine($"   Orders count: {apiResponse?.Orders?.Count ?? 0}");
        System.Diagnostics.Debug.WriteLine($"   PendingOrders count: {apiResponse?.PendingOrders?.Count ?? 0}");
        System.Diagnostics.Debug.WriteLine($"   Orders to process: {orders.Count}");

        if (orders.Count > 0)
        {
            var firstOrder = orders.First();
            System.Diagnostics.Debug.WriteLine($" First order details:");
            System.Diagnostics.Debug.WriteLine($"   ID: {firstOrder.Id}");
            System.Diagnostics.Debug.WriteLine($"   OrderNumber: {firstOrder.OrderNumber}");
            System.Diagnostics.Debug.WriteLine($"   Customer: {firstOrder.CustomerName}");
            System.Diagnostics.Debug.WriteLine($"   CreatedAt: {firstOrder.CreatedAt}");
            System.Diagnostics.Debug.WriteLine($"   Total: {firstOrder.TotalAmount}");
        }

        if (apiResponse?.Success == false && orders.Count == 0)
        {
            var apiError = apiResponse.Error ?? apiResponse.Message ?? "OrderWeb returned success=false.";
            return OrderPullResult.Failed(InferPullErrorStatus(apiError), $"API error: {apiError}");
        }

        return OrderPullResult.Ok(orders);
    }

    private static List<CloudOrderResponse> ExtractOrders(OrderWebApiResponse? apiResponse)
    {
        if (apiResponse?.Orders?.Any() == true)
        {
            return apiResponse.Orders;
        }

        return apiResponse?.PendingOrders ?? new List<CloudOrderResponse>();
    }

    private static string BuildPullOrdersError(HttpStatusCode statusCode, string content)
    {
        var trimmed = (content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return $"API error: {statusCode}";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            var parts = new[]
                {
                    GetJsonString(root, "error"),
                    GetJsonString(root, "message"),
                    GetJsonString(root, "details")
                }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts.Count > 0)
            {
                return $"API error: {statusCode} - {string.Join(" - ", parts)}";
            }
        }
        catch
        {
            // Use the raw snippet below.
        }

        var snippet = trimmed.Length > 220 ? $"{trimmed[..220]}..." : trimmed;
        return $"API error: {statusCode} - {snippet}";
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ShouldRetryWithBroadPull(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.InternalServerError
            || statusCode == HttpStatusCode.BadRequest
            || statusCode == HttpStatusCode.UnprocessableEntity;
    }

    private static HttpStatusCode InferPullErrorStatus(string? apiError)
    {
        if (string.IsNullOrWhiteSpace(apiError))
        {
            return HttpStatusCode.OK;
        }

        if (apiError.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase)
            || apiError.Contains("internal server", StringComparison.OrdinalIgnoreCase))
        {
            return HttpStatusCode.InternalServerError;
        }

        if (apiError.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)
            || apiError.Contains("bad request", StringComparison.OrdinalIgnoreCase))
        {
            return HttpStatusCode.BadRequest;
        }

        if (apiError.Contains("Unprocessable", StringComparison.OrdinalIgnoreCase)
            || apiError.Contains("validation", StringComparison.OrdinalIgnoreCase))
        {
            return HttpStatusCode.UnprocessableEntity;
        }

        return HttpStatusCode.OK;
    }

    private sealed record OrderPullResult(
        bool Success,
        List<CloudOrderResponse> Orders,
        HttpStatusCode? StatusCode,
        string? Error)
    {
        public static OrderPullResult Ok(List<CloudOrderResponse> orders) => new(true, orders, null, null);

        public static OrderPullResult Failed(HttpStatusCode statusCode, string error) => new(false, new List<CloudOrderResponse>(), statusCode, error);
    }

    /// <summary>
    /// Perform initial catch-up sync on app startup
    /// Fetches ALL orders from today to catch any missed during app downtime
    /// </summary>
    public async Task<(bool Success, int OrdersFound, string Message)> SyncTodaysOrdersAsync()
    {
        return await SyncOrdersByDateAsync(DateTime.Today);
    }

    /// <summary>
    /// Backfill recent OrderWeb orders whenever the API connection is established or manually synced.
    /// Starts at local midnight seven days ago so no early-day orders are missed.
    /// </summary>
    public async Task<(bool Success, int OrdersFound, string Message)> SyncLastSevenDaysAsync()
    {
        var backfillStart = DateTime.Today.AddDays(-BackfillDays);
        return await SyncOrdersByDateAsync(
            backfillStart,
            BackfillOrderSyncLimit,
            $"last {BackfillDays} days");
    }
    
    /// <summary>
    /// Sync orders from a specific date
    /// </summary>
    public async Task<(bool Success, int OrdersFound, string Message)> SyncOrdersByDateAsync(
        DateTime targetDate,
        int limit = DefaultOrderSyncLimit,
        string? rangeLabel = null)
    {
        var syncRange = rangeLabel ?? targetDate.ToString("yyyy-MM-dd");
        System.Diagnostics.Debug.WriteLine($" SYNC: Fetching all orders from {syncRange}...");
        
        try
        {
            var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
            if (!onlineMasterCheck.Allowed)
            {
                return (false, 0, onlineMasterCheck.Reason);
            }

            // Get configuration
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var apiBaseUrl = config.GetValueOrDefault("api_base_url", "");
            var cloudUrl = !string.IsNullOrWhiteSpace(apiBaseUrl)
                ? apiBaseUrl
                : config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                return (false, 0, "Cloud configuration incomplete");
            }

            var requestLimit = Math.Clamp(limit, 1, MaxOrderSyncLimit);
            var localStart = DateTime.SpecifyKind(targetDate.Date, DateTimeKind.Local);
            var sinceParam = localStart.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var dateOnlySinceParam = targetDate.Date.ToString("yyyy-MM-dd");
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($" SYNCING ORDERS SINCE: {sinceParam}");
            System.Diagnostics.Debug.WriteLine($" Restaurant: {tenantSlug}");
            System.Diagnostics.Debug.WriteLine($" API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...{apiKey.Substring(Math.Max(0, apiKey.Length - 4))}");
            System.Diagnostics.Debug.WriteLine($" Pulling OrderWeb orders from {syncRange} onwards (limit {requestLimit})");
            System.Diagnostics.Debug.WriteLine("========================================");

            var pullAttempts = new List<(string Label, string Endpoint, bool Broad)>
            {
                (
                    "confirmed orders since UTC timestamp",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit, since: sinceParam, status: "confirmed"),
                    false
                ),
                (
                    "confirmed orders since trading date",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit, since: dateOnlySinceParam, status: "confirmed"),
                    false
                ),
                (
                    "orders since UTC timestamp",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit, since: sinceParam),
                    false
                ),
                (
                    "orders since trading date",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit, since: dateOnlySinceParam),
                    false
                ),
                (
                    "broad confirmed orders",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit, status: "confirmed"),
                    true
                ),
                (
                    "broad orders",
                    BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: requestLimit),
                    true
                )
            };

            var primaryAttempt = pullAttempts[0];
            System.Diagnostics.Debug.WriteLine($" Trying OrderWeb pull: {primaryAttempt.Label}");
            var pullResult = await PullOrdersFromOrderWebAsync(primaryAttempt.Endpoint, apiKey);
            var usedFallback = false;
            foreach (var attempt in pullAttempts.Skip(1))
            {
                if (pullResult.Success && pullResult.Orders.Count > 0)
                {
                    break;
                }

                if (!ShouldTryNextOrderPullAttempt(pullResult, attempt.Broad))
                {
                    break;
                }

                System.Diagnostics.Debug.WriteLine($" OrderWeb pull attempt returned {DescribeOrderPullResult(pullResult)}. Retrying {attempt.Label}: {attempt.Endpoint}");
                var fallbackResult = await PullOrdersFromOrderWebAsync(attempt.Endpoint, apiKey);
                if (fallbackResult.Success)
                {
                    usedFallback = true;
                    pullResult = fallbackResult;

                    if (fallbackResult.Orders.Count > 0 || !attempt.Broad)
                    {
                        break;
                    }
                }
                else if (!pullResult.Success)
                {
                    pullResult = fallbackResult;
                }
            }

            if (!pullResult.Success)
            {
                return (false, 0, pullResult.Error ?? "API error while pulling OrderWeb orders.");
            }

            var syncStartDate = targetDate.Date;
            var ordersToProcess = pullResult.Orders
                .Where(order => order.CreatedAt == default || order.CreatedAt.Date >= syncStartDate)
                .ToList();

            if (ordersToProcess.Any())
            {
                System.Diagnostics.Debug.WriteLine($" Found {ordersToProcess.Count} orders from {syncRange}");

                var newOrdersCount = await ProcessNewOrdersAsync(ordersToProcess);

                System.Diagnostics.Debug.WriteLine($" SYNC COMPLETE: Processed {ordersToProcess.Count} orders, {newOrdersCount} were new");

                OnOrdersUpdated?.Invoke();

                var fallbackNote = usedFallback ? " using fallback" : string.Empty;
                return (true, ordersToProcess.Count, $"Synced {ordersToProcess.Count} orders from {syncRange}{fallbackNote} ({newOrdersCount} new)");
            }

            System.Diagnostics.Debug.WriteLine($" No orders found for {syncRange}");
            return (true, 0, $"No orders found for {syncRange}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Sync exception: {ex.Message}");
            return (false, 0, $"Sync failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// DEPRECATED: Use SyncOrdersByDateAsync instead
    /// </summary>
    private async Task<(bool Success, int OrdersFound, string Message)> SyncTodaysOrdersAsyncOld()
    {
        System.Diagnostics.Debug.WriteLine(" CATCH-UP SYNC: Fetching all today's orders from OrderWeb.net...");
        
        try
        {
            // Get configuration
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var cloudUrl = config.GetValueOrDefault(
                "api_base_url",
                config.GetValueOrDefault("cloud_url", "https://orderweb.net/api"));
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                return (false, 0, "Cloud configuration incomplete");
            }

            // Use the REST API endpoint for pending orders with query parameters
            // This is FULLY DYNAMIC - automatically uses whatever Restaurant ID you enter in Settings
            // CRITICAL: Request ALL orders (no status filter) to ensure nothing is missed
            // Increased limit to 100 to catch more orders
            string endpoint = $"{cloudUrl}/pos/pull-orders?tenant={tenantSlug}&limit=100";
            
            System.Diagnostics.Debug.WriteLine($" Catch-up sync from: {endpoint}");
            System.Diagnostics.Debug.WriteLine($"    Restaurant: {tenantSlug}");
            
            // CRITICAL: Clear ALL headers first to avoid "multiple values" error
            _httpClient.DefaultRequestHeaders.Clear();
            
            // OrderWeb.net REST API uses Bearer token authentication (as per official documentation)
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

            System.Diagnostics.Debug.WriteLine($" Making catch-up sync request...");
            var response = await _httpClient.GetAsync(endpoint);
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                if (EnableVerboseCloudPayloadLogging)
                {
                    System.Diagnostics.Debug.WriteLine($" CATCH-UP API RESPONSE: {jsonContent}");
                }
                
                var apiResponse = JsonSerializer.Deserialize<OrderWebApiResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                // DEBUG: Log API response structure
                System.Diagnostics.Debug.WriteLine($" Catch-up API Response Details:");
                System.Diagnostics.Debug.WriteLine($"   Success: {apiResponse?.Success}");
                System.Diagnostics.Debug.WriteLine($"   Orders count: {apiResponse?.Orders?.Count ?? 0}");
                System.Diagnostics.Debug.WriteLine($"   PendingOrders count: {apiResponse?.PendingOrders?.Count ?? 0}");

                // Check both Orders (new API) and PendingOrders (old API) for compatibility
                var ordersToProcess = apiResponse?.Orders?.Any() == true ? apiResponse.Orders : apiResponse?.PendingOrders ?? new List<CloudOrderResponse>();
                
                System.Diagnostics.Debug.WriteLine($" Orders to process: {ordersToProcess.Count}");
                
                if (apiResponse?.Success == true && ordersToProcess.Any())
                {
                    System.Diagnostics.Debug.WriteLine($" Catch-up found {ordersToProcess.Count} orders from OrderWeb.net");
                    
                    // Process all orders (will skip duplicates automatically)
                    await ProcessNewOrdersAsync(ordersToProcess);
                    
                    System.Diagnostics.Debug.WriteLine($" CATCH-UP SYNC COMPLETE: Processed {ordersToProcess.Count} orders");
                    
                    // Trigger UI refresh for catch-up sync
                    OnOrdersUpdated?.Invoke();
                    
                    return (true, ordersToProcess.Count, $"Synced {ordersToProcess.Count} orders from today");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" Catch-up sync: No pending orders found");
                    return (true, 0, "No pending orders");
                }
            }
            else
            {
                var error = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                System.Diagnostics.Debug.WriteLine($" Catch-up sync failed: {error}");
                return (false, 0, error);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Catch-up sync error: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopPolling();
        StopAckRetryService();
        _httpClient?.Dispose();
    }
    
    // ==================== NEW: PRINT ACKNOWLEDGMENT SYSTEM ====================
    
    /// <summary>
    /// Get or generate unique device ID for this POS
    /// </summary>
    private async Task<string> GetDeviceIdAsync()
    {
        if (!string.IsNullOrEmpty(_deviceId))
            return _deviceId;

        _deviceId = await _orderWebApiClient.GetDeviceIdAsync();
        return _deviceId;
    }
    
    /// <summary>
    /// Send "Order Received" confirmation to OrderWeb.net
    /// Called immediately when order arrives (before printing)
    /// </summary>
    public async Task<bool> SendOrderReceivedAsync(string orderId, string status = "queued_for_print")
    {
        try
        {
            var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
            if (!roleCheck.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($" Cannot send Order Received: {roleCheck.Reason}");
                return false;
            }

            var config = await _orderWebApiClient.GetConfigAsync();
            if (config == null)
            {
                System.Diagnostics.Debug.WriteLine(" Cannot send Order Received: No configuration");
                return false;
            }

            var url = OrderWebApiClient.BuildUrl(config, "/pos/orders/received");
            var deviceId = await GetDeviceIdAsync();
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("order-received", orderId, status, deviceId);
            
            var payload = new
            {
                tenant = config.TenantSlug,
                order_id = orderId,
                received_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                device_id = deviceId,
                status = status,
                idempotency_key = idempotencyKey
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            
            _orderWebApiClient.ApplyAuthHeaders(request, config.ApiKey, idempotencyKey);

            System.Diagnostics.Debug.WriteLine($" Sending Order Received for {orderId}: {status}");

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($" Order Received sent for {orderId}");
                await LogOrderReceivedAsync(orderId, deviceId, status, true);
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Order Received failed: {response.StatusCode} - {errorBody}");
                await LogOrderReceivedAsync(orderId, deviceId, status, false);
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error sending Order Received: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Log order received confirmation to database
    /// </summary>
    private async Task LogOrderReceivedAsync(string orderId, string deviceId, string status, bool sentSuccessfully)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO order_received_log (order_id, received_at, device_id, status, sent_to_cloud) 
                VALUES (@orderId, @receivedAt, @deviceId, @status, @sent)";
            
            command.Parameters.AddWithValue("@orderId", orderId);
            command.Parameters.AddWithValue("@receivedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@sent", sentSuccessfully ? 1 : 0);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to log order received: {ex.Message}");
        }
    }

    private static bool ShouldTryNextOrderPullAttempt(OrderPullResult pullResult, bool nextAttemptIsBroad)
    {
        if (!pullResult.Success)
        {
            return ShouldRetryWithBroadPull(pullResult.StatusCode);
        }

        return pullResult.Orders.Count == 0 && !nextAttemptIsBroad;
    }

    private static string DescribeOrderPullResult(OrderPullResult pullResult)
    {
        return pullResult.Success
            ? $"success with {pullResult.Orders.Count} order(s)"
            : pullResult.Error ?? "API error";
    }

    public async Task<bool> SendOrderSettlementAsync(
        Order order,
        string status = "paid",
        string? staffId = null,
        string? staffName = null,
        string? notes = null)
    {
        if (!string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var orderWebOrderId = ResolveOrderWebOrderId(order);
        if (string.IsNullOrWhiteSpace(orderWebOrderId))
        {
            System.Diagnostics.Debug.WriteLine(" Cannot send settlement: missing OrderWeb order id");
            return false;
        }

        var normalizedStatus = NormalizeSettlementStatus(status);
        var paymentMethod = normalizedStatus == "paid"
            ? NormalizeSettlementPaymentMethod(order.PaymentMethod)
            : null;
        var deviceId = await GetDeviceIdAsync();
        var config = await _orderWebApiClient.GetConfigAsync();
        if (config == null)
        {
            System.Diagnostics.Debug.WriteLine(" Cannot send settlement: No OrderWeb configuration");
            return false;
        }

        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("order-settlement", orderWebOrderId, normalizedStatus);
        var paidAt = ToUtc(order.PaidAt ?? order.CompletedTime ?? DateTime.Now);
        var url = OrderWebApiClient.BuildUrl(config, "/pos/orders/ack");

        await RepairLegacyOrderSettlementQueueAsync(url);

        if (await HasSettlementBeenRecordedAsync(orderWebOrderId, idempotencyKey))
        {
            System.Diagnostics.Debug.WriteLine($" Settlement already recorded for OrderWeb order {orderWebOrderId}");
            return true;
        }

        var payload = BuildSettlementPayload(
            config.TenantSlug,
            order,
            orderWebOrderId,
            normalizedStatus,
            paymentMethod,
            paidAt,
            deviceId,
            idempotencyKey,
            staffId,
            staffName,
            notes);

        try
        {
            var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
            if (!roleCheck.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($" Settlement queued: {roleCheck.Reason}");
                return await QueueOrderSettlementAsync(
                    order,
                    orderWebOrderId,
                    normalizedStatus,
                    paymentMethod,
                    paidAt,
                    deviceId,
                    idempotencyKey,
                    payload,
                    config,
                    url,
                    roleCheck.Reason);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            _orderWebApiClient.ApplyAuthHeaders(request, config.ApiKey, idempotencyKey);

            System.Diagnostics.Debug.WriteLine($" Sending settlement for OrderWeb order {orderWebOrderId}: {normalizedStatus}/{paymentMethod ?? "none"}");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                await RecordOrderSettlementAsync(
                    order,
                    orderWebOrderId,
                    normalizedStatus,
                    paymentMethod,
                    paidAt,
                    deviceId,
                    idempotencyKey,
                    sentToCloud: true,
                    queuedForRetry: false,
                    responseStatus: (int)response.StatusCode,
                    responseBody: responseBody,
                    lastError: null);

                System.Diagnostics.Debug.WriteLine($" Settlement sent for OrderWeb order {orderWebOrderId}");
                return true;
            }

            var error = $"HTTP {(int)response.StatusCode}: {responseBody}";
            System.Diagnostics.Debug.WriteLine($" Settlement failed: {error}");
            return await QueueOrderSettlementAsync(
                order,
                orderWebOrderId,
                normalizedStatus,
                paymentMethod,
                paidAt,
                deviceId,
                idempotencyKey,
                payload,
                config,
                url,
                error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error sending settlement: {ex.Message}");
            return await QueueOrderSettlementAsync(
                order,
                orderWebOrderId,
                normalizedStatus,
                paymentMethod,
                paidAt,
                deviceId,
                idempotencyKey,
                payload,
                config,
                url,
                ex.Message);
        }
    }

    private async Task<bool> QueueOrderSettlementAsync(
        Order order,
        string orderWebOrderId,
        string status,
        string? paymentMethod,
        DateTime paidAtUtc,
        string deviceId,
        string idempotencyKey,
        Dictionary<string, object> payload,
        OrderWebApiConfig config,
        string url,
        string? lastError)
    {
        var queued = await _orderWebApiClient.EnqueueAsync(
            OrderSettlementOperationType,
            url,
            payload,
            config.ApiKey,
            idempotencyKey,
            priority: 1);

        await RecordOrderSettlementAsync(
            order,
            orderWebOrderId,
            status,
            paymentMethod,
            paidAtUtc,
            deviceId,
            idempotencyKey,
            sentToCloud: false,
            queuedForRetry: queued,
            responseStatus: null,
            responseBody: null,
            lastError: queued ? null : lastError);

        if (queued)
        {
            System.Diagnostics.Debug.WriteLine($" Settlement queued for OrderWeb order {orderWebOrderId}");
        }

        return queued;
    }

    private async Task RepairLegacyOrderSettlementQueueAsync(string ackUrl)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            var queuedItems = new List<(int Id, string Payload)>();

            using (var select = connection.CreateCommand())
            {
                select.CommandText = @"
                    SELECT id, payload
                    FROM offline_queue
                    WHERE operation_type = @operationType
                      AND status IN ('pending', 'processing', 'failed')
                      AND (
                          endpoint LIKE '%/pos/orders/settle'
                          OR payload LIKE '%""status"":""completed""%'
                          OR payload LIKE '%amount_paid%'
                          OR payload LIKE '%pos_payment_method%'
                          OR payload LIKE '%staff_name%'
                      )";
                select.Parameters.AddWithValue("@operationType", OrderSettlementOperationType);

                using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    queuedItems.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            var repaired = 0;
            foreach (var item in queuedItems)
            {
                var repairedPayload = BuildQueuedSettlementAckPayload(item.Payload);
                using var update = connection.CreateCommand();
                update.CommandText = @"
                    UPDATE offline_queue
                    SET endpoint = @ackUrl,
                        payload = @payload,
                        status = 'pending',
                        retry_count = 0,
                        last_error = NULL,
                        response_status = NULL,
                        response_body = NULL,
                        scheduled_at = NULL
                    WHERE id = @id";
                update.Parameters.AddWithValue("@ackUrl", ackUrl);
                update.Parameters.AddWithValue("@payload", repairedPayload);
                update.Parameters.AddWithValue("@id", item.Id);
                repaired += await update.ExecuteNonQueryAsync();
            }

            if (repaired > 0)
            {
                System.Diagnostics.Debug.WriteLine($" Repaired {repaired} legacy OrderWeb settlement queue item(s) to paid /pos/orders/ack.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Legacy settlement queue repair skipped: {ex.Message}");
        }
    }

    private static string BuildQueuedSettlementAckPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var status = NormalizeSettlementStatus(GetJsonString(root, "status"));
            var payload = new Dictionary<string, object>
            {
                ["tenant"] = GetJsonString(root, "tenant") ?? string.Empty,
                ["order_id"] = GetJsonString(root, "order_id") ?? string.Empty,
                ["status"] = status,
                ["device_id"] = GetJsonString(root, "device_id") ?? string.Empty,
                ["idempotency_key"] = GetJsonString(root, "idempotency_key") ?? string.Empty
            };

            if (status == "paid")
            {
                payload["payment_method"] = NormalizeSettlementPaymentMethod(
                    GetJsonString(root, "payment_method")
                    ?? GetJsonString(root, "pos_payment_method"));
                payload["paid_at"] = GetJsonString(root, "paid_at")
                    ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            else
            {
                var reason = GetJsonString(root, "reason") ?? GetJsonString(root, "notes");
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    payload["reason"] = reason;
                }
            }

            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return payloadJson;
        }
    }

    private async Task<bool> HasSettlementBeenRecordedAsync(string orderWebOrderId, string idempotencyKey)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM orderweb_order_settlements
                WHERE (idempotency_key = @idempotencyKey OR (cloud_order_id = @cloudOrderId AND sent_to_cloud = 1))
                  AND (sent_to_cloud = 1 OR queued_for_retry = 1)";
            command.Parameters.AddWithValue("@cloudOrderId", orderWebOrderId);
            command.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Settlement guard unavailable: {ex.Message}");
            return false;
        }
    }

    private async Task RecordOrderSettlementAsync(
        Order order,
        string orderWebOrderId,
        string status,
        string? paymentMethod,
        DateTime paidAtUtc,
        string deviceId,
        string idempotencyKey,
        bool sentToCloud,
        bool queuedForRetry,
        int? responseStatus,
        string? responseBody,
        string? lastError)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            await EnsureOrderSettlementSchemaAsync(connection);
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO orderweb_order_settlements
                    (local_order_id, cloud_order_id, order_number, status, payment_method,
                     amount_paid, paid_at, fulfillment, device_id, idempotency_key,
                     sent_to_cloud, queued_for_retry, last_attempt_at, sent_at,
                     response_status, response_body, last_error)
                VALUES
                    (@localOrderId, @cloudOrderId, @orderNumber, @status, @paymentMethod,
                     @amountPaid, @paidAt, @fulfillment, @deviceId, @idempotencyKey,
                     @sentToCloud, @queuedForRetry, @lastAttemptAt, @sentAt,
                     @responseStatus, @responseBody, @lastError)
                ON DUPLICATE KEY UPDATE
                    local_order_id = VALUES(local_order_id),
                    order_number = COALESCE(VALUES(order_number), order_number),
                    status = VALUES(status),
                    payment_method = COALESCE(VALUES(payment_method), payment_method),
                    amount_paid = VALUES(amount_paid),
                    paid_at = VALUES(paid_at),
                    fulfillment = VALUES(fulfillment),
                    device_id = VALUES(device_id),
                    idempotency_key = VALUES(idempotency_key),
                    sent_to_cloud = CASE WHEN VALUES(sent_to_cloud) = 1 THEN 1 ELSE sent_to_cloud END,
                    queued_for_retry = CASE WHEN VALUES(queued_for_retry) = 1 THEN 1 ELSE queued_for_retry END,
                    last_attempt_at = VALUES(last_attempt_at),
                    sent_at = COALESCE(VALUES(sent_at), sent_at),
                    response_status = COALESCE(VALUES(response_status), response_status),
                    response_body = COALESCE(VALUES(response_body), response_body),
                    last_error = VALUES(last_error),
                    updated_at = CURRENT_TIMESTAMP";

            command.Parameters.AddWithValue("@localOrderId", order.OrderId);
            command.Parameters.AddWithValue("@cloudOrderId", orderWebOrderId);
            command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(order.OrderNumber) ? (object)DBNull.Value : order.OrderNumber);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@paymentMethod", string.IsNullOrWhiteSpace(paymentMethod) ? (object)DBNull.Value : paymentMethod);
            command.Parameters.AddWithValue("@amountPaid", status == "paid" ? order.TotalAmount : (object)DBNull.Value);
            command.Parameters.AddWithValue("@paidAt", status == "paid" ? paidAtUtc : (object)DBNull.Value);
            command.Parameters.AddWithValue("@fulfillment", NormalizeSettlementFulfillment(order.OrderType) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
            command.Parameters.AddWithValue("@sentToCloud", sentToCloud);
            command.Parameters.AddWithValue("@queuedForRetry", queuedForRetry);
            command.Parameters.AddWithValue("@lastAttemptAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("@sentAt", sentToCloud ? DateTime.UtcNow : (object)DBNull.Value);
            command.Parameters.AddWithValue("@responseStatus", responseStatus ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@responseBody", string.IsNullOrWhiteSpace(responseBody) ? (object)DBNull.Value : responseBody);
            command.Parameters.AddWithValue("@lastError", string.IsNullOrWhiteSpace(lastError) ? (object)DBNull.Value : lastError);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to record settlement: {ex.Message}");
        }
    }

    private static async Task EnsureOrderSettlementSchemaAsync(MySqlConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                ALTER TABLE orderweb_order_settlements
                MODIFY COLUMN status ENUM('paid', 'completed', 'cancelled', 'no_show') NOT NULL DEFAULT 'paid',
                MODIFY COLUMN payment_method ENUM('cash', 'card', 'gift_card', 'voucher', 'gift') NULL";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" OrderWeb settlement schema check skipped: {ex.Message}");
        }
    }

    private static Dictionary<string, object> BuildSettlementPayload(
        string tenant,
        Order order,
        string orderWebOrderId,
        string status,
        string? paymentMethod,
        DateTime paidAtUtc,
        string deviceId,
        string idempotencyKey,
        string? staffId,
        string? staffName,
        string? notes)
    {
        var payload = new Dictionary<string, object>
        {
            ["tenant"] = tenant,
            ["order_id"] = orderWebOrderId,
            ["status"] = status,
            ["device_id"] = deviceId,
            ["idempotency_key"] = idempotencyKey
        };

        if (status == "paid")
        {
            payload["payment_method"] = paymentMethod ?? "cash";
            payload["paid_at"] = paidAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        if (status != "paid" && !string.IsNullOrWhiteSpace(notes))
        {
            payload["reason"] = notes;
        }

        return payload;
    }

    private static string ResolveOrderWebOrderId(Order order)
    {
        return !string.IsNullOrWhiteSpace(order.CloudOrderId)
            ? order.CloudOrderId.Trim()
            : order.OrderId.Trim();
    }

    private static string NormalizeSettlementStatus(string? status)
    {
        var normalized = (status ?? "paid").Trim().Replace("-", "_").ToLowerInvariant();
        return normalized switch
        {
            "cancelled" or "canceled" => "cancelled",
            "no_show" or "noshow" => "no_show",
            _ => "paid"
        };
    }

    private static string NormalizeSettlementPaymentMethod(string? paymentMethod)
    {
        var normalized = OnlineOrderPaymentHelper.NormalizeMethod(paymentMethod);
        return normalized switch
        {
            "cash" => "cash",
            "gift_card" or "voucher" or "gift" => "gift_card",
            _ => "card"
        };
    }

    private static string? NormalizeSettlementFulfillment(string? orderType)
    {
        var normalized = (orderType ?? string.Empty).Trim().Replace("-", "_").ToLowerInvariant();
        return normalized switch
        {
            "delivery" or "del" => "delivery",
            "pickup" or "pick_up" or "collection" or "collect" or "col" or "takeaway" or "take_away" => "collection",
            _ => string.IsNullOrWhiteSpace(normalized) ? null : normalized
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }
    
    /// <summary>
    /// Send print acknowledgment to orderweb.net with enhanced tracking
    /// Called after successful or failed print
    /// </summary>
    public async Task<bool> SendPrintAcknowledgmentAsync(string orderId, string status, string? errorReason = null, int? printDurationMs = null, DateTime? printStartedAt = null, Dictionary<string, object>? printerInfo = null)
    {
        try
        {
            var roleCheck = await _orderWebApiClient.CanRunCloudJobsAsync();
            if (!roleCheck.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($" Cannot send ACK: {roleCheck.Reason}");
                return false;
            }

            var config = await _orderWebApiClient.GetConfigAsync();
            if (config == null)
            {
                System.Diagnostics.Debug.WriteLine(" Cannot send ACK: No configuration");
                return false;
            }

            var url = OrderWebApiClient.BuildUrl(config, "/pos/orders/ack");
            var deviceId = await GetDeviceIdAsync();
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("order-print-ack", orderId, status, deviceId);
            
            // Build enhanced payload with optional fields
            var payload = new Dictionary<string, object>
            {
                ["tenant"] = config.TenantSlug,
                ["order_id"] = orderId,
                ["status"] = status,
                ["printed_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["device_id"] = deviceId,
                ["idempotency_key"] = idempotencyKey
            };
            
            if (!string.IsNullOrEmpty(errorReason))
                payload["reason"] = errorReason;
                
            if (printStartedAt.HasValue)
                payload["print_started_at"] = printStartedAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                
            if (printDurationMs.HasValue)
                payload["print_duration_ms"] = printDurationMs.Value;
                
            if (printerInfo != null && printerInfo.Count > 0)
                payload["printer_info"] = printerInfo;

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            
            _orderWebApiClient.ApplyAuthHeaders(request, config.ApiKey, idempotencyKey);

            System.Diagnostics.Debug.WriteLine($" Sending enhanced ACK for order {orderId}: {status}");

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($" ACK sent successfully for order {orderId}");
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" ACK failed: {response.StatusCode} - {errorBody}");
                
                // Queue for retry
                await QueueFailedAckAsync(orderId, status, errorReason);
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error sending ACK: {ex.Message}");
            
            // Queue for retry later
            await QueueFailedAckAsync(orderId, status, errorReason);
            return false;
        }
    }
    
    /// <summary>
    /// Queue failed ACK for retry
    /// </summary>
    private async Task QueueFailedAckAsync(string orderId, string status, string? reason)
    {
        try
        {
            var deviceId = await GetDeviceIdAsync();
            
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                INSERT INTO pending_acks (order_id, status, reason, printed_at, device_id, created_at) 
                VALUES (@orderId, @status, @reason, @printedAt, @deviceId, @createdAt)";
            
            command.Parameters.AddWithValue("@orderId", orderId);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@reason", reason ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@printedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
            
            await command.ExecuteNonQueryAsync();
            
            System.Diagnostics.Debug.WriteLine($" ACK queued for retry: Order {orderId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to queue ACK: {ex.Message}");
        }
    }
    
    // ==================== NEW: ACK RETRY SERVICE ====================
    
    /// <summary>
    /// Start ACK retry service (runs every 60 seconds)
    /// </summary>
    public void StartAckRetryService()
    {
        _ackRetryTimer?.Dispose();
        _ackRetryTimer = null;
        _isAckRetryEnabled = true;
        System.Diagnostics.Debug.WriteLine(" ACK retry service enabled (managed by background sync)");
    }
    
    /// <summary>
    /// Stop ACK retry service
    /// </summary>
    public void StopAckRetryService()
    {
        _ackRetryTimer?.Dispose();
        _ackRetryTimer = null;
        _isAckRetryEnabled = false;
        System.Diagnostics.Debug.WriteLine("⏸ ACK retry service stopped");
    }
    
    public async Task<(bool Success, string Message)> RetryPendingAcksOnceAsync()
    {
        StartAckRetryService();
        await RetryPendingAcksAsync();
        return (true, "Pending ACK retry checked.");
    }

    /// <summary>
    /// Retry sending pending acknowledgments
    /// </summary>
    private async Task RetryPendingAcksAsync()
    {
        if (!await _ackRetryGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            // Get pending ACKs from last 6 hours
            var sixHoursAgo = DateTime.UtcNow.AddHours(-6);
            
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = @"
                SELECT id, order_id, status, reason, printed_at, device_id, created_at, retry_count 
                FROM pending_acks 
                WHERE created_at > @sixHoursAgo AND retry_count < 10 
                ORDER BY created_at ASC 
                LIMIT 50";
            
            command.Parameters.AddWithValue("@sixHoursAgo", sixHoursAgo);
            
            var pendingAcks = new List<Models.Api.PendingAck>();
            
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    pendingAcks.Add(new Models.Api.PendingAck
                    {
                        Id = reader.GetInt32(0),
                        OrderId = reader.GetString(1),
                        Status = reader.GetString(2),
                        Reason = reader.IsDBNull(3) ? null : reader.GetString(3),
                        PrintedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        DeviceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6),
                        RetryCount = reader.GetInt32(7)
                    });
                }
            }

            if (pendingAcks.Count == 0) return;
            
            System.Diagnostics.Debug.WriteLine($" Retrying {pendingAcks.Count} pending ACK(s)");

            foreach (var ack in pendingAcks)
            {
                var success = await RetryAckAsync(ack);
                
                using var updateCmd = connection.CreateCommand();
                
                if (success)
                {
                    // ACK sent successfully - remove from queue
                    updateCmd.CommandText = "DELETE FROM pending_acks WHERE id = @id";
                    updateCmd.Parameters.AddWithValue("@id", ack.Id);
                    await updateCmd.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($" ACK retry successful: Order {ack.OrderId}");
                }
                else
                {
                    // Still failed - increment retry count
                    updateCmd.CommandText = @"
                        UPDATE pending_acks 
                        SET retry_count = retry_count + 1, last_retry_at = @now 
                        WHERE id = @id";
                    updateCmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    updateCmd.Parameters.AddWithValue("@id", ack.Id);
                    await updateCmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" ACK retry error: {ex.Message}");
        }
        finally
        {
            _ackRetryGate.Release();
        }
    }
    
    /// <summary>
    /// Retry sending a single ACK
    /// </summary>
    private async Task<bool> RetryAckAsync(Models.Api.PendingAck ack)
    {
        try
        {
            var config = await _orderWebApiClient.GetConfigAsync();
            if (config == null)
                return false;

            var url = OrderWebApiClient.BuildUrl(config, "/pos/orders/ack");
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("order-print-ack", ack.OrderId, ack.Status, ack.DeviceId);
            
            var payload = new
            {
                tenant = config.TenantSlug,
                order_id = ack.OrderId,
                status = ack.Status,
                printed_at = (ack.PrintedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                device_id = ack.DeviceId,
                reason = ack.Reason,
                idempotency_key = idempotencyKey
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            
            _orderWebApiClient.ApplyAuthHeaders(request, config.ApiKey, idempotencyKey);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    // ==================== NEW: ENHANCED POLLING WITH PULL-ORDERS ENDPOINT ====================
    
    /// <summary>
    /// Process polled order from new pull-orders endpoint
    /// Handles both new orders and status updates
    /// </summary>
    private async Task ProcessPolledOrderAsync(Models.Api.PullOrderDto orderDto)
    {
        try
        {
            // Check if order already exists in local database
            var existingOrder = await GetOrderByCloudIdAsync(orderDto.OrderId.ToString());
            
            if (existingOrder == null)
            {
                // NEW ORDER - Save it
                System.Diagnostics.Debug.WriteLine($" New order from polling: {orderDto.OrderNumber}");
                
                var order = MapPullDtoToOrder(orderDto);
                await _orderService.SaveOrderAsync(order);
                
                // Trigger UI update
                OnOrdersUpdated?.Invoke();
                
                // Auto-print if enabled
                var config = await _databaseService.GetCloudConfigAsync();
                var autoPrintEnabled = config.GetValueOrDefault("auto_print_enabled", "True") == "True";
                
                if (autoPrintEnabled)
                {
                    _ = AutoPrintOrderAsync(new CloudOrderResponse { OrderNumber = orderDto.OrderId.ToString() });
                }
            }
            else
            {
                // Order exists - check if status changed
                var printStatusChanged = !string.IsNullOrEmpty(orderDto.PrintStatus) && 
                                        existingOrder.GetType().GetProperty("PrintStatus")?.GetValue(existingOrder)?.ToString() != orderDto.PrintStatus;
                
                if (printStatusChanged)
                {
                    System.Diagnostics.Debug.WriteLine($" Order status updated from cloud: {orderDto.OrderNumber}");
                    // Could update local status here if needed
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error processing polled order {orderDto?.OrderNumber}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get order by cloud ID
    /// </summary>
    private async Task<Order?> GetOrderByCloudIdAsync(string cloudOrderId)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM orders WHERE order_id = @orderId OR cloud_order_id = @orderId LIMIT 1";
            command.Parameters.AddWithValue("@orderId", cloudOrderId);
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // Simple check - order exists
                return new Order { OrderId = cloudOrderId };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Map PullOrderDto to Order model
    /// </summary>
    private Order MapPullDtoToOrder(Models.Api.PullOrderDto dto)
    {
        var order = new Order
        {
            OrderId = dto.OrderId.ToString(),
            OrderNumber = dto.OrderNumber ?? $"ORD-{dto.OrderId}",
            CloudOrderId = dto.OrderId.ToString(),
            CreatedAt = DateTime.TryParse(dto.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt) ? (createdAt.Kind == DateTimeKind.Utc ? createdAt.ToLocalTime() : createdAt) : DateTime.Now,
            UpdatedAt = DateTime.TryParse(dto.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var updatedAt) ? (updatedAt.Kind == DateTimeKind.Utc ? updatedAt.ToLocalTime() : updatedAt) : DateTime.Now,
            
            CustomerName = dto.Customer?.Name ?? "Online Customer",
            CustomerPhone = dto.Customer?.Phone ?? "",
            CustomerEmail = dto.Customer?.Email ?? "",
            CustomerAddress = dto.Customer?.Address ?? "",
            
            TotalAmount = (decimal)(dto.Payment?.Total ?? 0),
            SubtotalAmount = (decimal)(dto.Payment?.Subtotal ?? 0),
            TaxAmount = (decimal)(dto.Payment?.Tax ?? 0),
            
            OrderType = dto.OrderType ?? "online",
            SourceChannel = "web",
            PaymentMethod = OnlineOrderPaymentHelper.GetStorageMethod(dto.Payment?.Method ?? "card"),
            PaymentStatus = OnlineOrderPaymentHelper.ToPaymentStatus(dto.Payment?.Method, dto.Payment?.Status),
            SpecialInstructions = dto.SpecialInstructions,
            ScheduledTime = DateTime.TryParse(dto.ScheduledFor, out var scheduledTime) ? scheduledTime : null,
            
            Status = OrderStatus.New,
            LocalLifecycleState = LocalLifecycleState.Active,
            IsOpen = true,
            SyncStatus = Models.SyncStatus.Synced,
            KitchenTime = DateTime.Now,
            
            Items = new List<Models.OrderItem>()
        };

        // Convert order items
        if (dto.Items != null)
        {
            foreach (var item in dto.Items)
            {
                var orderItem = new Models.OrderItem
                {
                    OrderId = dto.OrderId.ToString(),
                    CloudItemId = item.Id,
                    ItemName = item.Name ?? "Unknown Item",
                    Quantity = item.Quantity,
                    ItemPrice = (decimal)item.Price,
                    SpecialInstructions = item.SpecialInstructions,
                    Addons = new List<OrderItemAddon>()
                };

                // Convert modifiers to addons
                if (item.Modifiers != null)
                {
                    foreach (var modifier in item.Modifiers)
                    {
                        orderItem.Addons.Add(new OrderItemAddon
                        {
                            AddonName = modifier.Name ?? "",
                            AddonPrice = (decimal)modifier.Price,
                            Quantity = 1
                        });
                    }
                }

                order.Items.Add(orderItem);
            }
        }

        ApplyOrderTotalFallback(order);
        return order;
    }
    
    // ==================== NEW: BATCH ACKNOWLEDGMENT ====================
    
    /// <summary>
    /// Send multiple acknowledgments in a single batch request
    /// More efficient than sending individually
    /// </summary>
    public async Task<bool> SendBatchAcknowledgmentsAsync(List<BatchAckItem> acknowledgments)
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var cloudUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                return false;
            }

            var url = $"{cloudUrl}/pos/orders/batch-ack";
            var deviceId = await GetDeviceIdAsync();
            
            var payload = new
            {
                tenant = tenantSlug,
                device_id = deviceId,
                acknowledgments = acknowledgments.Select(ack => new
                {
                    order_id = ack.OrderId,
                    status = ack.Status,
                    printed_at = ack.PrintedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    reason = ack.Reason
                }).ToList()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            System.Diagnostics.Debug.WriteLine($" Sending batch ACK: {acknowledgments.Count} items");

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($" Batch ACK sent successfully");
                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Batch ACK failed: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error sending batch ACK: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Manual retry of all pending ACKs (triggered by user button)
    /// </summary>
    public async Task<(int Success, int Failed)> ManualRetryAllPendingAcksAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine(" Manual retry of pending ACKs triggered");
            
            // Get all pending ACKs
            var pendingAcks = new List<BatchAckItem>();
            
            using (var connection = await _databaseService.GetConnectionAsync())
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT order_id, status, reason, printed_at 
                    FROM pending_acks 
                    WHERE retry_count < 10 
                    ORDER BY created_at ASC 
                    LIMIT 100";
                
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    pendingAcks.Add(new BatchAckItem
                    {
                        OrderId = reader.GetString(0),
                        Status = reader.GetString(1),
                        Reason = reader.IsDBNull(2) ? null : reader.GetString(2),
                        PrintedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3)
                    });
                }
            }
            
            if (pendingAcks.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Info: No pending ACKs to retry");
                return (0, 0);
            }
            
            System.Diagnostics.Debug.WriteLine($" Retrying {pendingAcks.Count} pending ACKs");
            
            // Send as batch
            var success = await SendBatchAcknowledgmentsAsync(pendingAcks);
            
            if (success)
            {
                // Clear pending ACKs from database
                using var connection = await _databaseService.GetConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM pending_acks WHERE retry_count < 10";
                var deleted = await command.ExecuteNonQueryAsync();
                
                System.Diagnostics.Debug.WriteLine($" Manual retry successful: {deleted} ACKs cleared");
                return (deleted, 0);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Manual retry failed");
                return (0, pendingAcks.Count);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Manual retry error: {ex.Message}");
            return (0, -1);
        }
    }
    
    // ==================== NEW: CONFIGURATION SYNC ====================
    
    /// <summary>
    /// Fetch POS configuration from OrderWeb.net
    /// Allows remote management of POS settings
    /// </summary>
    public async Task<Dictionary<string, string>?> FetchRemoteConfigurationAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            var cloudUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                return null;
            }

            var url = $"{OrderWebApiClient.NormalizeApiBaseUrl(cloudUrl)}/pos/config?tenant={tenantSlug}";
            var deviceId = await GetDeviceIdAsync();
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            _orderWebApiClient.ApplyAuthHeaders(request, apiKey);
            request.Headers.Add("X-Device-ID", deviceId);

            System.Diagnostics.Debug.WriteLine($" Fetching remote configuration");

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var remoteConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                System.Diagnostics.Debug.WriteLine($" Remote configuration fetched: {remoteConfig?.Count ?? 0} settings");
                
                // Apply remote configuration to local database
                if (remoteConfig != null)
                {
                    await ApplyRemoteConfigurationAsync(remoteConfig);
                }
                
                return remoteConfig;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Config fetch failed: {response.StatusCode}");
                return null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error fetching config: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Apply remote configuration to local database
    /// </summary>
    private async Task ApplyRemoteConfigurationAsync(Dictionary<string, string> remoteConfig)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            
            foreach (var kvp in remoteConfig)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO settings (setting_key, setting_value) 
                    VALUES (@key, @value) 
                    ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)";
                
                command.Parameters.AddWithValue("@key", kvp.Key);
                command.Parameters.AddWithValue("@value", kvp.Value);
                
                await command.ExecuteNonQueryAsync();
            }
            
            System.Diagnostics.Debug.WriteLine($" Applied {remoteConfig.Count} remote config settings");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error applying config: {ex.Message}");
        }
    }
}

/// <summary>
/// Batch acknowledgment item model
/// </summary>
public class BatchAckItem
{
    public string OrderId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime? PrintedAt { get; set; }
}
