using System.Text.Json;
using System.Text;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using MyFirstMauiApp.Services;

namespace POS_in_NET.Services;

public class CloudOrderService
{
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
    private DateTime _lastSyncTime;
    private string? _lastModifiedHeader;
    private readonly object _pollingLock = new object();
    private OrderWebWebSocketService? _webSocketService;
    private string? _deviceId;
    
    // Live update event - Used by BOTH WebSocket AND aggressive polling for UI refresh
    public event Action? OnOrdersUpdated;
    
    // Public properties for status monitoring
    public bool IsPolling => _pollingTimer != null;
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
        var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
        if (!onlineMasterCheck.Allowed)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud polling skipped: {onlineMasterCheck.Reason}");
            return;
        }

        var config = await _databaseService.GetCloudConfigAsync();
        
        if (!config.ContainsKey("is_enabled") || config["is_enabled"] != "True")
        {
            System.Diagnostics.Debug.WriteLine("Cloud polling is disabled");
            return;
        }

        if (string.IsNullOrEmpty(config.GetValueOrDefault("tenant_slug")) || 
            string.IsNullOrEmpty(config.GetValueOrDefault("api_key")))
        {
            System.Diagnostics.Debug.WriteLine("Cloud configuration incomplete");
            return;
        }

        // Moderate polling: 15-second interval to balance responsiveness with server load
        const double POLLING_INTERVAL = 15.0; // 15 seconds - reasonable polling for backup sync
        
        // Stop any existing polling first
        StopPolling();
        
        System.Diagnostics.Debug.WriteLine($" Starting backup polling every {POLLING_INTERVAL} seconds");
        System.Diagnostics.Debug.WriteLine(" Polling runs in background AND triggers UI refresh when new orders found");
        System.Diagnostics.Debug.WriteLine(" Works with WebSocket for redundant order delivery");
        
        // Reset change detection
        _lastModifiedHeader = null;
        
        // Start immediate fetch, then continue with timer
        _ = Task.Run(async () => await PollForOrdersAsync());
        
        var intervalMs = (int)(POLLING_INTERVAL * 1000); // 3000ms = 3 seconds
        _pollingTimer = new Timer(async _ => await PollForOrdersAsync(), 
            null, TimeSpan.FromMilliseconds(intervalMs), TimeSpan.FromMilliseconds(intervalMs));
            
        System.Diagnostics.Debug.WriteLine(" Backup polling timer started successfully");
    }

    /// <summary>
    /// Stop polling for orders
    /// </summary>
    public void StopPolling()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isPolling = false;
        System.Diagnostics.Debug.WriteLine("Cloud order polling stopped");
    }

    /// <summary>
    /// Manually fetch orders from OrderWeb.net (for immediate sync with UI refresh)
    /// </summary>
    public async Task<(int NewOrders, int TotalOrders, string Message)> FetchOrdersAsync()
    {
        System.Diagnostics.Debug.WriteLine(" Manual sync initiated by user - WILL trigger UI refresh");
        
        try
        {
            // Use SyncTodaysOrdersAsync instead of PollForOrdersAsync
            // This fetches ALL orders from today, not just new ones
            var syncResult = await SyncTodaysOrdersAsync();
            
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

            string endpoint = BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: 100);
            
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

            System.Diagnostics.Debug.WriteLine($" Aggressive polling check: {endpoint}");
            var apiStartTime = DateTime.Now;
            var response = await _orderWebApiClient.SendAsync(request);
            var apiDuration = (DateTime.Now - apiStartTime).TotalMilliseconds;
            
            System.Diagnostics.Debug.WriteLine($" API Response: Status={response.StatusCode}, Duration={apiDuration:F0}ms");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Polling response in {apiDuration:F0}ms | Content: {jsonContent.Length} chars");
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" API RESPONSE: {jsonContent}");
#endif
                
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
            if (method == "voucher" || method == "cash" || method == "card")
            {
                System.Diagnostics.Debug.WriteLine($" Using PaymentMethod: '{method}'");
                return method;
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
                
                // Default for online payments
                System.Diagnostics.Debug.WriteLine($" DEFAULTING to: cash (couldn't determine specific method)");
                return "cash";
            }
            
            System.Diagnostics.Debug.WriteLine($" Using PaymentMethod as-is: '{method}'");
            return method;
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
            
            // PAYMENT METHOD - Smart detection with multiple fallbacks
            PaymentMethod = DeterminePaymentMethod(cloudOrder),
            
            ScheduledTime = cloudOrder.ScheduledTime,
            SpecialInstructions = cloudOrder.SpecialInstructions,
            
            // Status and timing
            Status = OrderStatus.New,
            SyncStatus = Models.SyncStatus.Synced, // Already synced from cloud
            CreatedAt = cloudOrder.CreatedAt,
            UpdatedAt = DateTime.Now,
            KitchenTime = DateTime.Now, // Send to kitchen immediately
            
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

        return localOrder;
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
            PaymentStatus = order.PaymentStatus.ToString().ToLowerInvariant(),
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

            var endpoint = BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: 1);
            
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await client.GetAsync(endpoint);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, "Connection successful");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                // Check if this is OrderWeb.net responding with a database error
                if (responseContent.Contains("\"success\":false") && responseContent.Contains("\"error\""))
                {
                    // This means we connected successfully, but OrderWeb.net has an internal issue
                    return (true, "Connection successful (OrderWeb.net responded, but has internal database issue)");
                }
                return (false, $"Server error: {responseContent}");
            }
            else
            {
                return (false, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
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

        if (baseUrl.EndsWith("/pos/pull-orders", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/pull-orders".Length];
        }

        return baseUrl.TrimEnd('/');
    }

    private static string BuildPullOrdersEndpoint(string? apiBaseUrl, string tenantSlug, int limit = 100, string? since = null)
    {
        var baseUrl = NormalizeApiBaseUrl(apiBaseUrl, tenantSlug);
        var endpoint = $"{baseUrl}/pos/pull-orders?tenant={Uri.EscapeDataString(tenantSlug)}&status=confirmed&limit={limit}";
        if (!string.IsNullOrWhiteSpace(since))
        {
            endpoint += $"&since={Uri.EscapeDataString(since)}";
        }

        return endpoint;
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
    /// Sync orders from a specific date
    /// </summary>
    public async Task<(bool Success, int OrdersFound, string Message)> SyncOrdersByDateAsync(DateTime targetDate)
    {
        System.Diagnostics.Debug.WriteLine($" SYNC: Fetching all orders from {targetDate:yyyy-MM-dd}...");
        
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

            // Build endpoint with SINCE parameter (required by OrderWeb.net)
            // Convert local time to UTC for API (OrderWeb.net expects UTC)
            // Format: ISO 8601 (YYYY-MM-DDTHH:MM:SSZ) for API compatibility
            var targetDateUtc = targetDate.Kind == DateTimeKind.Utc ? targetDate : targetDate.ToUniversalTime();
            string sinceParam = targetDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string endpoint = BuildPullOrdersEndpoint(cloudUrl, tenantSlug, limit: 100, since: sinceParam);
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($" SYNCING ORDERS SINCE: {sinceParam}");
            System.Diagnostics.Debug.WriteLine($" Endpoint: {endpoint}");
            System.Diagnostics.Debug.WriteLine($" Restaurant: {tenantSlug}");
            System.Diagnostics.Debug.WriteLine($" API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...{apiKey.Substring(Math.Max(0, apiKey.Length - 4))}");
            System.Diagnostics.Debug.WriteLine($"⏰ Pulling CONFIRMED orders from {targetDate:MMM dd, yyyy} onwards (max 60 days)");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            // CRITICAL: Clear ALL headers first to avoid "multiple values" error
            _httpClient.DefaultRequestHeaders.Clear();
            
            // OrderWeb.net REST API uses Bearer token authentication
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

            System.Diagnostics.Debug.WriteLine($" Making sync request...");
            var response = await _httpClient.GetAsync(endpoint);
            
            System.Diagnostics.Debug.WriteLine($" API Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($" Response Headers: {response.Headers}");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine("========================================");
                System.Diagnostics.Debug.WriteLine($" API RESPONSE ({jsonContent.Length} chars):");
                System.Diagnostics.Debug.WriteLine($"First 500 chars: {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}");
                System.Diagnostics.Debug.WriteLine("========================================");
                
                var apiResponse = JsonSerializer.Deserialize<OrderWebApiResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                // DEBUG: Log API response structure
                System.Diagnostics.Debug.WriteLine($" API Response Parsed:");
                System.Diagnostics.Debug.WriteLine($"   Success: {apiResponse?.Success}");
                System.Diagnostics.Debug.WriteLine($"   Orders count: {apiResponse?.Orders?.Count ?? 0}");
                System.Diagnostics.Debug.WriteLine($"   PendingOrders count: {apiResponse?.PendingOrders?.Count ?? 0}");
                
                if (apiResponse?.Orders != null && apiResponse.Orders.Any())
                {
                    System.Diagnostics.Debug.WriteLine($" First order details:");
                    var firstOrder = apiResponse.Orders.First();
                    System.Diagnostics.Debug.WriteLine($"   ID: {firstOrder.Id}");
                    System.Diagnostics.Debug.WriteLine($"   OrderNumber: {firstOrder.OrderNumber}");
                    System.Diagnostics.Debug.WriteLine($"   Customer: {firstOrder.CustomerName}");
                    System.Diagnostics.Debug.WriteLine($"   CreatedAt: {firstOrder.CreatedAt}");
                    System.Diagnostics.Debug.WriteLine($"   Total: {firstOrder.TotalAmount}");
                }

                // Check both Orders (new API) and PendingOrders (old API) for compatibility
                var ordersToProcess = apiResponse?.Orders?.Any() == true ? apiResponse.Orders : apiResponse?.PendingOrders ?? new List<CloudOrderResponse>();
                
                System.Diagnostics.Debug.WriteLine($" Orders to process: {ordersToProcess.Count}");
                
                if (apiResponse?.Success == true && ordersToProcess.Any())
                {
                    System.Diagnostics.Debug.WriteLine($" Found {ordersToProcess.Count} orders from {targetDate:yyyy-MM-dd}");
                    
                    // Process all orders (will skip duplicates automatically)
                    int newOrdersCount = await ProcessNewOrdersAsync(ordersToProcess);
                    
                    System.Diagnostics.Debug.WriteLine($" SYNC COMPLETE: Processed {ordersToProcess.Count} orders, {newOrdersCount} were new");
                    
                    // Trigger UI refresh
                    OnOrdersUpdated?.Invoke();
                    
                    return (true, ordersToProcess.Count, $"Synced {ordersToProcess.Count} orders from {targetDate:MMM dd, yyyy} ({newOrdersCount} new)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($" No orders found for {targetDate:yyyy-MM-dd}");
                    return (true, 0, $"No orders found for {targetDate:MMM dd, yyyy}");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($" Sync failed: {response.StatusCode} - {errorContent}");
                return (false, 0, $"API error: {response.StatusCode}");
            }
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
            var cloudUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            
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
                System.Diagnostics.Debug.WriteLine($" CATCH-UP API RESPONSE: {jsonContent}");
                
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
        if (_ackRetryTimer != null)
        {
            _ackRetryTimer.Dispose();
        }
        
        System.Diagnostics.Debug.WriteLine(" ACK retry service started");
        
        // Run every 60 seconds
        _ackRetryTimer = new Timer(
            async _ => await RetryPendingAcksAsync(),
            null,
            TimeSpan.FromSeconds(30), // Start after 30 seconds
            TimeSpan.FromSeconds(60)  // Repeat every 60 seconds
        );
    }
    
    /// <summary>
    /// Stop ACK retry service
    /// </summary>
    public void StopAckRetryService()
    {
        _ackRetryTimer?.Dispose();
        _ackRetryTimer = null;
        System.Diagnostics.Debug.WriteLine("⏸ ACK retry service stopped");
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
            PaymentMethod = dto.Payment?.Method ?? "card",
            PaymentStatus = dto.Payment?.Status?.ToLower() == "paid" ? PaymentStatus.Paid : PaymentStatus.Pending,
            SpecialInstructions = dto.SpecialInstructions,
            ScheduledTime = DateTime.TryParse(dto.ScheduledFor, out var scheduledTime) ? scheduledTime : null,
            
            Status = OrderStatus.New,
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
