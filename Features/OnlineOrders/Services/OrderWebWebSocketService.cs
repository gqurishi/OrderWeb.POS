using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MyFirstMauiApp.Services;

namespace POS_in_NET.Services;

/// <summary>
/// WebSocket service for real-time push notifications from OrderWeb.net
/// Handles: new orders, gift card updates, loyalty updates
/// </summary>
public class OrderWebWebSocketService
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private string _websocketUrl = "";
    private string _tenantId = "";
    private string _apiKey = "";
    private bool _isConnected = false;
    private readonly DatabaseService _databaseService;
    private readonly OrderService _orderService;
    private CloudOrderService? _cloudOrderService;
    private ReservationSyncService? _reservationSyncService;

    // Events for real-time notifications
    public event EventHandler<OrderReceivedEventArgs>? NewOrderReceived;
    public event EventHandler<GiftCardUpdatedEventArgs>? GiftCardUpdated;
    public event EventHandler<LoyaltyUpdatedEventArgs>? LoyaltyUpdated;
    public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

    public bool IsConnected => _isConnected;
    public DateTime? LastConnectionTime { get; private set; }
    public DateTime? LastMessageTime { get; private set; }
    public string ConnectionStatusText => GetConnectionStatusText();
    
    /// <summary>
    /// Get detailed connection status for debugging
    /// </summary>
    public string GetConnectionStatus()
    {
        if (_webSocket == null)
            return " WebSocket not initialized";
            
        return $"WebSocket State: {_webSocket.State}, IsConnected: {_isConnected}, URL: {_websocketUrl}, Tenant: {_tenantId}";
    }
    
    /// <summary>
    /// Get user-friendly connection status text
    /// </summary>
    private string GetConnectionStatusText()
    {
        if (_isConnected && _webSocket?.State == WebSocketState.Open)
            return " Live";
        else if (_webSocket?.State == WebSocketState.Connecting)
            return " Connecting...";
        else
            return " Offline";
    }

    public OrderWebWebSocketService(DatabaseService databaseService, OrderService orderService)
    {
        _databaseService = databaseService;
        _orderService = orderService;
        System.Diagnostics.Debug.WriteLine(" OrderWebWebSocketService created");
    }

    public void SetCloudOrderService(CloudOrderService cloudOrderService)
    {
        _cloudOrderService = cloudOrderService;
        System.Diagnostics.Debug.WriteLine(" WebSocket linked to CloudOrderService for shared order processing");
    }

    public void SetReservationSyncService(ReservationSyncService reservationSyncService)
    {
        _reservationSyncService = reservationSyncService;
        System.Diagnostics.Debug.WriteLine(" WebSocket linked to ReservationSyncService for live bookings");
    }

    /// <summary>
    /// Configure WebSocket connection settings
    /// </summary>
    public void Configure(string websocketUrl, string tenantId, string apiKey)
    {
        _websocketUrl = websocketUrl?.Trim() ?? "";
        _tenantId = tenantId?.Trim() ?? "";
        _apiKey = apiKey?.Trim() ?? "";
        
        System.Diagnostics.Debug.WriteLine($" WebSocket configured: {_websocketUrl}");
    }

    /// <summary>
    /// Connect to OrderWeb.net WebSocket server
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
            if (!onlineMasterCheck.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket connect skipped: {onlineMasterCheck.Reason}");
                return false;
            }

            System.Diagnostics.Debug.WriteLine(" ConnectAsync() called!");
            
            if (string.IsNullOrEmpty(_websocketUrl) || string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_apiKey))
            {
                System.Diagnostics.Debug.WriteLine($" WebSocket configuration missing:");
                System.Diagnostics.Debug.WriteLine($"   TenantID: '{_tenantId}'");
                return false;
            }

            // Disconnect if already connected
            if (_webSocket != null)
            {
                System.Diagnostics.Debug.WriteLine(" Disconnecting existing WebSocket...");
                await DisconnectAsync();
            }

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            // Build WebSocket URL - tenant is part of the path
            var wsUrl = _websocketUrl;
            if (!_websocketUrl.EndsWith($"/{_tenantId}"))
            {
                wsUrl = $"{_websocketUrl}/{_tenantId}";
            }
            
            // OrderWeb.net WebSocket authentication: X-API-Key header (NOT query parameter)
            // As per official documentation: headers: { 'X-API-Key': 'your_api_key' }
            _webSocket.Options.SetRequestHeader("X-API-Key", _apiKey);
            
            System.Diagnostics.Debug.WriteLine($" Connecting to WebSocket: {wsUrl}");
            System.Diagnostics.Debug.WriteLine($" Using X-API-Key header for authentication");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cancellationTokenSource.Token);

            _isConnected = true;
            LastConnectionTime = DateTime.Now;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(true, "Connected"));
            System.Diagnostics.Debug.WriteLine(" WebSocket connected successfully!");
            
            // Write connection success to log file for debugging
#if DEBUG
            try
            {
                var logPath = Path.Combine(FileSystem.AppDataDirectory, "websocket_log.txt");
                var logEntry = $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WebSocket CONNECTED\n";
                await File.AppendAllTextAsync(logPath, logEntry);
            }
            catch
            {
                // Ignore logging failures.
            }
#endif

            // Start listening for messages
            _ = Task.Run(() => ListenForMessagesAsync(_cancellationTokenSource.Token));

            // Start ping/pong keep-alive (every 30 seconds)
            _ = Task.Run(() => SendKeepAliveAsync(_cancellationTokenSource.Token));

            return true;
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(false, $"Connection failed: {ex.Message}"));
            System.Diagnostics.Debug.WriteLine($" WebSocket connection error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Exception Type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($" Inner Exception: {ex.InnerException.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// Disconnect from WebSocket server
    /// </summary>
    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }

            _webSocket?.Dispose();
            _webSocket = null;
            _isConnected = false;

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(false, "Disconnected"));
            System.Diagnostics.Debug.WriteLine(" WebSocket disconnected");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disconnecting WebSocket: {ex.Message}");
        }
    }

    /// <summary>
    /// Test WebSocket connection without keeping it open
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        ClientWebSocket? testSocket = null;
        try
        {
            if (string.IsNullOrEmpty(_websocketUrl) || string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_apiKey))
            {
                System.Diagnostics.Debug.WriteLine(" WebSocket configuration missing for test");
                return false;
            }

            testSocket = new ClientWebSocket();

            // Build WebSocket URL - check if tenant is already in URL
            var wsUrl = _websocketUrl;
            if (!_websocketUrl.EndsWith($"/{_tenantId}"))
            {
                wsUrl = $"{_websocketUrl}/{_tenantId}";
            }
            
            // Add API key as query parameter (OrderWeb.net authentication method)
            wsUrl = $"{wsUrl}?apiKey={_apiKey}";
            
            System.Diagnostics.Debug.WriteLine(" Testing configured WebSocket connection");

            // Also set headers as backup
            testSocket.Options.SetRequestHeader("X-Tenant-ID", _tenantId);
            testSocket.Options.SetRequestHeader("X-API-Key", _apiKey);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await testSocket.ConnectAsync(new Uri(wsUrl), cts.Token);

            if (testSocket.State == WebSocketState.Open)
            {
                System.Diagnostics.Debug.WriteLine(" WebSocket test successful");
                await testSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" WebSocket test failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($" Exception Type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($" Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($" Inner Exception: {ex.InnerException.Message}");
            }
            return false;
        }
        finally
        {
            testSocket?.Dispose();
        }
    }

    /// <summary>
    /// Listen for incoming WebSocket messages
    /// </summary>
    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine(" ListenForMessagesAsync started!");
        
        var buffer = new byte[1024 * 4];
        
        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    System.Diagnostics.Debug.WriteLine(" WebSocket closed by server");
                    await DisconnectAsync();
                    break;
                }

                using var messageStream = new MemoryStream();
                do
                {
                    messageStream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        break;
                    }

                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                while (result.MessageType == WebSocketMessageType.Text);

                var message = Encoding.UTF8.GetString(messageStream.ToArray());
                AppDiagnostics.Log($"WebSocket message received (length: {message.Length})");
#if DEBUG
                AppDiagnostics.Log($"WebSocket message received ({message.Length} characters)");

                try
                {
                    var logPath = Path.Combine(FileSystem.AppDataDirectory, "websocket_log.txt");
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Message received (length: {message.Length})\n\n";
                    await File.AppendAllTextAsync(logPath, logEntry);
                }
                catch
                {
                    // Ignore logging failures.
                }
#endif

                // Process the message
                await ProcessMessageAsync(message);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(" WebSocket listening cancelled");
        }
        catch (WebSocketException wsEx)
        {
            System.Diagnostics.Debug.WriteLine($" WebSocket connection lost: {wsEx.Message}");
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(false, $"Connection lost: {wsEx.Message}"));
            
            // Unlimited auto-reconnect with exponential backoff
            System.Diagnostics.Debug.WriteLine(" Starting auto-reconnect (unlimited retries)...");
            int retryCount = 0;
            int retryDelay = 5000; // Start with 5 seconds
            
            // Keep trying until connected or cancelled
            while (!_isConnected && !cancellationToken.IsCancellationRequested)
            {
                retryCount++;
                System.Diagnostics.Debug.WriteLine($" Reconnect attempt #{retryCount} in {retryDelay/1000}s...");
                await Task.Delay(retryDelay, cancellationToken);
                
                try
                {
                    var reconnected = await ConnectAsync();
                    if (reconnected)
                    {
                        System.Diagnostics.Debug.WriteLine($" Auto-reconnect successful after {retryCount} attempts!");
                        
                        // Trigger offline queue processing if available
                        System.Diagnostics.Debug.WriteLine(" Triggering offline queue processing...");
                        // Note: Queue service will be triggered by CloudSettingsPage after connection
                        
                        break;
                    }
                }
                catch (Exception reconEx)
                {
                    System.Diagnostics.Debug.WriteLine($" Reconnect attempt #{retryCount} failed: {reconEx.Message}");
                }
                
                // Exponential backoff: 5s → 10s → 30s → 60s (max)
                if (retryDelay < 10000)
                    retryDelay = 10000; // 10s after first failure
                else if (retryDelay < 30000)
                    retryDelay = 30000; // 30s after second
                else
                    retryDelay = 60000; // 60s max
            }
            
            if (!_isConnected)
            {
                System.Diagnostics.Debug.WriteLine($" Reconnection cancelled after {retryCount} attempts");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error in WebSocket listener: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   Stack trace: {ex.StackTrace}");
            _isConnected = false;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(false, $"Connection lost: {ex.Message}"));
        }
        
        System.Diagnostics.Debug.WriteLine(" ListenForMessagesAsync ended");
    }    /// <summary>
    /// Process incoming WebSocket messages
    /// </summary>
    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            // Update last message timestamp
            LastMessageTime = DateTime.Now;

#if DEBUG
            AppDiagnostics.Log($"WebSocket message received (length: {message.Length})");
#endif

            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement) &&
                !root.TryGetProperty("event", out typeElement))
            {
                System.Diagnostics.Debug.WriteLine(" Message missing 'type'/'event' field");
                return;
            }

            var messageType = typeElement.GetString()?.Trim();

            switch (messageType)
            {
                case "new_order":
                case "order_created":
                    HandleNewOrder(root);
                    break;

                case "new_reservation":
                case "reservation_created":
                case "reservation_updated":
                case "reservation_cancelled":
                    await HandleNewReservationAsync(root);
                    break;

                case "order_updated":
                    HandleOrderUpdate(root);
                    break;

                case "gift_card_updated":
                    HandleGiftCardUpdate(root);
                    break;

                case "loyalty_updated":
                    HandleLoyaltyUpdate(root);
                    break;

                case "connected":
                    System.Diagnostics.Debug.WriteLine($" Connection confirmed: {message}");
                    break;

                case "ping":
                    // Respond to keep-alive ping from server
                    System.Diagnostics.Debug.WriteLine(" Received ping from server, sending pong...");
                    await SendPongAsync();
                    break;

                case "pong":
                    // Server acknowledged our ping
                    System.Diagnostics.Debug.WriteLine(" Received pong from server - connection alive");
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($" Unknown message type: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error processing message: {ex.Message}");
        }
    }

    private async Task HandleNewReservationAsync(JsonElement data)
    {
        if (_reservationSyncService == null)
        {
            System.Diagnostics.Debug.WriteLine(" new_reservation ignored: ReservationSyncService not linked");
            return;
        }

        var dto = _reservationSyncService.ParseReservationFromWebSocket(data);
        if (dto == null)
        {
            System.Diagnostics.Debug.WriteLine(" new_reservation message could not be parsed");
            return;
        }

        await _reservationSyncService.ProcessInboundReservationAsync(dto, fromRealtime: true);
    }

    /// <summary>
    /// Handle new order from WebSocket
    /// </summary>
    private async void HandleNewOrder(JsonElement data)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" HandleNewOrder called!");
            System.Diagnostics.Debug.WriteLine($" Full data: {data.GetRawText()}");
            
            if (!TryGetOrderElement(data, out var orderElement))
            {
                System.Diagnostics.Debug.WriteLine(" New order message missing order payload");
                System.Diagnostics.Debug.WriteLine($"Available properties: {string.Join(", ", data.EnumerateObject().Select(p => p.Name))}");
                return;
            }

            // Parse basic order data (OrderWeb.net WebSocket format)
            var cloudOrderId = GetStringProperty(orderElement, "orderId")
                ?? GetStringProperty(orderElement, "order_id")
                ?? GetStringProperty(orderElement, "id")
                ?? GetStringProperty(data, "order_id")
                ?? "";
            var orderNumber = GetStringProperty(orderElement, "orderNumber")
                ?? GetStringProperty(orderElement, "order_number")
                ?? GetStringProperty(data, "order_number")
                ?? cloudOrderId.Substring(0, Math.Min(8, cloudOrderId.Length));
            var customerName = GetStringProperty(orderElement, "customerName")
                ?? GetStringProperty(orderElement, "customer_name")
                ?? GetNestedStringProperty(orderElement, "customer", "name")
                ?? "Guest";
            var customerPhone = GetStringProperty(orderElement, "customerPhone")
                ?? GetStringProperty(orderElement, "customer_phone")
                ?? GetNestedStringProperty(orderElement, "customer", "phone")
                ?? "";
            var customerEmail = GetStringProperty(orderElement, "customerEmail")
                ?? GetStringProperty(orderElement, "customer_email")
                ?? GetNestedStringProperty(orderElement, "customer", "email")
                ?? "";
            var customerAddress = GetStringProperty(orderElement, "deliveryAddress")
                ?? GetStringProperty(orderElement, "delivery_address")
                ?? GetNestedStringProperty(orderElement, "customer", "address")
                ?? "";
            
            // Financial data - WebSocket sends totalAmount directly
            var totalAmount = GetDecimalProperty(orderElement, "totalAmount", decimal.MinValue);
            if (totalAmount == decimal.MinValue)
            {
                totalAmount = GetDecimalProperty(orderElement, "total_amount", decimal.MinValue);
            }
            if (totalAmount == decimal.MinValue)
            {
                totalAmount = GetNestedDecimalProperty(orderElement, "payment", "total", 0m);
            }

            var subtotal = GetDecimalProperty(orderElement, "subtotal", totalAmount);
            var deliveryFee = GetDecimalProperty(orderElement, "deliveryFee", decimal.MinValue);
            if (deliveryFee == decimal.MinValue)
            {
                deliveryFee = GetDecimalProperty(orderElement, "delivery_fee", 0m);
            }
            var taxAmount = GetDecimalProperty(orderElement, "tax", decimal.MinValue);
            if (taxAmount == decimal.MinValue)
            {
                taxAmount = GetDecimalProperty(orderElement, "tax_amount", 0m);
            }
            
            // Order details
            var orderType = GetStringProperty(orderElement, "orderType")
                ?? GetStringProperty(orderElement, "order_type")
                ?? "pickup";
            var paymentMethod = GetStringProperty(orderElement, "paymentMethod")
                ?? GetStringProperty(orderElement, "payment_method")
                ?? GetNestedStringProperty(orderElement, "payment", "method")
                ?? "online";
            var paymentStatus = GetStringProperty(orderElement, "paymentStatus")
                ?? GetStringProperty(orderElement, "payment_status")
                ?? GetNestedStringProperty(orderElement, "payment", "status")
                ?? (OnlineOrderPaymentHelper.IsDeferredPaymentMethod(paymentMethod) ? "pending" : "paid");
            var specialInstructions = GetStringProperty(orderElement, "notes")
                ?? GetStringProperty(orderElement, "specialInstructions")
                ?? GetStringProperty(orderElement, "special_instructions")
                ?? "";
            var scheduledTime = ParseOptionalDate(
                GetStringProperty(orderElement, "scheduledTime")
                ?? GetStringProperty(orderElement, "scheduled_time")
                ?? GetStringProperty(orderElement, "scheduled_for"));
            
            // Get createdAt from order (OrderWeb.net format)
            var createdAt = ParseOptionalDate(
                GetStringProperty(orderElement, "createdAt")
                ?? GetStringProperty(orderElement, "created_at")) ?? DateTime.Now;

            System.Diagnostics.Debug.WriteLine($" NEW ORDER via WebSocket: {orderNumber}");

            var cloudOrder = new Models.Api.CloudOrderResponse
            {
                Id = cloudOrderId,
                OrderNumber = orderNumber,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerEmail = customerEmail,
                Address = customerAddress,
                Total = totalAmount.ToString("0.00"),
                Subtotal = subtotal.ToString("0.00"),
                DeliveryFee = deliveryFee.ToString("0.00"),
                Tax = taxAmount.ToString("0.00"),
                OrderType = orderType,
                PaymentMethod = paymentMethod,
                PaymentStatus = paymentStatus,
                SpecialInstructions = specialInstructions,
                ScheduledTime = scheduledTime,
                CreatedAt = createdAt
            };

            // Parse order items - items are at root level in OrderWeb.net structure
            if (TryGetItemsArray(data, orderElement, out var itemsElement))
            {
                System.Diagnostics.Debug.WriteLine($" Parsing {itemsElement.GetArrayLength()} items for order {orderNumber}");
                
                foreach (var itemElem in itemsElement.EnumerateArray())
                {
                    // Get item name from items[].name field (as per OrderWeb.net structure)
                    var itemName = GetStringProperty(itemElem, "name")
                        ?? GetStringProperty(itemElem, "item_name")
                        ?? GetStringProperty(itemElem, "displayName")
                        ?? GetStringProperty(itemElem, "display_name")
                        ?? "Unknown Item";
                    
                    var item = new Models.Api.CloudOrderItem
                    {
                        Id = GetIntProperty(itemElem, "id", 0),
                        MenuItemId = GetStringProperty(itemElem, "menuItemId") ?? GetStringProperty(itemElem, "menu_item_id"),
                        VariantId = GetStringProperty(itemElem, "variantId") ?? GetStringProperty(itemElem, "variant_id"),
                        VariantName = GetStringProperty(itemElem, "variantName") ?? GetStringProperty(itemElem, "variant_name"),
                        DisplayName = GetStringProperty(itemElem, "displayName") ?? GetStringProperty(itemElem, "display_name"),
                        Name = itemName,
                        Quantity = GetIntProperty(itemElem, "quantity", 1),
                        Price = GetDecimalProperty(itemElem, "price", 0m),
                        SpecialInstructions = GetStringProperty(itemElem, "specialInstructions")
                            ?? GetStringProperty(itemElem, "special_instructions")
                    };

                    System.Diagnostics.Debug.WriteLine($"   Item: {itemName} x{item.Quantity}");

                    // Parse selectedAddons (JSON array in OrderWeb.net structure)
                    if (TryGetAddonArray(itemElem, out var addonsElem))
                    {
                        foreach (var addonElem in addonsElem.EnumerateArray())
                        {
                            var addon = new Models.Api.CloudOrderAddon
                            {
                                Id = GetStringProperty(addonElem, "addon_id") ?? GetStringProperty(addonElem, "id"),
                                Name = GetStringProperty(addonElem, "name")
                                    ?? GetStringProperty(addonElem, "modifier_name")
                                    ?? "Unknown Addon",
                                Price = GetDecimalProperty(addonElem, "price", 0m)
                            };
                            item.SelectedAddons.Add(addon);
                        }
                    }

                    cloudOrder.Items.Add(item);
                }
            }

            var success = _cloudOrderService != null
                ? await _cloudOrderService.ProcessIncomingCloudOrderAsync(cloudOrder)
                : await SaveOrderWithoutCloudServiceAsync(cloudOrder, orderElement.GetRawText());

            if (success)
            {
                System.Diagnostics.Debug.WriteLine($" Order {orderNumber} processed from WebSocket successfully!");
                
                // Trigger event for UI notification
                NewOrderReceived?.Invoke(this, new OrderReceivedEventArgs
                {
                    OrderId = cloudOrderId,
                    OrderNumber = orderNumber,
                    CustomerName = customerName,
                    TotalAmount = totalAmount,
                    OrderType = orderType
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Info: Order {orderNumber} already existed or was not saved from WebSocket.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error handling new order: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private static bool TryGetItemsArray(JsonElement messageRoot, JsonElement orderElement, out JsonElement itemsElement)
    {
        if (messageRoot.TryGetProperty("items", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (orderElement.TryGetProperty("items", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        itemsElement = default;
        return false;
    }

    private static bool TryGetAddonArray(JsonElement itemElement, out JsonElement addonsElement)
    {
        foreach (var propertyName in new[] { "selectedAddons", "selected_addons", "addons", "modifiers" })
        {
            if (itemElement.TryGetProperty(propertyName, out addonsElement) &&
                addonsElement.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        addonsElement = default;
        return false;
    }

    private static bool TryGetOrderElement(JsonElement messageRoot, out JsonElement orderElement)
    {
        foreach (var wrapperName in new[] { "data", "order", "payload" })
        {
            if (messageRoot.TryGetProperty(wrapperName, out var wrapper) && wrapper.ValueKind == JsonValueKind.Object)
            {
                if (LooksLikeOrder(wrapper))
                {
                    orderElement = wrapper;
                    return true;
                }

                if (TryGetOrderElement(wrapper, out orderElement))
                {
                    return true;
                }
            }
        }

        if (LooksLikeOrder(messageRoot))
        {
            orderElement = messageRoot;
            return true;
        }

        orderElement = default;
        return false;
    }

    private static bool LooksLikeOrder(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
            (element.TryGetProperty("orderId", out _) ||
             element.TryGetProperty("order_id", out _) ||
             element.TryGetProperty("orderNumber", out _) ||
             element.TryGetProperty("order_number", out _) ||
             element.TryGetProperty("items", out _));
    }

    private static decimal GetDecimalProperty(JsonElement element, string propertyName, decimal fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static decimal GetNestedDecimalProperty(JsonElement element, string parentName, string propertyName, decimal fallback)
    {
        return element.TryGetProperty(parentName, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetDecimalProperty(parent, propertyName, fallback)
            : fallback;
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? GetNestedStringProperty(JsonElement element, string parentName, string propertyName)
    {
        return element.TryGetProperty(parentName, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetStringProperty(parent, propertyName)
            : null;
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private async Task<bool> SaveOrderWithoutCloudServiceAsync(Models.Api.CloudOrderResponse cloudOrder, string rawOrderData)
    {
        var order = new Models.Order
        {
            OrderId = cloudOrder.Id,
            OrderNumber = cloudOrder.OrderNumber,
            CloudOrderId = cloudOrder.Id,
            CustomerName = cloudOrder.CustomerName ?? "Guest",
            CustomerPhone = cloudOrder.CustomerPhone ?? "",
            CustomerEmail = cloudOrder.CustomerEmail ?? "",
            CustomerAddress = cloudOrder.Address ?? "",
            TotalAmount = cloudOrder.TotalAmount,
            SubtotalAmount = decimal.TryParse(cloudOrder.Subtotal, out var subtotal) ? subtotal : cloudOrder.TotalAmount,
            DeliveryFee = decimal.TryParse(cloudOrder.DeliveryFee, out var deliveryFee) ? deliveryFee : 0m,
            TaxAmount = decimal.TryParse(cloudOrder.Tax, out var tax) ? tax : 0m,
            OrderType = cloudOrder.OrderType ?? "pickup",
            PaymentMethod = OnlineOrderPaymentHelper.GetStorageMethod(cloudOrder.PaymentMethod ?? "online"),
            SpecialInstructions = cloudOrder.SpecialInstructions ?? "",
            ScheduledTime = cloudOrder.ScheduledTime,
            Status = Models.OrderStatus.New,
            LocalLifecycleState = Models.LocalLifecycleState.Active,
            IsOpen = true,
            SyncStatus = Models.SyncStatus.Synced,
            SourceChannel = "web",
            CreatedAt = cloudOrder.CreatedAt,
            UpdatedAt = DateTime.Now,
            OrderData = rawOrderData,
            PaymentStatus = OnlineOrderPaymentHelper.ToPaymentStatus(cloudOrder.PaymentMethod, cloudOrder.PaymentStatus)
        };

        foreach (var cloudItem in cloudOrder.Items)
        {
            var inferredVariant = string.IsNullOrWhiteSpace(cloudItem.VariantId) && string.IsNullOrWhiteSpace(cloudItem.VariantName)
                ? await InferVariantAsync(cloudItem.MenuItemId, cloudItem.Price)
                : null;
            var variantId = !string.IsNullOrWhiteSpace(cloudItem.VariantId) ? cloudItem.VariantId : inferredVariant?.Id;
            var variantName = !string.IsNullOrWhiteSpace(cloudItem.VariantName) ? cloudItem.VariantName : inferredVariant?.Name;

            var item = new Models.OrderItem
            {
                OrderId = cloudOrder.Id,
                CloudItemId = cloudItem.Id,
                MenuItemId = cloudItem.MenuItemId,
                VariantId = variantId,
                VariantName = variantName,
                DisplayName = !string.IsNullOrWhiteSpace(cloudItem.DisplayName) ? cloudItem.DisplayName : BuildDisplayName(cloudItem.Name, variantName),
                ItemName = cloudItem.Name ?? "Unknown Item",
                Quantity = cloudItem.Quantity,
                ItemPrice = cloudItem.Price ?? 0m,
                SpecialInstructions = cloudItem.SpecialInstructions
            };

            foreach (var cloudAddon in cloudItem.SelectedAddons)
            {
                item.Addons.Add(new Models.OrderItemAddon
                {
                    AddonId = cloudAddon.Id,
                    AddonName = cloudAddon.Name ?? "Unknown Addon",
                    AddonPrice = cloudAddon.Price ?? 0m,
                    Quantity = 1
                });
            }

            order.Items.Add(item);
        }

        ApplyOrderTotalFallback(order);

        var (success, message) = await _orderService.SaveOrderAsync(order);
        if (!success)
        {
            System.Diagnostics.Debug.WriteLine($" WebSocket fallback save failed for {cloudOrder.OrderNumber}: {message}");
        }

        return success;
    }

    private static void ApplyOrderTotalFallback(Models.Order order)
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

    private static string? BuildDisplayName(string? itemName, string? variantName)
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
    /// Handle order status update
    /// </summary>
    private void HandleOrderUpdate(JsonElement data)
    {
        try
        {
            var orderId = data.GetProperty("order_id").GetString() ?? "";
            var newStatus = data.GetProperty("status").GetString() ?? "";

            System.Diagnostics.Debug.WriteLine($" Order updated: {orderId} → {newStatus}");

            // TODO: Update in local database when OrderService method is ready
            // await _orderService.UpdateOrderStatusAsync(orderId, newStatus);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error handling order update: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle gift card balance update
    /// </summary>
    private void HandleGiftCardUpdate(JsonElement data)
    {
        try
        {
            var cardNumber = data.GetProperty("card_number").GetString() ?? "";
            var newBalance = data.GetProperty("balance").GetDecimal();

            System.Diagnostics.Debug.WriteLine(" Gift card balance update received");

            GiftCardUpdated?.Invoke(this, new GiftCardUpdatedEventArgs
            {
                CardNumber = cardNumber,
                NewBalance = newBalance
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error handling gift card update: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle loyalty points update
    /// </summary>
    private void HandleLoyaltyUpdate(JsonElement data)
    {
        try
        {
            var customerPhone = data.GetProperty("customer_phone").GetString() ?? "";
            var newPoints = data.GetProperty("points").GetInt32();

            System.Diagnostics.Debug.WriteLine("Loyalty points update received");

            LoyaltyUpdated?.Invoke(this, new LoyaltyUpdatedEventArgs
            {
                CustomerPhone = customerPhone,
                NewPoints = newPoints
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error handling loyalty update: {ex.Message}");
        }
    }

    /// <summary>
    /// Send periodic ping to keep connection alive (every 30 seconds)
    /// </summary>
    private async Task SendKeepAliveAsync(CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine(" Keep-alive ping task started (30s interval)");
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                if (_webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        var ping = "{\"type\":\"ping\"}"u8.ToArray();
                        await _webSocket.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Text, true, CancellationToken.None);
                        System.Diagnostics.Debug.WriteLine(" Sent ping to server");
                        
                        // Log connection health
                        var timeSinceLastMessage = LastMessageTime.HasValue 
                            ? (DateTime.Now - LastMessageTime.Value).TotalSeconds 
                            : -1;
                        System.Diagnostics.Debug.WriteLine($" Connection health: Last message {timeSinceLastMessage:F0}s ago");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($" Error sending ping: {ex.Message}");
                        // Connection may be broken, try to reconnect
                        System.Diagnostics.Debug.WriteLine(" Ping failed - attempting reconnect...");
                        await ConnectAsync();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($" WebSocket not open (state: {_webSocket?.State}), attempting reconnect...");
                    await ConnectAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(" Keep-alive ping task cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Keep-alive ping error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send pong response to keep connection alive
    /// </summary>
    private async Task SendPongAsync()
    {
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                var pong = "{\"type\":\"pong\"}"u8.ToArray();
                await _webSocket.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending pong: {ex.Message}");
        }
    }
}

// Event argument classes
public class OrderReceivedEventArgs : EventArgs
{
    public string OrderId { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string OrderType { get; set; } = "";
}

public class GiftCardUpdatedEventArgs : EventArgs
{
    public string CardNumber { get; set; } = "";
    public decimal NewBalance { get; set; }
}

public class LoyaltyUpdatedEventArgs : EventArgs
{
    public string CustomerPhone { get; set; } = "";
    public int NewPoints { get; set; }
}

public class ConnectionStatusEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string Message { get; set; } = "";

    public ConnectionStatusEventArgs(bool isConnected, string message)
    {
        IsConnected = isConnected;
        Message = message;
    }
}
