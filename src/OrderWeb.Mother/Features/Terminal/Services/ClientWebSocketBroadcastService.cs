using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using MySqlConnector;
using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Compatibility;
using OrderWeb.Contracts.Features;
using OrderWeb.Contracts.Synchronization;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class ClientWebSocketBroadcastService : IDisposable
{
    public const int DefaultPort = 5055;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;
    private readonly PermissionService _permissionService;
    private readonly ReservationSyncService _reservationSync;
    private readonly ClientTerminalAccessService _clientAccess;
    private readonly ClientPosOperationalService _operational;
    private readonly ConcurrentDictionary<string, ClientWebSocketConnection> _clients = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _rateLimitWindows = new();
    private readonly ConcurrentDictionary<string, object> _cashierRequestIds = new();
    private readonly SemaphoreSlim _lifetimeLock = new(1, 1);
    private WebApplication? _app;

    public ClientWebSocketBroadcastService(
        DatabaseService databaseService,
        AuthenticationService authenticationService,
        PermissionService permissionService,
        ReservationSyncService reservationSync,
        ClientTerminalAccessService clientAccess)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
        _permissionService = permissionService;
        _reservationSync = reservationSync;
        _clientAccess = clientAccess;
        _operational = new ClientPosOperationalService(databaseService, reservationSync);
        _reservationSync.SyncCompleted += OnReservationSyncCompleted;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = DefaultPort;
    public string StatusMessage { get; private set; } = "Not started";
    public DateTime? StartedAt { get; private set; }
    public int ConnectedClientCount => _clients.Count;

    public async Task StartAsync(int port = DefaultPort)
    {
        if (!TerminalConfigurationService.IsConfigured || !TerminalConfigurationService.IsMotherTerminal)
        {
            StatusMessage = "API server starts on the Mother terminal only.";
            return;
        }

        await _lifetimeLock.WaitAsync();
        try
        {
            if (IsRunning)
            {
                return;
            }

            Port = port <= 0 ? DefaultPort : port;
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel().UseUrls($"http://0.0.0.0:{Port}");

            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet("/health", HandleHealthAsync);
            app.MapPost("/api/pos/v1/compatibility", HandleCompatibilityAsync);
            app.MapPost("/api/client/bootstrap", HandleBootstrapAsync);
            app.MapGet("/api/client/sync/versions", HandleSyncVersionsAsync);
            app.MapGet("/api/client/layout", HandleLayoutAsync);
            app.MapGet("/api/client/menu", HandleMenuAsync);
            app.MapGet("/api/client/delivery-zones/lookup", HandleDeliveryZoneLookupAsync);
            app.MapGet("/api/client/delivery-zones/quote", HandleDeliveryZoneQuoteAsync);
            app.MapGet("/api/client/orders", HandleListOrdersAsync);
            app.MapGet("/api/client/orders/{orderId}", HandleGetOrderAsync);
            app.MapPost("/api/client/orders", HandleUpsertOrderAsync);
            app.MapPost("/api/client/orders/void", HandleVoidOrderAsync);
            app.MapGet("/api/client/order-history", HandleOrderHistoryAsync);
            app.MapGet("/api/client/order-history/{orderId}", HandleOrderHistoryDetailAsync);
            app.MapGet("/api/client/reservations", HandleListReservationsAsync);
            app.MapPost("/api/client/reservations", HandleCreateReservationAsync);
            app.MapPost("/api/client/reservations/status", HandleUpdateReservationStatusAsync);
            app.MapPost("/api/client/reservations/sync", HandleSyncReservationsAsync);
            app.MapPost("/api/client/login", HandleLoginAsync);
            app.MapGet("/api/client/access", HandleClientAccessAsync);
            app.MapGet("/api/client/cashier/authorization", HandleCashierAuthorizationAsync);
            app.MapGet("/api/client/cashier/dashboard", HandleCashierDashboardAsync);
            app.MapGet("/api/client/cashier/z-report/preview", HandleCashierZReportPreviewAsync);
            app.MapPost("/api/client/cashier/z-report/print", HandleCashierZReportPrintAsync);
            app.MapPost("/api/client/cashier/cash-drawer/open", HandleCashierCashDrawerOpenAsync);
            app.MapGet("/api/client/customers/search", HandleCustomerSearchAsync);
            app.MapPost("/api/client/customers/upsert", HandleCustomerUpsertAsync);
            app.MapPost("/api/client/payments", HandlePaymentAsync);
            app.MapGet("/api/client/payments/{requestId}", HandlePaymentStatusAsync);
            app.MapPost("/api/client/gift-cards/lookup", HandleGiftCardLookupAsync);
            app.MapPost("/api/client/gift-cards/sell", HandleGiftCardSellAsync);
            app.MapPost("/api/client/gift-cards/top-up", HandleGiftCardTopUpAsync);
            app.MapPost("/api/client/gift-cards/redeem", HandleGiftCardRedeemAsync);
            app.MapPost("/api/client/gift-cards/activate", HandleGiftCardActivateAsync);
            app.MapPost("/api/client/loyalty/search", HandleLoyaltySearchAsync);
            app.MapPost("/api/client/loyalty/customers", HandleLoyaltyCreateCustomerAsync);
            app.MapPost("/api/client/loyalty/add", HandleLoyaltyAddPointsAsync);
            app.MapPost("/api/client/loyalty/redeem", HandleLoyaltyRedeemPointsAsync);
            app.MapPost("/api/client/loyalty/history", HandleLoyaltyHistoryAsync);
            app.MapPost("/api/client/loyalty/test", HandleLoyaltyTestAsync);
            app.MapPost("/api/client/prints", HandlePrintAsync);
            app.MapGet("/api/client/prints/{requestId}", HandlePrintStatusAsync);
            app.MapGet("/api/client/images/{imageId}", HandleImageAsync);
            app.MapPost("/terminals/heartbeat", HandleHeartbeatAsync);
            app.Map("/ws", HandleWebSocketAsync);

            await app.StartAsync();
            _app = app;
            IsRunning = true;
            StartedAt = DateTime.Now;
            StatusMessage = $"Running on port {Port}";
        }
        catch (Exception ex)
        {
            IsRunning = false;
            StatusMessage = $"Could not start Mother API: {ex.Message}";
            AppDiagnostics.LogFatal("Start Mother Client API", ex);
        }
        finally
        {
            _lifetimeLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifetimeLock.WaitAsync();
        try
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            IsRunning = false;
            StatusMessage = "Stopped";
        }
        finally
        {
            _lifetimeLock.Release();
        }
    }

    /// <summary>
    /// Sends a compact change notification. Consumers fetch authoritative data
    /// afterwards; this channel intentionally never carries a full menu/order.
    /// </summary>
    public async Task PublishDataChangedAsync(string eventType, string version, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("An event type is required.", nameof(eventType));
        }

        var section = ResolveConfigurationSection(eventType);
        if (section != null)
        {
            // A configuration notification always carries the version Mother
            // just committed. The data itself is fetched separately by Client.
            version = await IncrementConfigurationVersionAsync(section);
        }
        var timestamp = DateTimeOffset.UtcNow;
        var correlation = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
        long eventId;
        await using (var connection = new MySqlConnection(_databaseService.GetConnectionString()))
        {
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);
            await using var command = new MySqlCommand(@"
                INSERT INTO terminal_events (event_type, entity_type, entity_id, payload_json, created_at)
                VALUES (@eventType, 'sync', @version, @payload, UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();", connection);
            command.Parameters.AddWithValue("@eventType", eventType.Trim());
            command.Parameters.AddWithValue("@version", version ?? string.Empty);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(new { correlationId = correlation, restaurantId = GetRestaurantSlug(), timestamp }));
            eventId = Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        var notification = new
        {
            type = eventType,
            eventId,
            restaurantId = GetRestaurantSlug(),
            version = version ?? string.Empty,
            timestamp,
            correlationId = correlation
        };
        foreach (var client in _clients.Values)
        {
            if (client.WebSocket.State == WebSocketState.Open)
            {
                await SendWebSocketJsonAsync(client.WebSocket, notification, CancellationToken.None);
            }
        }
    }

    /// <summary>Call after a Mother configuration transaction commits.</summary>
    public Task PublishConfigurationChangedAsync(string section, string? correlationId = null) =>
        PublishDataChangedAsync($"{section.Trim().ToLowerInvariant()}.updated", string.Empty, correlationId);

    /// <summary>
    /// Phase 4: Client API mutations run inside Mother but must refresh Mother Live Order UI
    /// as a remote change (source != Mother till name), otherwise DatabaseChangeMonitor skips them.
    /// </summary>
    private void NotifyMotherUiOfClientOrderChange(string? orderId, string? orderNumber, string? terminalId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        var sourceName = string.IsNullOrWhiteSpace(terminalId)
            ? "Client POS"
            : $"Client {terminalId.Trim()}";

        try
        {
            AppDataRefreshService.RequestRefresh(
                AppDataRefreshType.Orders | AppDataRefreshType.Tables,
                sourceTerminalName: sourceName,
                orderNumber: orderNumber,
                entityType: "order",
                entityId: orderId.Trim());
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Mother UI order refresh notify failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Revokes a terminal session in Mother storage before notifying that exact
    /// Client. Subsequent sensitive HTTP calls fail even if the socket message
    /// is delayed or missed.
    /// </summary>
    public async Task RevokeClientSessionAsync(string terminalId, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("A terminal ID is required.", nameof(terminalId));
        var correlation = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
        long eventId;
        await using (var connection = new MySqlConnection(_databaseService.GetConnectionString()))
        {
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);
            await using (var revoke = new MySqlCommand("UPDATE client_user_sessions SET revoked_at = UTC_TIMESTAMP() WHERE terminal_id = @terminalId AND revoked_at IS NULL", connection))
            {
                revoke.Parameters.AddWithValue("@terminalId", terminalId);
                await revoke.ExecuteNonQueryAsync();
            }
            await using var audit = new MySqlCommand(@"
                INSERT INTO terminal_events (event_type, entity_type, entity_id, payload_json, created_at)
                VALUES ('session.revoked', 'terminal', @terminalId, @payload, UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();", connection);
            audit.Parameters.AddWithValue("@terminalId", terminalId);
            audit.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(new { correlationId = correlation, restaurantId = GetRestaurantSlug() }));
            eventId = Convert.ToInt64(await audit.ExecuteScalarAsync());
        }
        if (_clients.TryGetValue(terminalId, out var client) && client.WebSocket.State == WebSocketState.Open)
        {
            await SendWebSocketJsonAsync(client.WebSocket, new
            {
                type = "session.revoked", eventId, restaurantId = GetRestaurantSlug(), version = eventId.ToString(),
                timestamp = DateTimeOffset.UtcNow, correlationId = correlation
            }, CancellationToken.None);
        }
    }

    private async Task HandleSyncVersionsAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        var versions = await GetConfigurationVersionsAsync();
        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            schemaVersion = 1,
            versions,
            generatedUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task HandleLayoutAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.OpenTables) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.DineIn))
        {
            return;
        }

        try
        {
            var floorService = new FloorService();
            var tableSessionService = new TableSessionService();
            List<RestaurantTable> tables;
            List<Floor> floors;
            try
            {
                floors = await floorService.GetAllFloorsAsync();
            }
            catch (Exception floorEx)
            {
                AppDiagnostics.Log($"Client layout floors failed: {floorEx.GetType().Name}: {floorEx.Message}");
                floors = new List<Floor>();
            }

            try
            {
                tables = await tableSessionService.GetTablesWithSessionsAsync();
            }
            catch (Exception sessionEx)
            {
                AppDiagnostics.Log($"Client layout sessions failed: {sessionEx.GetType().Name}: {sessionEx.Message}");
                tables = new List<RestaurantTable>();
            }

            if (tables.Count == 0)
            {
                try
                {
                    tables = await new RestaurantTableService().GetAllTablesAsync();
                }
                catch (Exception tableEx)
                {
                    AppDiagnostics.Log($"Client layout tables failed: {tableEx.GetType().Name}: {tableEx.Message}");
                    tables = new List<RestaurantTable>();
                }
            }

            // Version stamp must never block layout delivery to Client.
            string version = "1";
            try
            {
                var versions = await GetConfigurationVersionsAsync();
                if (versions.TryGetValue(SyncSectionKeys.Tables, out var tableVersion) &&
                    !string.IsNullOrWhiteSpace(tableVersion))
                {
                    version = tableVersion;
                }
            }
            catch (Exception versionEx)
            {
                AppDiagnostics.Log($"Client layout version lookup failed: {versionEx.GetType().Name}: {versionEx.Message}");
            }

            // If floors list is empty but tables exist, synthesize floor rows from table FloorIds.
            if (floors.Count == 0 && tables.Count > 0)
            {
                floors = tables
                    .GroupBy(table => table.FloorId)
                    .OrderBy(group => group.Key)
                    .Select(group => new Floor
                    {
                        Id = group.Key,
                        Name = string.IsNullOrWhiteSpace(group.First().FloorName)
                            ? $"Floor {group.Key}"
                            : group.First().FloorName!,
                        IsActive = true
                    })
                    .ToList();
            }

            var floorIdsWithBackground = await LoadFloorIdsWithBackgroundImagesAsync();

            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                version,
                generatedUtc = DateTimeOffset.UtcNow,
                floors = floors.Select((floor, index) => new
                {
                    id = floor.Id.ToString(),
                    name = string.IsNullOrWhiteSpace(floor.Name) ? $"Floor {floor.Id}" : floor.Name,
                    sortOrder = index,
                    backgroundImageId = floorIdsWithBackground.Contains(floor.Id) || floor.HasBackgroundImage
                        ? $"floor-{floor.Id}"
                        : null
                }),
                tables = tables.Select(table => new
                {
                    id = table.Id.ToString(),
                    floorId = table.FloorId.ToString(),
                    name = table.TableNumber,
                    capacity = table.Capacity,
                    status = table.Status.ToString(),
                    x = table.PositionX,
                    y = table.PositionY,
                    openOrderId = table.CurrentSession?.LinkedOrderId ?? table.CurrentSession?.CurrentOrderId,
                    revision = 1L,
                    guestCount = table.CurrentSession?.PartySize ?? 0,
                    currentTotal = table.CurrentSession?.LinkedOrderTotalAmount ?? 0m,
                    sessionStatus = table.CurrentSession?.Status.ToString(),
                    icon = string.IsNullOrWhiteSpace(table.TableDesignIcon) ? "table_1.png" : table.TableDesignIcon
                })
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client layout snapshot failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = $"Mother POS could not load the restaurant layout. ({ex.GetType().Name}: {ex.Message})"
            });
        }
    }

    private async Task HandleMenuAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders) ||
            !await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.DineIn, PosFeatureKeys.Collection, PosFeatureKeys.Delivery, PosFeatureKeys.LiveOrders))
        {
            return;
        }

        try
        {
            // Version stamp must never block menu delivery to Client.
            string version = "1";
            try
            {
                var versions = await GetConfigurationVersionsAsync();
                if (versions.TryGetValue(SyncSectionKeys.Menu, out var menuVersion) &&
                    !string.IsNullOrWhiteSpace(menuVersion))
                {
                    version = menuVersion;
                }
            }
            catch (Exception versionEx)
            {
                AppDiagnostics.Log($"Client menu version lookup failed: {versionEx.GetType().Name}: {versionEx.Message}");
            }

            var snapshot = await _operational.BuildMenuSnapshotAsync(version);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                version = snapshot.Version,
                generatedUtc = DateTimeOffset.UtcNow,
                categories = snapshot.Categories,
                products = snapshot.Products,
                prices = snapshot.Prices,
                modifierGroups = snapshot.ModifierGroups,
                modifiers = snapshot.Modifiers,
                productModifiers = snapshot.ProductModifiers,
                variants = snapshot.Variants,
                mealDeals = snapshot.MealDeals,
                mealDealChoices = snapshot.MealDealChoices,
                mealDealCategoryRules = snapshot.MealDealCategoryRules,
                tastingMenus = snapshot.TastingMenus,
                tastingMenuOptions = snapshot.TastingMenuOptions,
                tastingMenuCourses = snapshot.TastingMenuCourses,
                tastingMenuChoices = snapshot.TastingMenuChoices
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client menu snapshot failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = $"Mother POS could not load the food menu. ({ex.GetType().Name}: {ex.Message})"
            });
        }
    }

    private async Task HandleDeliveryZoneLookupAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Delivery))
        {
            return;
        }

        var postcode = context.Request.Query["postcode"].ToString();
        var suggestions = await _operational.LookupAddressesAsync(postcode);
        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            addressSuggestions = suggestions
        });
    }

    private async Task HandleDeliveryZoneQuoteAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Delivery))
        {
            return;
        }

        var postcode = context.Request.Query["postcode"].ToString();
        var quote = await _operational.QuoteDeliveryZoneAsync(postcode);
        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            quote = new
            {
                postcode = quote.Postcode,
                isDeliverable = quote.IsDeliverable,
                deliveryZoneName = quote.DeliveryZoneName,
                deliveryFee = quote.DeliveryFee
            }
        });
    }

    private async Task HandleListOrdersAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders))
        {
            return;
        }

        try
        {
            var orderType = context.Request.Query["orderType"].ToString();
            var orderFeature = FeatureForOrderType(orderType);
            if (orderFeature is not null)
            {
                if (!await EnsureFeatureAsync(context, session, orderFeature))
                {
                    return;
                }
            }
            else if (!await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.Collection, PosFeatureKeys.Delivery, PosFeatureKeys.LiveOrders, PosFeatureKeys.DineIn))
            {
                return;
            }
            var orders = await _operational.ListOpenOrdersAsync(orderType);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                orders
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client open-order list failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not load open Client orders."
            });
        }
    }

    private async Task HandleGetOrderAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders))
        {
            return;
        }

        var orderId = context.Request.RouteValues["orderId"]?.ToString();
        if (string.IsNullOrWhiteSpace(orderId))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A Mother order id is required."
            });
            return;
        }

        try
        {
            var result = await _operational.GetOrderAsync(orderId);
            if (!result.Success || result.Order == null)
            {
                await WriteJsonAsync(context, (HttpStatusCode)result.StatusCode, new
                {
                    success = false,
                    message = result.Message
                });
                return;
            }

            var feature = FeatureForOrderType(result.Order.OrderType) ?? PosFeatureKeys.Collection;
            if (!await EnsureFeatureAsync(context, session, feature))
            {
                return;
            }

            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                message = result.Message,
                order = result.Order
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order load failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not load this Client order."
            });
        }
    }

    private async Task HandleUpsertOrderAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "client-orders", 40, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders))
        {
            return;
        }

        var request = await ReadJsonAsync<ClientOrderUpsertHttpRequest>(context);
        if (request == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "An order payload is required."
            });
            return;
        }

        var orderFeature = FeatureForOrderType(request.OrderType) ?? PosFeatureKeys.Collection;
        if (!await EnsureFeatureAsync(context, session, orderFeature))
        {
            return;
        }

        var printDocuments = ResolveClientPrintDocuments(request.PrintKitchen, request.PrintReceipt, request.PrintDocuments);
        if (printDocuments.Contains("kitchen_ticket") &&
            !await EnsureAnyCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders, PosCapabilityKeys.SubmitOrders, PosCapabilityKeys.PrintReceipts))
        {
            return;
        }
        if (printDocuments.Contains("customer_receipt") &&
            !await EnsureCapabilityAsync(context, session, PosCapabilityKeys.PrintReceipts))
        {
            return;
        }
        if (printDocuments.Contains("reprint") &&
            !await EnsureCapabilityAsync(context, session, PosCapabilityKeys.ReprintReceipts))
        {
            return;
        }

        try
        {
            var result = await _operational.UpsertOrderAsync(new ClientOrderUpsertRequest(
                request.OrderId,
                request.OrderType,
                request.CustomerName,
                request.CustomerPhone,
                request.CustomerEmail,
                request.CustomerAddress,
                request.DeliveryFee,
                request.Notes,
                request.ScheduledTime,
                request.TableId,
                request.TableNumber,
                request.Guests,
                request.Lines?.Select(line => new ClientOrderLineRequest(
                    line.Id,
                    line.ProductId,
                    line.Name ?? string.Empty,
                    line.Quantity,
                    line.UnitPrice,
                    line.Notes,
                    line.Modifiers,
                    line.VariantId,
                    line.VariantName,
                    line.VariantPrice,
                    line.MealDealId,
                    line.MealDealChoices,
                    line.TastingMenuId)).ToList(),
                request.ExpectedVersion,
                request.ExpectedUpdatedUtc));
            if (!result.Success)
            {
                await WriteJsonAsync(context, (HttpStatusCode)result.StatusCode, new
                {
                    success = false,
                    conflict = result.StatusCode == 409 && result.Order != null,
                    message = result.Message,
                    order = result.Order
                });
                return;
            }

            if (result.Order == null)
            {
                await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
                {
                    success = false,
                    message = result.Message
                });
                return;
            }

            object? print = null;
            if (printDocuments.Count > 0)
            {
                var printRequestId = string.IsNullOrWhiteSpace(request.PrintRequestId)
                    ? Guid.NewGuid().ToString("N")
                    : request.PrintRequestId.Trim();
                print = await ExecuteClientPrintDocumentsAsync(
                    session,
                    result.Order.Id,
                    printDocuments,
                    printRequestId);
            }

            // WS order.updated is published from OrderService after persist.
            NotifyMotherUiOfClientOrderChange(result.Order.Id, result.Order.OrderNumber, session.TerminalId);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                message = result.Message,
                order = result.Order,
                print
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order save failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not save this Client order."
            });
        }
    }

    private async Task HandleVoidOrderAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureAnyCapabilityAsync(context, session, PosCapabilityKeys.VoidOrders, PosCapabilityKeys.VoidItems, PosCapabilityKeys.CreateOrders, PosCapabilityKeys.SubmitOrders))
        {
            return;
        }

        var request = await ReadJsonAsync<ClientOrderVoidHttpRequest>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.OrderId))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A Mother order id is required to void."
            });
            return;
        }

        try
        {
            var existing = await new OrderService().GetOrderByExternalIdAsync(request.OrderId.Trim());
            var feature = FeatureForOrderType(existing?.OrderType) ?? PosFeatureKeys.Collection;
            if (!await EnsureFeatureAsync(context, session, feature))
            {
                return;
            }

            var result = await _operational.VoidOrderAsync(request.OrderId);
            if (!result.Success || result.Order == null)
            {
                await WriteJsonAsync(context, (HttpStatusCode)result.StatusCode, new
                {
                    success = false,
                    message = result.Message
                });
                return;
            }

            NotifyMotherUiOfClientOrderChange(result.Order.Id, result.Order.OrderNumber, session.TerminalId);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                message = result.Message,
                order = result.Order
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order void failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not void this Client order."
            });
        }
    }

    private async Task HandleOrderHistoryAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        // Order History on Client is Manager-facing and mapped under Payments / orderhistory.
        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakePayments) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Payments))
        {
            return;
        }

        try
        {
            var dateRaw = context.Request.Query["date"].ToString();
            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(dateRaw) &&
                DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
            {
                date = parsedDate.Date;
            }

            var page = 1;
            if (int.TryParse(context.Request.Query["page"], out var parsedPage))
            {
                page = parsedPage;
            }

            var pageSize = ClientOrderHistoryService.DefaultPageSize;
            if (int.TryParse(context.Request.Query["pageSize"], out var parsedSize))
            {
                pageSize = parsedSize;
            }

            var history = new ClientOrderHistoryService(_databaseService);
            var result = await history.SearchAsync(
                new ClientOrderHistoryRequest(
                    date,
                    context.Request.Query["orderType"].ToString(),
                    context.Request.Query["search"].ToString(),
                    page,
                    pageSize),
                context.RequestAborted);

            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = result.Success,
                message = result.Message,
                completed = result.Completed,
                voided = result.Voided,
                hasNextPage = result.HasNextPage,
                page = Math.Max(1, page),
                pageSize = Math.Clamp(
                    pageSize <= 0 ? ClientOrderHistoryService.DefaultPageSize : pageSize,
                    1,
                    ClientOrderHistoryService.MaxPageSize)
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order-history failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not load order history."
            });
        }
    }

    private async Task HandleOrderHistoryDetailAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakePayments) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Payments))
        {
            return;
        }

        try
        {
            var orderId = context.Request.RouteValues["orderId"]?.ToString();
            var history = new ClientOrderHistoryService(_databaseService);
            var result = await history.GetDetailAsync(orderId, context.RequestAborted);
            var status = result.Success
                ? HttpStatusCode.OK
                : string.Equals(result.ErrorCode, OrderHistoryErrorCodes.NotFound, StringComparison.OrdinalIgnoreCase)
                    ? HttpStatusCode.NotFound
                    : string.Equals(result.ErrorCode, OrderHistoryErrorCodes.Validation, StringComparison.OrdinalIgnoreCase)
                        ? HttpStatusCode.BadRequest
                        : HttpStatusCode.BadRequest;

            await WriteJsonAsync(context, status, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client order-history detail failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not load this order.",
                errorCode = OrderHistoryErrorCodes.Unknown
            });
        }
    }

    private async Task HandleListReservationsAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Reservations))
        {
            return;
        }

        try
        {
            var from = ParseDateQuery(context.Request.Query["from"], DateTime.Today.AddDays(-7));
            var to = ParseDateQuery(context.Request.Query["to"], DateTime.Today.AddMonths(1).AddDays(7));
            var reservations = await _operational.ListReservationsAsync(from, to);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                reservations
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client reservation list failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not load reservations."
            });
        }
    }

    private async Task HandleCreateReservationAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "client-reservations", 40, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Reservations))
        {
            return;
        }

        var request = await ReadJsonAsync<ClientReservationCreateHttpRequest>(context);
        if (request == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A reservation payload is required."
            });
            return;
        }

        if (!TryParseReservationDate(request.ReservationDate, out var reservationDate) ||
            !TryParseReservationTime(request.ReservationTime, out var reservationTime))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A valid booking date and time are required."
            });
            return;
        }

        try
        {
            var result = await _operational.CreateReservationAsync(new ClientReservationCreateRequest(
                reservationDate,
                reservationTime,
                request.Covers,
                request.CustomerName,
                request.CustomerPhone,
                request.CustomerEmail,
                request.PromoCode,
                request.Notes,
                request.Allergies,
                request.TableNumber,
                request.Channel));
            if (!result.Success || result.Reservation == null)
            {
                await WriteJsonAsync(context, (HttpStatusCode)result.StatusCode, new
                {
                    success = false,
                    message = result.Message
                });
                return;
            }

            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                message = result.Message,
                reservation = result.Reservation
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client reservation create failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not save this reservation."
            });
        }
    }

    private async Task HandleUpdateReservationStatusAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "client-reservations-status", 60, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Reservations))
        {
            return;
        }

        var request = await ReadJsonAsync<ClientReservationStatusHttpRequest>(context);
        if (request == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A reservation status payload is required."
            });
            return;
        }

        try
        {
            var result = await _operational.UpdateReservationStatusAsync(
                request.CloudId ?? request.Id,
                request.LocalId,
                request.Status);
            if (!result.Success)
            {
                await WriteJsonAsync(context, (HttpStatusCode)result.StatusCode, new
                {
                    success = false,
                    message = result.Message
                });
                return;
            }

            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client reservation status failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not update this reservation."
            });
        }
    }

    private async Task HandleSyncReservationsAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        if (!await EnsureCapabilityAsync(context, session, PosCapabilityKeys.TakeOrders) ||
            !await EnsureFeatureAsync(context, session, PosFeatureKeys.Reservations))
        {
            return;
        }

        try
        {
            var date = ParseDateQuery(context.Request.Query["date"], DateTime.Today);
            var result = await _operational.SyncDateAsync(date);
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = result.Success,
                message = result.Message,
                reservations = result.Reservations
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client reservation sync failed: {ex.GetType().Name}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not sync website reservations."
            });
        }
    }

    private void OnReservationSyncCompleted(object? sender, ReservationSyncCompletedEventArgs e)
    {
        if (e.Result.NewReservations == 0 && e.Result.UpdatedReservations == 0)
        {
            return;
        }

        _ = PublishDataChangedAsync("reservation.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
    }

    private static DateTime ParseDateQuery(string? value, DateTime fallback)
    {
        return DateTime.TryParse(value, out var parsed) ? parsed.Date : fallback.Date;
    }

    private static bool TryParseReservationDate(string? value, out DateTime date)
    {
        if (DateTime.TryParse(value, out var parsed))
        {
            date = parsed.Date;
            return true;
        }

        date = default;
        return false;
    }

    private static bool TryParseReservationTime(string? value, out TimeSpan time)
    {
        if (TimeSpan.TryParse(value, out time))
        {
            return true;
        }

        if (DateTime.TryParse(value, out var parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        time = default;
        return false;
    }

    private Task HandleHealthAsync(HttpContext context)
    {
        var config = TerminalConfigurationService.GetConfiguration();
        return WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            status = "ok",
            role = config.ModeDisplay,
            terminalName = config.TerminalName,
            apiPort = Port,
            motherIp = TerminalNetworkInfoService.GetBestLocalIpAddress(),
            websocketPath = "/ws",
            connectedClients = ConnectedClientCount,
            serverTime = DateTimeOffset.Now
        });
    }

    private async Task HandleCompatibilityAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ClientCompatibilityRequest>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.AppVersion) || string.IsNullOrWhiteSpace(request.TerminalId))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "App version and terminal ID are required." });
            return;
        }

        var terminal = await ValidateTerminalTokenAsync(context);
        if (!terminal.Success || !string.Equals(terminal.TerminalId, request.TerminalId, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { success = false, message = "Terminal authorization failed." });
            return;
        }

        const string minimumVersion = "1.0.0";
        const int minimumSchemaVersion = 1;
        const int minimumPayloadVersion = 1;
        var status = CompareVersions(request.AppVersion, minimumVersion) < 0 ||
                     request.SchemaVersion < minimumSchemaVersion ||
                     request.PayloadVersion < minimumPayloadVersion
            ? ClientCompatibilityStatus.UpdateRequired
            : CompareVersions(request.AppVersion, "1.1.0") < 0
                ? ClientCompatibilityStatus.UpdateRecommended
                : ClientCompatibilityStatus.Supported;
        var result = new ClientCompatibilityResult(status, minimumVersion,
            status == ClientCompatibilityStatus.UpdateRequired
                ? "This Client version cannot safely perform current POS operations. Update is required."
                : status == ClientCompatibilityStatus.UpdateRecommended
                    ? "A Client update is recommended."
                    : "Client is compatible.");
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = true, result });
    }

    private static int CompareVersions(string left, string right)
    {
        _ = Version.TryParse(left.Split('-', '+')[0], out var leftVersion);
        _ = Version.TryParse(right.Split('-', '+')[0], out var rightVersion);
        return (leftVersion ?? new Version(0, 0)).CompareTo(rightVersion ?? new Version(0, 0));
    }

    private static bool TryValidateRequestCompatibility(HttpContext context, out string message)
    {
        const string minimumVersion = "1.0.0";
        var appVersion = context.Request.Headers["X-POS-App-Version"].ToString();
        var build = context.Request.Headers["X-POS-Build-Number"].ToString();
        var platform = context.Request.Headers["X-POS-Platform"].ToString();
        var schemaOk = int.TryParse(context.Request.Headers["X-POS-Schema-Version"], out var schema) && schema >= 1;
        var payloadOk = int.TryParse(context.Request.Headers["X-POS-Payload-Version"], out var payload) && payload >= 1;
        if (string.IsNullOrWhiteSpace(appVersion) || string.IsNullOrWhiteSpace(build) || string.IsNullOrWhiteSpace(platform) ||
            CompareVersions(appVersion, minimumVersion) < 0 || !schemaOk || !payloadOk)
        {
            message = $"This Client is incompatible. Update to at least version {minimumVersion}.";
            return false;
        }
        message = string.Empty;
        return true;
    }

    private async Task HandleBootstrapAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "bootstrap", 5, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }
        var request = await ReadJsonAsync<ClientBootstrapRequest>(context);
        if (request == null ||
            string.IsNullOrWhiteSpace(request.TerminalName) ||
            string.IsNullOrWhiteSpace(request.PairingCode))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "Terminal name and pairing code are required."
            });
            return;
        }

        var result = await ActivateClientTerminalAsync(request, GetRemoteIp(context));
        if (!result.Success)
        {
            await WriteJsonAsync(context, result.StatusCode, new
            {
                success = false,
                message = result.Message
            });
            return;
        }

        await WriteJsonAsync(context, HttpStatusCode.OK, result.Bootstrap);
    }

    private async Task HandleLoginAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "login", 10, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }
        var request = await ReadJsonAsync<ClientLoginRequest>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Pin))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "PIN is required."
            });
            return;
        }

        var terminal = await ValidateTerminalTokenAsync(context, request.TerminalToken);
        if (!terminal.Success)
        {
            await WriteJsonAsync(context, terminal.StatusCode, new
            {
                success = false,
                message = terminal.Message
            });
            return;
        }
        if (!TryValidateRequestCompatibility(context, out var compatibilityMessage))
        {
            await WriteJsonAsync(context, HttpStatusCode.UpgradeRequired, new { success = false, message = compatibilityMessage });
            return;
        }

        var login = await _authenticationService.ValidatePinAsync(request.Pin.Trim());
        if (!login.Success || login.User == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new
            {
                success = false,
                message = login.Message
            });
            return;
        }
        if (!login.User.IsActive)
        {
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new
            {
                success = false,
                message = "This staff member is inactive."
            });
            return;
        }
        if (login.User.Role == UserRole.Admin)
        {
            // A Client terminal is for daily operations only. Do this check before
            // issuing a session so an Administrator can never receive a Client token.
            await AuditSensitiveOperationAsync(terminal.TerminalId, login.User.Id, "client_admin_login_rejected", "denied");
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new
            {
                success = false,
                errorCode = "admin_mother_only",
                message = "Administrator access is available on the Mother POS only. Please use the Mother POS terminal."
            });
            return;
        }

        var sessionToken = CreateToken();
        var expiresAt = DateTime.UtcNow.AddHours(12);
        await StoreClientSessionAsync(terminal.TerminalId, login.User, HashToken(sessionToken), expiresAt);
        await UpdateCurrentUserAsync(terminal.TerminalId, login.User.Id);
        await AuditSensitiveOperationAsync(terminal.TerminalId, login.User.Id, "client_session_created", "success");

        var permissions = await _permissionService.GetRolePermissionsAsync(login.User.Role);
        var capabilities = ClientAccessPolicy.FilterCapabilities(MotherCapabilityResolver.ForRole(login.User.Role));
        var features = await _clientAccess.GetGrantedFeaturesAsync(terminal.TerminalId, context.RequestAborted);
        var routes = ClientAccessPolicy.RoutesForFeatures(features);
        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            user_id = login.User.Id,
            userId = login.User.Id,
            name = login.User.Name,
            username = login.User.Username,
            role = login.User.Role.ToString(),
            session_token = sessionToken,
            sessionToken,
            expires_at = expiresAt,
            expiresAt,
            permissions,
            capabilities,
            features,
            routes,
            restaurant_name = TerminalConfigurationService.GetConfiguration().DatabaseName,
            restaurantName = TerminalConfigurationService.GetConfiguration().DatabaseName,
            // Keep the flat fields for existing till clients and also provide
            // the payload envelope consumed by OrderWeb.Client.
            payload = new
            {
                userId = login.User.Id.ToString(),
                userName = login.User.Name,
                role = login.User.Role.ToString(),
                permissions,
                capabilities,
                features,
                routes,
                sessionToken,
                expiresAtUtc = expiresAt,
                restaurantName = TerminalConfigurationService.GetConfiguration().DatabaseName
            }
        });
    }

    private async Task HandleClientAccessAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        var features = await _clientAccess.GetGrantedFeaturesAsync(session.TerminalId, context.RequestAborted);
        var routes = ClientAccessPolicy.RoutesForFeatures(features);
        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            terminalId = session.TerminalId,
            features,
            routes,
            generatedUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task HandleHeartbeatAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ClientHeartbeatRequest>(context) ?? new ClientHeartbeatRequest();
        var terminal = await ValidateTerminalTokenAsync(context, request.TerminalToken);
        if (!terminal.Success)
        {
            await WriteJsonAsync(context, terminal.StatusCode, new
            {
                success = false,
                message = terminal.Message
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.TerminalId) &&
            !string.Equals(request.TerminalId.Trim(), terminal.TerminalId, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new
            {
                success = false,
                message = "Invalid terminal token."
            });
            return;
        }

        await UpdateHeartbeatAsync(terminal.TerminalId, request, GetRemoteIp(context));

        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            terminalId = terminal.TerminalId,
            status = "online",
            serverTime = DateTimeOffset.Now
        });
    }

    private async Task HandleCustomerSearchAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.ViewCustomers) ||
            !await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.Customers, PosFeatureKeys.Collection, PosFeatureKeys.Delivery))
        {
            return;
        }

        var orderType = context.Request.Query["orderType"].ToString();
        var name = context.Request.Query["name"].ToString();
        var phone = context.Request.Query["phone"].ToString();
        var addressOrPostcode = context.Request.Query["addressOrPostcode"].ToString();
        if (string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone) &&
            string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "Enter a name, phone number, address, or postcode before searching."
            });
            return;
        }

        var customerService = new CustomerDataService();
        var customers = string.Equals(orderType, "delivery", StringComparison.OrdinalIgnoreCase)
            ? await customerService.SearchForDeliveryAsync(addressOrPostcode, name, phone)
            : await customerService.SearchForCollectionAsync(name, phone);

        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            customers = customers.Select(ToClientCustomer).ToList()
        });
    }

    private async Task HandleCustomerUpsertAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ClientCustomerUpsertRequest>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "Customer name and phone number are required."
            });
            return;
        }

        var session = await ValidateClientSessionAsync(context, request.Auth?.SessionToken);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.ManageCustomers) ||
            !await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.Customers, PosFeatureKeys.Collection, PosFeatureKeys.Delivery))
        {
            return;
        }

        var orderTypes = string.IsNullOrWhiteSpace(request.OrderTypes)
            ? "collection"
            : request.OrderTypes.Trim().ToLowerInvariant();
        var isDelivery = orderTypes is "delivery" or "both";
        var isCollection = orderTypes is "collection" or "both";
        if (isDelivery && string.IsNullOrWhiteSpace(request.FullAddress))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "A delivery customer requires an address."
            });
            return;
        }

        var customerService = new CustomerDataService();
        if (isCollection)
        {
            await customerService.UpsertCollectionCustomerAsync(request.Name.Trim(), request.PhoneNumber.Trim());
        }

        if (isDelivery)
        {
            await customerService.UpsertDeliveryCustomerAsync(
                request.Name.Trim(),
                request.PhoneNumber.Trim(),
                request.FullAddress?.Trim() ?? string.Empty,
                request.City,
                request.County,
                request.Postcode);
        }

        var records = await customerService.SearchAsync(
            request.Name,
            request.PhoneNumber,
            request.FullAddress ?? request.Postcode,
            includeCollection: isCollection,
            includeDelivery: isDelivery);
        var customer = records.FirstOrDefault();
        if (customer == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new
            {
                success = false,
                message = "Mother POS could not confirm the customer record after saving it."
            });
            return;
        }

        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            customer = ToClientCustomer(customer)
        });
        await PublishDataChangedAsync("customer.updated", customer.UpdatedAt.Ticks.ToString());
    }

    private async Task HandlePaymentAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "payment", 10, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }
        var request = await ReadJsonAsync<ClientPaymentRequest>(context);
        if (request == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "A payment request is required." });
            return;
        }

        var session = await ValidateClientSessionAsync(context, request.SessionToken);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.TakePayments) ||
            !await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.Payments, PosFeatureKeys.GiftCards, PosFeatureKeys.CustomerPoints))
        {
            return;
        }

        var result = await new MotherPaymentService().TakePaymentAsync(new PaymentRequest(
            request.RequestId,
            session.TerminalId,
            string.Empty,
            request.OrderId,
            request.Method,
            request.Amount,
            DateTimeOffset.UtcNow,
            request.ExpectedOrderRevision,
            request.CorrelationId,
            request.GiftCardNumber,
            request.GiftCardIdempotencyKey,
            request.LoyaltyLookup,
            request.LoyaltyPoints,
            request.LoyaltyIdempotencyKey));

        if (!result.IsSuccess || result.Value == null)
        {
            var status = result.Error?.Code switch
            {
                OrderWeb.Contracts.Results.OperationErrorCode.Validation => HttpStatusCode.BadRequest,
                OrderWeb.Contracts.Results.OperationErrorCode.NotFound => HttpStatusCode.NotFound,
                OrderWeb.Contracts.Results.OperationErrorCode.Conflict => HttpStatusCode.Conflict,
                _ => HttpStatusCode.UnprocessableEntity
            };
            await WriteJsonAsync(context, status, new { success = false, message = result.Error?.Message ?? "Mother POS could not approve the payment." });
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "client_payment", "denied");
            return;
        }

        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = true, payment = result.Value });
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "client_payment", "approved");
        if (string.Equals(
                (request.Method ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_"),
                "loyalty",
                StringComparison.Ordinal) ||
            string.Equals(request.Method, "points", StringComparison.OrdinalIgnoreCase))
        {
            await PublishDataChangedAsync("loyalty.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        }
        // WS order.updated is published from OrderService after payment persist.
        NotifyMotherUiOfClientOrderChange(request.OrderId, null, session.TerminalId);
    }

    private async Task HandlePaymentStatusAsync(HttpContext context)
    {
        var requestId = context.Request.RouteValues["requestId"]?.ToString();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "A payment request ID is required." });
            return;
        }

        var session = await ValidateClientSessionAsync(context, context.Request.Headers["X-Session-Token"].FirstOrDefault());
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.TakePayments) ||
            !await EnsureAnyFeatureAsync(context, session, PosFeatureKeys.Payments, PosFeatureKeys.GiftCards, PosFeatureKeys.CustomerPoints))
        {
            return;
        }

        var result = await new MotherPaymentService().GetResultAsync($"client:{session.TerminalId}:{requestId}");
        if (!result.IsSuccess || result.Value == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { success = false, message = "Mother has no final result for this payment request yet." });
            return;
        }
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = true, payment = result.Value });
    }

    private async Task HandleGiftCardLookupAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "gift-card-lookup", 30, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientGiftCardLookupRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.CardNumber))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: "A gift card number is required.",
                Error: "A gift card number is required.",
                ErrorCode: GiftCardErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientGiftCardRequestAsync(context, request.SessionToken, "client_gift_card_lookup");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosGiftCardService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_lookup", "unavailable");
            return;
        }

        try
        {
            var result = await service.LookupAsync(request.CardNumber, request.Purpose ?? GiftCardLookupPurposes.Redeem);
            var dto = ClientPosGiftCardService.ToLookupDto(result);
            await WriteJsonAsync(context, dto.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, dto);
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_gift_card_lookup",
                dto.Success ? "success" : "denied");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client gift-card lookup failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientGiftCardLookupResponseDto(
                Success: false,
                Message: "Mother POS could not look up the gift card in OrderWeb cloud.",
                Error: "Mother POS could not look up the gift card in OrderWeb cloud.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_lookup", "error");
        }
    }

    private async Task HandleGiftCardSellAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "gift-card-sell", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientGiftCardMutateRequestDto>(context);
        if (request == null || request.Amount <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "A positive gift card sale amount is required.",
                Error: "A positive gift card sale amount is required.",
                ErrorCode: GiftCardErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientGiftCardRequestAsync(context, request.SessionToken, "client_gift_card_sell");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosGiftCardService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_sell", "unavailable");
            return;
        }

        try
        {
            var result = await service.SellAsync(
                request.CardNumber,
                request.Amount,
                request.PaymentMethod ?? "cash",
                request.OrderId,
                request.Description,
                request.IdempotencyKey);
            var dto = ClientPosGiftCardService.ToTransactionDto(result);
            await WriteJsonAsync(context, dto.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, dto);
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_gift_card_sell",
                dto.Success ? "success" : "denied");
            if (dto.Success)
            {
                await PublishDataChangedAsync("giftcard.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client gift-card sell failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother POS could not sell the gift card through OrderWeb cloud.",
                Error: "Mother POS could not sell the gift card through OrderWeb cloud.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_sell", "error");
        }
    }

    private async Task HandleGiftCardTopUpAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "gift-card-top-up", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientGiftCardMutateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.CardNumber) || request.Amount <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "A gift card number and positive top-up amount are required.",
                Error: "A gift card number and positive top-up amount are required.",
                ErrorCode: GiftCardErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientGiftCardRequestAsync(context, request.SessionToken, "client_gift_card_top_up");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosGiftCardService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_top_up", "unavailable");
            return;
        }

        try
        {
            var result = await service.TopUpAsync(
                request.CardNumber,
                request.Amount,
                request.PaymentMethod ?? "cash",
                request.OrderId,
                request.Description,
                request.IdempotencyKey);
            var dto = ClientPosGiftCardService.ToTransactionDto(result);
            await WriteJsonAsync(context, dto.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, dto);
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_gift_card_top_up",
                dto.Success ? "success" : "denied");
            if (dto.Success)
            {
                await PublishDataChangedAsync("giftcard.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client gift-card top-up failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother POS could not top up the gift card through OrderWeb cloud.",
                Error: "Mother POS could not top up the gift card through OrderWeb cloud.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_top_up", "error");
        }
    }

    private async Task HandleGiftCardRedeemAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "gift-card-redeem", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientGiftCardMutateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.CardNumber) || request.Amount <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: "A gift card number and positive redeem amount are required.",
                Error: "A gift card number and positive redeem amount are required.",
                ErrorCode: GiftCardErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientGiftCardRequestAsync(context, request.SessionToken, "client_gift_card_redeem");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosGiftCardService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_redeem", "unavailable");
            return;
        }

        try
        {
            var result = await service.RedeemAsync(
                request.CardNumber,
                request.Amount,
                request.OrderId,
                request.Description,
                request.IdempotencyKey);
            var dto = ClientPosGiftCardService.ToRedeemDto(result);
            await WriteJsonAsync(context, dto.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, dto);
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_gift_card_redeem",
                dto.Success ? "success" : "denied");
            if (dto.Success)
            {
                await PublishDataChangedAsync("giftcard.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client gift-card redeem failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientGiftCardRedeemResponseDto(
                Success: false,
                Message: "Mother POS could not redeem the gift card through OrderWeb cloud.",
                Error: "Mother POS could not redeem the gift card through OrderWeb cloud.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_redeem", "error");
        }
    }

    private async Task HandleGiftCardActivateAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "gift-card-activate", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientGiftCardMutateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.CardNumber) || request.Amount <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "A gift card number and positive activation amount are required.",
                Error: "A gift card number and positive activation amount are required.",
                ErrorCode: GiftCardErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientGiftCardRequestAsync(context, request.SessionToken, "client_gift_card_activate");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosGiftCardService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother gift-card service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_activate", "unavailable");
            return;
        }

        try
        {
            var activate = await service.ActivateAsync(
                request.CardNumber,
                request.Amount,
                request.PaymentMethod ?? "cash",
                request.OrderId,
                request.Description,
                request.IdempotencyKey,
                queueOnRetryableFailure: true);
            var dto = ClientPosGiftCardService.ToTransactionDto(activate.Result, activate.Queued, activate.QueueMessage);
            await WriteJsonAsync(context, dto.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, dto);
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_gift_card_activate",
                activate.Result.Success ? "success" : activate.Queued ? "queued" : "denied");
            if (dto.Success)
            {
                await PublishDataChangedAsync("giftcard.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client gift-card activate failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientGiftCardTransactionResponseDto(
                Success: false,
                Message: "Mother POS could not activate the gift card through OrderWeb cloud.",
                Error: "Mother POS could not activate the gift card through OrderWeb cloud.",
                ErrorCode: GiftCardErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_gift_card_activate", "error");
        }
    }

    private async Task<(ClientSessionValidation Session, bool Ok)?> BeginClientGiftCardRequestAsync(
        HttpContext context,
        string? sessionToken,
        string auditAction)
    {
        var session = await ValidateClientSessionAsync(context, sessionToken);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return null;
        }

        if (!await EnsureFeatureAsync(context, session, PosFeatureKeys.GiftCards))
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, auditAction, "feature_denied");
            return null;
        }

        return (session, true);
    }

    private async Task HandleLoyaltySearchAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-search", 30, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyLookupRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Lookup))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "A phone number or loyalty card lookup is required.",
                Error: "A phone number or loyalty card lookup is required.",
                ErrorCode: LoyaltyErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_search");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_search", "unavailable");
            return;
        }

        try
        {
            var result = await service.SearchAsync(request.Lookup);
            await WriteJsonAsync(
                context,
                result.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                ClientPosLoyaltyService.ToLookupDto(result));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_search",
                result.Success ? "success" : "denied");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty search failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother POS could not look up loyalty in OrderWeb cloud.",
                Error: "Mother POS could not look up loyalty in OrderWeb cloud.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_search", "error");
        }
    }

    private async Task HandleLoyaltyCreateCustomerAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-create", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyCreateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Name))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Customer phone and name are required.",
                Error: "Customer phone and name are required.",
                ErrorCode: LoyaltyErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_create");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_create", "unavailable");
            return;
        }

        try
        {
            var result = await service.CreateCustomerAsync(request.Phone, request.Name, request.Email);
            await WriteJsonAsync(
                context,
                result.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                ClientPosLoyaltyService.ToLookupDto(result));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_create",
                result.Success ? "success" : "denied");
            if (result.Success)
            {
                await PublishDataChangedAsync("loyalty.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty create failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother POS could not create the loyalty customer in OrderWeb cloud.",
                Error: "Mother POS could not create the loyalty customer in OrderWeb cloud.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_create", "error");
        }
    }

    private async Task HandleLoyaltyAddPointsAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-add", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyMutateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Lookup) || request.Points <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "A customer lookup and positive points amount are required.",
                Error: "A customer lookup and positive points amount are required.",
                ErrorCode: LoyaltyErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_add");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_add", "unavailable");
            return;
        }

        try
        {
            var result = await service.AddPointsAsync(
                request.Lookup,
                request.Points,
                request.Reason,
                request.IdempotencyKey);
            await WriteJsonAsync(
                context,
                result.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                ClientPosLoyaltyService.ToLookupDto(result));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_add",
                result.Success ? "success" : "denied");
            if (result.Success)
            {
                await PublishDataChangedAsync("loyalty.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty add failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother POS could not add loyalty points through OrderWeb cloud.",
                Error: "Mother POS could not add loyalty points through OrderWeb cloud.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_add", "error");
        }
    }

    private async Task HandleLoyaltyRedeemPointsAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-redeem", 12, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyMutateRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Lookup) || request.Points <= 0)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "A customer lookup and positive points amount are required.",
                Error: "A customer lookup and positive points amount are required.",
                ErrorCode: LoyaltyErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_redeem");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_redeem", "unavailable");
            return;
        }

        try
        {
            var result = await service.RedeemPointsAsync(
                request.Lookup,
                request.Points,
                request.Reason,
                request.IdempotencyKey);
            await WriteJsonAsync(
                context,
                result.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                ClientPosLoyaltyService.ToLookupDto(result));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_redeem",
                result.Success ? "success" : "denied");
            if (result.Success)
            {
                await PublishDataChangedAsync("loyalty.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty redeem failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother POS could not redeem loyalty points through OrderWeb cloud.",
                Error: "Mother POS could not redeem loyalty points through OrderWeb cloud.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_redeem", "error");
        }
    }

    private async Task HandleLoyaltyHistoryAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-history", 20, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyLookupRequestDto>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.Lookup))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "A phone number or loyalty card lookup is required.",
                Error: "A phone number or loyalty card lookup is required.",
                ErrorCode: LoyaltyErrorCodes.Validation));
            return;
        }

        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_history");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_history", "unavailable");
            return;
        }

        try
        {
            var result = await service.HistoryAsync(request.Lookup);
            await WriteJsonAsync(
                context,
                result.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                ClientPosLoyaltyService.ToLookupDto(result, includeHistory: true));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_history",
                result.Success ? "success" : "denied");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty history failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "Mother POS could not load loyalty history from OrderWeb cloud.",
                Error: "Mother POS could not load loyalty history from OrderWeb cloud.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_history", "error");
        }
    }

    private async Task HandleLoyaltyTestAsync(HttpContext context)
    {
        if (!TryAllowRequest(context, "loyalty-test", 10, TimeSpan.FromMinutes(1)))
        {
            await WriteRateLimitedAsync(context);
            return;
        }

        var request = await ReadJsonAsync<ClientLoyaltyLookupRequestDto>(context) ?? new ClientLoyaltyLookupRequestDto();
        var gate = await BeginClientLoyaltyRequestAsync(context, request.SessionToken, "client_loyalty_test");
        if (gate == null)
        {
            return;
        }

        var service = ClientPosLoyaltyService.TryResolve();
        if (service == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                Error: "Mother loyalty service is not available. Check OrderWeb cloud settings on Mother POS.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_test", "unavailable");
            return;
        }

        try
        {
            var test = await service.TestConnectionAsync(request.Lookup);
            await WriteJsonAsync(
                context,
                test.Success ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity,
                new ClientLoyaltyTestResponseDto(
                    Success: test.Success,
                    Message: test.Message,
                    Error: test.Success ? null : test.Message,
                    ErrorCode: test.Success ? null : test.ErrorCode ?? LoyaltyErrorCodes.Unknown));
            await AuditSensitiveOperationAsync(
                gate.Value.Session.TerminalId,
                gate.Value.Session.UserId,
                "client_loyalty_test",
                test.Success ? "success" : "denied");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client loyalty test failed: {ex.GetType().Name}: {ex.Message}");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new ClientLoyaltyTestResponseDto(
                Success: false,
                Message: "Mother POS could not test the OrderWeb loyalty connection.",
                Error: "Mother POS could not test the OrderWeb loyalty connection.",
                ErrorCode: LoyaltyErrorCodes.CloudDown));
            await AuditSensitiveOperationAsync(gate.Value.Session.TerminalId, gate.Value.Session.UserId, "client_loyalty_test", "error");
        }
    }

    private async Task<(ClientSessionValidation Session, bool Ok)?> BeginClientLoyaltyRequestAsync(
        HttpContext context,
        string? sessionToken,
        string auditAction)
    {
        var session = await ValidateClientSessionAsync(context, sessionToken);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: session.Message,
                Error: session.Message,
                ErrorCode: LoyaltyErrorCodes.AccessDenied));
            return null;
        }

        var granted = await _clientAccess.GetGrantedFeaturesAsync(session.TerminalId, context.RequestAborted);
        if (!granted.Contains(PosFeatureKeys.CustomerPoints))
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, auditAction, "feature_denied");
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"feature_denied:{PosFeatureKeys.CustomerPoints}", "denied");
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new ClientLoyaltyLookupResponseDto(
                Success: false,
                Message: "This Client terminal is not allowed to use Loyalty (Terminal Access).",
                Error: "This Client terminal is not allowed to use Loyalty (Terminal Access).",
                ErrorCode: LoyaltyErrorCodes.AccessDenied));
            return null;
        }

        return (session, true);
    }

    private async Task HandlePrintAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ClientPrintRequest>(context);
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.OrderId))
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "A print request ID and Mother order ID are required." });
            return;
        }

        var session = await ValidateClientSessionAsync(context, request.SessionToken);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }

        var documentType = NormalizeClientPrintDocument(request.DocumentType);
        if (documentType is null)
        {
            await WriteJsonAsync(context, HttpStatusCode.UnprocessableEntity, new
            {
                success = false,
                message = "Supported Client print types are kitchen/bar tickets and customer receipts. Mother prints them on its network/IP printers."
            });
            return;
        }

        if (documentType is "kitchen_ticket")
        {
            if (!await EnsureAnyCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders, PosCapabilityKeys.SubmitOrders, PosCapabilityKeys.PrintReceipts))
            {
                return;
            }
        }
        else
        {
            var requiredCapability = documentType == "reprint"
                ? PosCapabilityKeys.ReprintReceipts
                : PosCapabilityKeys.PrintReceipts;
            if (!await EnsureCapabilityAsync(context, session, requiredCapability))
            {
                return;
            }
        }

        var print = await ExecuteClientPrintDocumentsAsync(
            session,
            request.OrderId.Trim(),
            [documentType],
            request.RequestId.Trim());
        var status = print.Status;
        var succeeded = status is "queued" or "printed" or "partial";
        await WriteJsonAsync(context, succeeded ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, new
        {
            success = succeeded,
            print
        });
    }

    private async Task HandlePrintStatusAsync(HttpContext context)
    {
        var requestId = context.Request.RouteValues["requestId"]?.ToString();
        var session = await ValidateClientSessionAsync(context, context.Request.Headers["X-Session-Token"].FirstOrDefault());
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, message = session.Message });
            return;
        }
        if (!await EnsureAnyCapabilityAsync(context, session, PosCapabilityKeys.CreateOrders, PosCapabilityKeys.PrintReceipts))
        {
            return;
        }
        var audit = string.IsNullOrWhiteSpace(requestId) ? null : await GetPrintAuditAsync(requestId, session.TerminalId);
        if (audit == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { success = false, message = "Mother has no print history for this request." });
            return;
        }
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = audit.Status is "queued" or "printed" or "partial", print = audit });
    }

    /// <summary>
    /// Client never talks to printers. Mother queues kitchen/bar/receipt jobs onto
    /// the configured network/IP printers and returns only the Mother print status.
    /// </summary>
    private async Task<ClientPrintAudit> ExecuteClientPrintDocumentsAsync(
        ClientSessionValidation session,
        string orderId,
        IReadOnlyCollection<string> documents,
        string printRequestId)
    {
        var duplicate = await GetPrintAuditAsync(printRequestId, session.TerminalId);
        if (duplicate != null)
        {
            return duplicate;
        }

        var primaryDocument = documents.Contains("kitchen_ticket")
            ? "kitchen_ticket"
            : documents.Contains("reprint") ? "reprint" : "customer_receipt";

        if (!await TryBeginPrintAuditAsync(printRequestId, session, orderId, primaryDocument))
        {
            return await GetPrintAuditAsync(printRequestId, session.TerminalId)
                ?? new ClientPrintAudit(printRequestId, orderId, primaryDocument, "failed", "Mother is already processing this print request.");
        }

        var order = await new OrderService().GetOrderByExternalIdAsync(orderId);
        if (order == null)
        {
            await StorePrintAuditAsync(printRequestId, session, orderId, primaryDocument, "failed", "Mother POS could not find this order.");
            return new ClientPrintAudit(printRequestId, orderId, primaryDocument, "failed", "Mother POS could not find this order.");
        }

        var printFeature = FeatureForOrderType(order.OrderType);
        if (printFeature != null && !await _clientAccess.HasFeatureAsync(session.TerminalId, printFeature))
        {
            await StorePrintAuditAsync(printRequestId, session, orderId, primaryDocument, "failed", "This terminal is not allowed to print that order type.");
            return new ClientPrintAudit(printRequestId, orderId, primaryDocument, "failed", "This terminal is not allowed to print that order type.");
        }

        var messages = new List<string>();
        var anyQueued = false;
        var anyFailed = false;

        if (documents.Contains("kitchen_ticket"))
        {
            var routing = ServiceHelper.GetService<OrderRoutingPrintService>() ?? new OrderRoutingPrintService();
            var routingResult = await routing.PrintOrderAsync(ToKitchenPrintOrder(order));
            if (routingResult.AnyPrinted)
            {
                anyQueued = true;
                messages.Add(routingResult.HasFailures
                    ? $"Kitchen/bar tickets partly queued on Mother IP printers: {string.Join("; ", routingResult.FailedRoutes)}"
                    : "Kitchen/bar tickets queued on Mother IP printers.");
            }
            else
            {
                anyFailed = true;
                messages.Add(routingResult.FailedRoutes.Count > 0
                    ? $"Kitchen/bar print failed: {string.Join("; ", routingResult.FailedRoutes)}"
                    : "Mother could not queue kitchen/bar tickets; check configured IP printers.");
            }
        }

        if (documents.Contains("customer_receipt") || documents.Contains("reprint"))
        {
            var receiptService = ServiceHelper.GetService<ReceiptService>();
            if (receiptService == null)
            {
                anyFailed = true;
                messages.Add("Mother receipt service is unavailable.");
            }
            else
            {
                var queued = await receiptService.PrintFullCustomerReceiptAsync(order);
                if (queued)
                {
                    anyQueued = true;
                    messages.Add("Customer receipt queued on Mother IP receipt printer.");
                }
                else
                {
                    anyFailed = true;
                    messages.Add("Mother could not queue the receipt; check the configured receipt printer.");
                }
            }
        }

        var status = anyQueued && anyFailed ? "partial" : anyQueued ? "queued" : "failed";
        var message = messages.Count == 0
            ? "Mother did not print any documents."
            : string.Join(" ", messages);
        await StorePrintAuditAsync(printRequestId, session, orderId, primaryDocument, status, message);
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"client_print:{primaryDocument}", status);
        return new ClientPrintAudit(printRequestId, orderId, primaryDocument, status, message);
    }

    private static HashSet<string> ResolveClientPrintDocuments(
        bool printKitchen,
        bool printReceipt,
        IReadOnlyList<string>? printDocuments)
    {
        var documents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (printKitchen)
        {
            documents.Add("kitchen_ticket");
        }
        if (printReceipt)
        {
            documents.Add("customer_receipt");
        }

        if (printDocuments != null)
        {
            foreach (var document in printDocuments)
            {
                var normalized = NormalizeClientPrintDocument(document);
                if (normalized != null)
                {
                    documents.Add(normalized == "reprint" ? "reprint" : normalized);
                }
            }
        }

        return documents;
    }

    private static string? NormalizeClientPrintDocument(string? documentType)
    {
        var value = documentType?.Trim().ToLowerInvariant();
        return value switch
        {
            "kitchen_ticket" or "kitchen" or "kitchen ticket" or "bar_ticket" or "bar" or "bar ticket" => "kitchen_ticket",
            "customer_receipt" or "receipt" or "bill" or "customer receipt" => "customer_receipt",
            "reprint" => "reprint",
            _ => null
        };
    }

    private async Task<bool> EnsureAnyCapabilityAsync(HttpContext context, ClientSessionValidation session, params string[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            if (await HasCapabilityAsync(session, capability))
            {
                return true;
            }
        }

        var denied = capabilities.Length == 1 ? capabilities[0] : string.Join(",", capabilities);
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"capability_denied:{denied}", "denied");
        await WriteJsonAsync(context, HttpStatusCode.Forbidden, new { success = false, message = "Your role is not allowed to perform this operation." });
        return false;
    }

    private async Task<bool> HasCapabilityAsync(ClientSessionValidation session, string capability)
    {
        if (!ClientAccessPolicy.IsCapabilityAllowed(capability))
        {
            return false;
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT role FROM users WHERE id = @userId AND is_active = TRUE LIMIT 1", connection);
        command.Parameters.AddWithValue("@userId", session.UserId);
        var roleValue = await command.ExecuteScalarAsync();
        return Enum.TryParse<UserRole>(roleValue?.ToString(), true, out var role) &&
               role != UserRole.Admin &&
               MotherCapabilityResolver.ForRole(role).Contains(capability);
    }

    private static TableOrder ToKitchenPrintOrder(Order order)
    {
        var printOrder = new TableOrder
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            TableNumber = ParseTableNumber(order),
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            Notes = order.SpecialInstructions,
            OrderMode = string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase) ? "dine_in" : "takeaway",
            StartTime = order.CreatedAt,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
        foreach (var item in order.Items)
        {
            printOrder.Items.Add(new TableOrderItem
            {
                Id = string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId,
                OrderId = order.OrderId,
                MenuItemId = item.MenuItemId ?? string.Empty,
                VariantId = item.VariantId,
                VariantName = item.VariantName,
                DisplayName = item.DisplayName,
                PrintGroupId = item.PrintGroupId,
                PrintInRed = item.PrintInRed,
                Name = item.ItemName,
                UnitPrice = item.ItemPrice ?? 0m,
                Quantity = Math.Max(1, item.Quantity),
                Notes = item.SpecialInstructions,
                SendStatus = ItemSendStatus.NotSent
            });
        }
        return printOrder;
    }

    private static int ParseTableNumber(Order order)
    {
        if (!string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var value = order.CustomerName?.Trim();
        if (value?.StartsWith("Table ", StringComparison.OrdinalIgnoreCase) == true)
        {
            value = value[6..].Trim();
        }
        return int.TryParse(value, out var tableNumber) ? tableNumber : 0;
    }

    private async Task<HashSet<int>> LoadFloorIdsWithBackgroundImagesAsync()
    {
        var ids = new HashSet<int>();
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                @"SELECT FloorId FROM FloorBackgroundImages
                  WHERE ImageData IS NOT NULL AND LENGTH(ImageData) > 0",
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    ids.Add(reader.GetInt32(0));
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client layout floor backgrounds lookup failed: {ex.GetType().Name}: {ex.Message}");
        }

        return ids;
    }

    private async Task HandleImageAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            context.Response.StatusCode = (int)session.StatusCode;
            return;
        }

        var imageId = context.Request.RouteValues["imageId"]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(imageId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand { Connection = connection };
        if (imageId.Equals("restaurant-logo", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = @"SELECT logo_data, logo_mime_type, logo_content_hash FROM business_info
                                    WHERE logo_data IS NOT NULL AND logo_content_hash IS NOT NULL
                                    ORDER BY updated_at DESC, id DESC LIMIT 1";
        }
        else if (imageId.StartsWith("floor-", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(imageId["floor-".Length..], out var floorId))
        {
            command.CommandText = @"SELECT ImageData, MimeType, ContentHash FROM FloorBackgroundImages
                                    WHERE FloorId = @floorId LIMIT 1";
            command.Parameters.AddWithValue("@floorId", floorId);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var bytes = reader.IsDBNull(0) ? null : (byte[])reader[0];
        var mime = reader.IsDBNull(1) ? "image/jpeg" : reader.GetString(1);
        var hash = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        if (bytes == null || bytes.Length == 0 || !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = mime;
        context.Response.Headers.ETag = $"\"{hash}\"";
        context.Response.Headers["X-Image-Hash"] = hash;
        context.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = "WebSocket request required."
            });
            return;
        }

        var terminal = await ValidateTerminalTokenAsync(context);
        if (!terminal.Success)
        {
            context.Response.StatusCode = (int)terminal.StatusCode;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new ClientWebSocketConnection(terminal.TerminalId, webSocket);
        _clients[terminal.TerminalId] = connection;
        await UpdateWebSocketConnectionStateAsync(terminal.TerminalId, connected: true, updateLastMessage: true);

        try
        {
            await SendWebSocketJsonAsync(webSocket, new
            {
                type = "CONNECTED",
                terminalId = terminal.TerminalId,
                serverTime = DateTimeOffset.Now
            }, context.RequestAborted);

            var buffer = new byte[2048];
            while (!context.RequestAborted.IsCancellationRequested &&
                   webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted);
                connection.LastMessageAt = DateTime.Now;
                await UpdateWebSocketLastMessageAsync(terminal.TerminalId);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        finally
        {
            _clients.TryRemove(terminal.TerminalId, out _);
            await UpdateWebSocketConnectionStateAsync(terminal.TerminalId, connected: false, updateLastMessage: false);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closed by Mother POS",
                    CancellationToken.None);
            }
        }
    }

    private async Task UpdateWebSocketConnectionStateAsync(string terminalId, bool connected, bool updateLastMessage)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);

            await using var command = new MySqlCommand(@"
                UPDATE terminal_pairings
                SET websocket_status = @status,
                    websocket_connected_at = CASE WHEN @connected THEN NOW() ELSE websocket_connected_at END,
                    websocket_disconnected_at = CASE WHEN @connected THEN websocket_disconnected_at ELSE NOW() END,
                    websocket_last_message_at = CASE WHEN @updateLastMessage THEN NOW() ELSE websocket_last_message_at END,
                    updated_at = NOW()
                WHERE terminal_id = @terminalId", connection);
            command.Parameters.AddWithValue("@terminalId", terminalId);
            command.Parameters.AddWithValue("@status", connected ? "connected" : "disconnected");
            command.Parameters.AddWithValue("@connected", connected);
            command.Parameters.AddWithValue("@updateLastMessage", updateLastMessage);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"WebSocket state update failed for terminal {terminalId}: {ex.Message}");
        }
    }

    private async Task UpdateWebSocketLastMessageAsync(string terminalId)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);

            await using var command = new MySqlCommand(@"
                UPDATE terminal_pairings
                SET websocket_last_message_at = NOW(),
                    updated_at = NOW()
                WHERE terminal_id = @terminalId", connection);
            command.Parameters.AddWithValue("@terminalId", terminalId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"WebSocket message timestamp update failed for terminal {terminalId}: {ex.Message}");
        }
    }

    private async Task<ClientActivationResult> ActivateClientTerminalAsync(ClientBootstrapRequest request, string? remoteIp)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return ClientActivationResult.Fail(
                "Client POS pairing is allowed on the Mother terminal only.",
                HttpStatusCode.Forbidden);
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);

        var terminalName = request.TerminalName.Trim();
        var code = NormalizePairingCode(request.PairingCode);

        var lockout = await TerminalPairingService.GetPairingLockoutAsync(connection, terminalName);
        if (lockout.IsLocked)
        {
            return ClientActivationResult.Fail(lockout.Message, HttpStatusCode.TooManyRequests);
        }

        string? failureReason = null;
        string? failureMessage = null;
        await using (var selectCommand = new MySqlCommand(@"
            SELECT pairing_code, pairing_expires_at, paired_at, disabled_at
            FROM terminal_pairings
            WHERE terminal_name = @terminalName
            LIMIT 1", connection))
        {
            selectCommand.Parameters.AddWithValue("@terminalName", terminalName);
            await using var reader = await selectCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                failureReason = "unknown_terminal";
                failureMessage = "This terminal has not been added on the Mother terminal.";
            }
            else if (!reader.IsDBNull(reader.GetOrdinal("disabled_at")))
            {
                return ClientActivationResult.Fail(
                    "This terminal is disabled on the Mother terminal.",
                    HttpStatusCode.Forbidden);
            }
            else if (!reader.IsDBNull(reader.GetOrdinal("paired_at")))
            {
                return ClientActivationResult.Fail(
                    "This pairing code has already been used. Create a new code on the Mother terminal.",
                    HttpStatusCode.Conflict);
            }
            else
            {
                var storedCode = reader.IsDBNull(reader.GetOrdinal("pairing_code"))
                    ? string.Empty
                    : reader.GetString("pairing_code");
                if (!string.Equals(storedCode, code, StringComparison.Ordinal))
                {
                    failureReason = "wrong_code";
                    failureMessage = "Pairing code is incorrect.";
                }
            }
        }

        if (failureReason != null)
        {
            await TerminalPairingService.RegisterFailedPairingAttemptAsync(connection, terminalName, failureReason, remoteIp);
            return ClientActivationResult.Fail(failureMessage ?? "Pairing failed.", HttpStatusCode.Unauthorized);
        }

        var terminalId = Guid.NewGuid().ToString("N");
        var token = CreateToken();
        var tokenHash = HashToken(token);
        var restaurantSlug = GetRestaurantSlug();
        var lastSyncEventId = await GetLastSyncEventIdAsync(connection);
        var bootstrap = CreateBootstrapResponse(request, terminalId, token, lastSyncEventId);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var updatePairingCommand = new MySqlCommand(@"
                UPDATE terminal_pairings
                SET terminal_id = @terminalId,
                    device_type = @deviceType,
                    platform = @platform,
                    device_id = @deviceId,
                    device_name = @deviceName,
                    token_hash = @tokenHash,
                    paired_at = NOW(),
                    last_seen_at = NOW(),
                    app_version = @appVersion,
                    client_status = 'online',
                    enabled = TRUE,
                    last_ip_address = @lastIpAddress,
                    last_sync_event_id = @lastSyncEventId,
                    restaurant_slug = @restaurantSlug,
                    websocket_status = 'disconnected',
                    websocket_connected_at = NULL,
                    websocket_disconnected_at = NULL,
                    websocket_last_message_at = NULL,
                    pairing_code = NULL,
                    pairing_expires_at = NULL,
                    revoked_at = NULL,
                    updated_at = NOW()
                WHERE terminal_name = @terminalName
                  AND pairing_code = @pairingCode
                  AND paired_at IS NULL
                  AND disabled_at IS NULL", connection, transaction))
            {
                updatePairingCommand.Parameters.AddWithValue("@terminalId", terminalId);
                updatePairingCommand.Parameters.AddWithValue("@deviceType", request.DeviceType ?? string.Empty);
                updatePairingCommand.Parameters.AddWithValue("@platform", request.Platform ?? string.Empty);
                updatePairingCommand.Parameters.AddWithValue("@deviceId", request.DeviceId ?? string.Empty);
                updatePairingCommand.Parameters.AddWithValue("@deviceName", request.DeviceName ?? terminalName);
                updatePairingCommand.Parameters.AddWithValue("@tokenHash", tokenHash);
                updatePairingCommand.Parameters.AddWithValue("@appVersion", request.AppVersion ?? string.Empty);
                updatePairingCommand.Parameters.AddWithValue("@lastIpAddress", remoteIp ?? string.Empty);
                updatePairingCommand.Parameters.AddWithValue("@lastSyncEventId", lastSyncEventId);
                updatePairingCommand.Parameters.AddWithValue("@restaurantSlug", restaurantSlug);
                updatePairingCommand.Parameters.AddWithValue("@terminalName", terminalName);
                updatePairingCommand.Parameters.AddWithValue("@pairingCode", code);

                var rows = await updatePairingCommand.ExecuteNonQueryAsync();
                if (rows <= 0)
                {
                    await transaction.RollbackAsync();
                    return ClientActivationResult.Fail(
                        "Could not activate pairing. Create a new code and try again.",
                        HttpStatusCode.Conflict);
                }
            }

            await UpsertTerminalHealthAsync(connection, transaction, terminalName, request.AppVersion, remoteIp, "online", string.Empty);
            await TerminalPairingService.ClearPairingAttemptsAsync(connection, transaction, terminalName);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return ClientActivationResult.Ok(terminalId, token, lastSyncEventId, bootstrap);
    }

    private ClientBootstrapResponse CreateBootstrapResponse(
        ClientBootstrapRequest request,
        string terminalId,
        string terminalToken,
        long lastSyncEventId)
    {
        var config = TerminalConfigurationService.GetConfiguration();
        var terminalName = request.TerminalName.Trim();
        var motherIp = TerminalNetworkInfoService.GetBestLocalIpAddress();
        var websocketUrl = $"ws://{motherIp}:{Port}/ws";
        var deviceType = string.IsNullOrWhiteSpace(request.DeviceType) ? "Client" : request.DeviceType.Trim();
        var platform = string.IsNullOrWhiteSpace(request.Platform) ? DeviceInfo.Platform.ToString() : request.Platform.Trim();
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? terminalName : request.DeviceName.Trim();

        return new ClientBootstrapResponse(
            Success: true,
            TerminalId: terminalId,
            TerminalToken: terminalToken,
            TerminalName: terminalName,
            ApiPort: Port,
            WebsocketUrl: websocketUrl,
            LoginRequired: true,
            Restaurant: new BootstrapRestaurantInfo(
                Name: config.DatabaseName,
                Slug: GetRestaurantSlug()),
            TerminalIdentity: new BootstrapTerminalIdentity(
                TerminalId: terminalId,
                TerminalName: terminalName,
                DeviceType: deviceType,
                Platform: platform,
                DeviceId: request.DeviceId ?? string.Empty,
                DeviceName: deviceName,
                AppVersion: request.AppVersion ?? string.Empty),
            TerminalConfig: new BootstrapTerminalConfig(
                MotherIp: motherIp,
                ApiPort: Port,
                WebsocketPath: "/ws",
                HeartbeatPath: "/terminals/heartbeat",
                LoginPath: "/api/client/login",
                HeartbeatSeconds: 30),
            Sync: new BootstrapSyncInfo(
                Checkpoint: lastSyncEventId,
                FullSyncRequired: true),
            Auth: new BootstrapAuthInfo(
                LoginRequired: true,
                TerminalTokenRequired: true,
                UserSessionRequired: true,
                PinLoginEnabled: true),
            Features: new BootstrapFeatureFlags(
                WebSocketEnabled: true,
                HeartbeatEnabled: true,
                MenuBootstrapEnabled: false,
                OrdersBootstrapEnabled: false,
                CustomerBootstrapEnabled: false),
            ServerTime: DateTimeOffset.Now);
    }

    private async Task<TerminalTokenValidation> ValidateTerminalTokenAsync(HttpContext context, string? suppliedToken = null)
    {
        var token = string.IsNullOrWhiteSpace(suppliedToken)
            ? GetBearerToken(context)
            : suppliedToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return TerminalTokenValidation.Fail("Terminal token is required.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);

        await using var command = new MySqlCommand(@"
            SELECT terminal_id, terminal_name, enabled, disabled_at, revoked_at, restaurant_slug
            FROM terminal_pairings
            WHERE token_hash = @tokenHash
              AND paired_at IS NOT NULL
            LIMIT 1", connection);
        command.Parameters.AddWithValue("@tokenHash", HashToken(token));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return TerminalTokenValidation.Fail("Invalid terminal token.");
        }

        var enabled = reader.IsDBNull(reader.GetOrdinal("enabled")) || reader.GetBoolean("enabled");
        if (!enabled || !reader.IsDBNull(reader.GetOrdinal("disabled_at")))
        {
            return TerminalTokenValidation.Fail("Terminal disabled.", HttpStatusCode.Forbidden);
        }

        if (!reader.IsDBNull(reader.GetOrdinal("revoked_at")))
        {
            return TerminalTokenValidation.Fail("Terminal token revoked.");
        }

        var storedRestaurantSlug = reader.IsDBNull(reader.GetOrdinal("restaurant_slug"))
            ? string.Empty
            : reader.GetString("restaurant_slug");
        if (!string.Equals(storedRestaurantSlug, GetRestaurantSlug(), StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTokenValidation.Fail("Invalid terminal token.");
        }

        var terminalId = reader.GetString("terminal_id");
        var claimedTerminalId = context.Request.Headers["X-Terminal-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(claimedTerminalId) && !string.Equals(claimedTerminalId, terminalId, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTokenValidation.Fail("Terminal identity does not match its token.");
        }

        return TerminalTokenValidation.Ok(terminalId, reader.GetString("terminal_name"));
    }

    private bool TryAllowRequest(HttpContext context, string operation, int limit, TimeSpan window)
    {
        var remote = GetRemoteIp(context) ?? "unknown";
        var key = $"{operation}:{remote}";
        var queue = _rateLimitWindows.GetOrAdd(key, _ => new ConcurrentQueue<DateTimeOffset>());
        var now = DateTimeOffset.UtcNow;
        while (queue.TryPeek(out var oldest) && now - oldest > window)
        {
            queue.TryDequeue(out _);
        }
        if (queue.Count >= limit)
        {
            return false;
        }
        queue.Enqueue(now);
        return true;
    }

    private static Task WriteRateLimitedAsync(HttpContext context) =>
        WriteJsonAsync(context, HttpStatusCode.TooManyRequests, new { success = false, message = "Too many requests. Try again shortly." });

    private async Task AuditSensitiveOperationAsync(string terminalId, int userId, string action, string outcome)
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);
            await using var command = new MySqlCommand(@"
                INSERT INTO client_security_audit (terminal_id, user_id, action, outcome, created_at)
                VALUES (@terminalId, @userId, @action, @outcome, UTC_TIMESTAMP())", connection);
            command.Parameters.AddWithValue("@terminalId", terminalId);
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@action", action);
            command.Parameters.AddWithValue("@outcome", outcome);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Audit failures must not disclose credentials or block a completed payment.
            AppDiagnostics.Log($"Client security audit write failed: {ex.GetType().Name}");
        }
    }

    private async Task<ClientPrintAudit?> GetPrintAuditAsync(string requestId, string terminalId)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        await using var command = new MySqlCommand(@"
            SELECT request_id, order_id, document_type, status, message
            FROM client_print_audit
            WHERE request_id = @requestId AND terminal_id = @terminalId
            LIMIT 1", connection);
        command.Parameters.AddWithValue("@requestId", requestId);
        command.Parameters.AddWithValue("@terminalId", terminalId);
        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new ClientPrintAudit(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4))
            : null;
    }

    private async Task StorePrintAuditAsync(string requestId, ClientSessionValidation session, string orderId, string? documentType, string status, string message)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        await using var command = new MySqlCommand(@"
            INSERT INTO client_print_audit
                (request_id, terminal_id, user_id, order_id, document_type, status, message, created_at, completed_at)
            VALUES
                (@requestId, @terminalId, @userId, @orderId, @documentType, @status, @message, UTC_TIMESTAMP(), UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE
                status = @status,
                message = @message,
                completed_at = UTC_TIMESTAMP()", connection);
        command.Parameters.AddWithValue("@requestId", requestId);
        command.Parameters.AddWithValue("@terminalId", session.TerminalId);
        command.Parameters.AddWithValue("@userId", session.UserId);
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@documentType", documentType ?? string.Empty);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> TryBeginPrintAuditAsync(string requestId, ClientSessionValidation session, string orderId, string? documentType)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        await using var command = new MySqlCommand(@"
            INSERT IGNORE INTO client_print_audit
                (request_id, terminal_id, user_id, order_id, document_type, status, message, created_at)
            VALUES
                (@requestId, @terminalId, @userId, @orderId, @documentType, 'processing', 'Mother is processing this print request.', UTC_TIMESTAMP())", connection);
        command.Parameters.AddWithValue("@requestId", requestId);
        command.Parameters.AddWithValue("@terminalId", session.TerminalId);
        command.Parameters.AddWithValue("@userId", session.UserId);
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@documentType", documentType ?? string.Empty);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    // Customer data is deliberately authorized by Mother for every request. A
    // Child terminal's pairing token alone is not enough: an active staff
    // session, issued by Mother for that exact terminal, is required too.
    private async Task<ClientSessionValidation> ValidateClientSessionAsync(HttpContext context, string? suppliedSessionToken = null)
    {
        var terminal = await ValidateTerminalTokenAsync(context);
        if (!terminal.Success)
        {
            return ClientSessionValidation.Fail(terminal.Message, terminal.StatusCode);
        }
        if (!TryValidateRequestCompatibility(context, out var compatibilityMessage))
        {
            return ClientSessionValidation.Fail(compatibilityMessage, HttpStatusCode.UpgradeRequired);
        }

        var sessionToken = string.IsNullOrWhiteSpace(suppliedSessionToken)
            ? context.Request.Headers["X-Session-Token"].ToString()
            : suppliedSessionToken.Trim();
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return ClientSessionValidation.Fail("An active staff session is required.");
        }

        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);

        await using var command = new MySqlCommand(@"
            SELECT s.user_id, u.role
            FROM client_user_sessions s
            INNER JOIN users u ON u.id = s.user_id
            WHERE s.terminal_id = @terminalId
              AND s.session_token_hash = @sessionHash
              AND s.expires_at > UTC_TIMESTAMP()
               AND s.revoked_at IS NULL
               AND u.is_active = TRUE
               AND COALESCE(u.is_archived, FALSE) = FALSE
            LIMIT 1", connection);
        command.Parameters.AddWithValue("@terminalId", terminal.TerminalId);
        command.Parameters.AddWithValue("@sessionHash", HashToken(sessionToken));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return ClientSessionValidation.Fail("Your staff session has expired or is no longer allowed.", HttpStatusCode.Unauthorized);
        }

        var userId = reader.GetInt32(0);
        var roleValue = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        if (Enum.TryParse<UserRole>(roleValue, true, out var role) && role == UserRole.Admin)
        {
            await AuditSensitiveOperationAsync(terminal.TerminalId, userId, "client_admin_session_rejected", "denied");
            return ClientSessionValidation.Fail(
                "Administrator access is available on the Mother POS only. Please use the Mother POS terminal.",
                HttpStatusCode.Forbidden);
        }

        return ClientSessionValidation.Ok(terminal.TerminalId, userId);
    }

    private async Task<bool> EnsureCapabilityAsync(HttpContext context, ClientSessionValidation session, string capability)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new MySqlCommand("SELECT role FROM users WHERE id = @userId AND is_active = TRUE LIMIT 1", connection);
        command.Parameters.AddWithValue("@userId", session.UserId);
        var roleValue = await command.ExecuteScalarAsync(context.RequestAborted);
        if (!Enum.TryParse<UserRole>(roleValue?.ToString(), true, out var role) ||
            role == UserRole.Admin ||
            !ClientAccessPolicy.IsCapabilityAllowed(capability) ||
            !MotherCapabilityResolver.ForRole(role).Contains(capability))
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"capability_denied:{capability}", "denied");
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new { success = false, message = "Your role is not allowed to perform this operation." });
            return false;
        }

        return true;
    }

    // Cashier financial operations use explicit server-side capabilities.
    // Client UI visibility is never treated as authorization.
    private async Task<bool> EnsureCashierCapabilityAsync(HttpContext context, ClientSessionValidation session, string capability)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new MySqlCommand("SELECT role FROM users WHERE id = @userId AND is_active = TRUE LIMIT 1", connection);
        command.Parameters.AddWithValue("@userId", session.UserId);
        var roleValue = await command.ExecuteScalarAsync(context.RequestAborted);
        if (!Enum.TryParse<UserRole>(roleValue?.ToString(), true, out var role) ||
            role != UserRole.Cashier ||
            !CashierCapabilities.IsGrantedTo(role, capability))
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"cashier_capability_denied:{capability}", "denied");
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new { success = false, errorCode = "permission_denied", message = "Your Cashier account is not allowed to perform this operation." });
            return false;
        }

        return true;
    }

    private async Task HandleCashierAuthorizationAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, errorCode = "session_invalid", message = session.Message });
            return;
        }

        var capabilities = new[]
        {
            CashierCapabilities.ViewDaily,
            CashierCapabilities.PreviewZ,
            CashierCapabilities.PrintZ,
            CashierCapabilities.OpenDrawer
        };
        var granted = new List<string>();
        foreach (var capability in capabilities)
        {
            if (await EnsureCashierCapabilityAsync(context, session, capability)) granted.Add(capability);
            else return;
        }

        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cashier_authorization_viewed", "success");
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = true, capabilities = granted, terminalId = session.TerminalId, generatedUtc = DateTimeOffset.UtcNow });
    }

    private async Task HandleCashierDashboardAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, errorCode = "session_invalid", message = session.Message });
            return;
        }

        if (!await EnsureCashierCapabilityAsync(context, session, CashierCapabilities.ViewDaily)) return;

        var zReports = ServiceHelper.GetService<ZReportService>();
        if (zReports is null)
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cashier_dashboard_viewed", "failed");
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { success = false, errorCode = "mother_unavailable", message = "Mother report service is currently unavailable." });
            return;
        }

        try
        {
            var snapshot = await zReports.GetSummaryAsync(DateTime.Today, includeTopItems: false);
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cashier_dashboard_viewed", "success");
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                businessDate = snapshot.ReportDate,
                totalOrders = snapshot.OrderCount,
                totalSales = snapshot.GrossSales,
                cashTotal = snapshot.CashTotal,
                cardTotal = snapshot.CardTotal,
                otherPaymentTotal = snapshot.GiftCardTotal,
                voidCount = snapshot.VoidCount,
                voidAmount = 0m,
                discountTotal = snapshot.DiscountTotal,
                expectedCash = snapshot.ExpectedCashInDrawer,
                countedCash = snapshot.LastCashCountAmount,
                variance = snapshot.CashCountVariance,
                terminalName = snapshot.TerminalName,
                generatedUtc = DateTimeOffset.UtcNow,
                version = $"cashier-dashboard-{snapshot.GeneratedAt.Ticks}"
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Cashier dashboard endpoint failed: {ex.GetType().Name}");
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cashier_dashboard_viewed", "failed");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { success = false, errorCode = "report_unavailable", message = "Mother could not prepare the daily dashboard." });
        }
    }

    private async Task HandleCashierZReportPreviewAsync(HttpContext context)
    {
        var session = await ValidateClientSessionAsync(context);
        if (!session.Success)
        {
            await WriteJsonAsync(context, session.StatusCode, new { success = false, errorCode = "session_invalid", message = session.Message });
            return;
        }
        if (!await EnsureCashierCapabilityAsync(context, session, CashierCapabilities.PreviewZ)) return;

        var zReports = ServiceHelper.GetService<ZReportService>();
        if (zReports is null)
        {
            await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { success = false, errorCode = "mother_unavailable", message = "Mother report service is currently unavailable." });
            return;
        }
        try
        {
            var snapshot = await zReports.GetSummaryAsync(DateTime.Today, includeTopItems: false);
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_previewed", "success");
            await WriteJsonAsync(context, HttpStatusCode.OK, new
            {
                success = true,
                isPreview = true,
                businessDate = snapshot.ReportDate,
                terminalName = snapshot.TerminalName,
                generatedUtc = DateTimeOffset.UtcNow,
                totalOrders = snapshot.OrderCount,
                grossSales = snapshot.GrossSales,
                cashTotal = snapshot.CashTotal,
                cardTotal = snapshot.CardTotal,
                otherPaymentTotal = snapshot.GiftCardTotal,
                voidCount = snapshot.VoidCount,
                discountTotal = snapshot.DiscountTotal,
                expectedCash = snapshot.ExpectedCashInDrawer,
                countedCash = snapshot.LastCashCountAmount,
                variance = snapshot.CashCountVariance,
                reportReference = snapshot.ReportReference
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Cashier Z preview endpoint failed: {ex.GetType().Name}");
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_previewed", "failed");
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { success = false, errorCode = "report_unavailable", message = "Mother could not prepare the Z Report preview." });
        }
    }

    private async Task HandleCashierZReportPrintAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<CashierActionRequest>(context);
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId)) { await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "A print request ID is required." }); return; }
        var session = await ValidateClientSessionAsync(context, request.SessionToken);
        if (!session.Success) { await WriteJsonAsync(context, session.StatusCode, new { success = false, errorCode = "session_invalid", message = session.Message }); return; }
        if (!await EnsureCashierCapabilityAsync(context, session, CashierCapabilities.PrintZ)) return;
        var key = $"z:{session.TerminalId}:{request.RequestId.Trim()}";
        if (!_cashierRequestIds.TryAdd(key, new object())) { await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_print_duplicate", "blocked"); await WriteJsonAsync(context, HttpStatusCode.Conflict, new { success = false, errorCode = "duplicate_request", message = "This Z Report print request was already processed." }); return; }
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_print_requested", "success");
        try
        {
            var reports = ServiceHelper.GetService<ZReportService>(); var printer = ServiceHelper.GetService<ZReportPrintService>();
            if (reports is null || printer is null) { await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { success = false, errorCode = "printer_unavailable", message = "Mother print service is unavailable." }); return; }
            var snapshot = await reports.GetSummaryAsync(DateTime.Today, includeTopItems: false); snapshot.IsReprint = false;
            var result = await printer.PrintAsync(snapshot, false, session.UserId);
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_printed", result.Success ? "success" : "failed");
            await WriteJsonAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, new { success = result.Success, printerName = result.PrinterName, message = result.Message, reportReference = snapshot.ReportReference, printedUtc = DateTimeOffset.UtcNow });
        }
        catch (Exception ex) { await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "z_report_printed", "failed"); await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { success = false, message = "Mother could not print the Z Report." }); AppDiagnostics.Log($"Cashier Z print failed: {ex.GetType().Name}"); }
    }

    private async Task HandleCashierCashDrawerOpenAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<CashierActionRequest>(context);
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId)) { await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { success = false, message = "A drawer request ID is required." }); return; }
        var session = await ValidateClientSessionAsync(context, request.SessionToken);
        if (!session.Success) { await WriteJsonAsync(context, session.StatusCode, new { success = false, errorCode = "session_invalid", message = session.Message }); return; }
        if (!await EnsureCashierCapabilityAsync(context, session, CashierCapabilities.OpenDrawer)) return;
        if (!await EnsureFeatureAsync(context, session, PosFeatureKeys.Payments)) return;
        var key = $"drawer:{session.TerminalId}:{request.RequestId.Trim()}";
        if (!_cashierRequestIds.TryAdd(key, new object())) { await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cash_drawer_duplicate", "blocked"); await WriteJsonAsync(context, HttpStatusCode.Conflict, new { success = false, errorCode = "duplicate_request", message = "This cash drawer request was already processed." }); return; }
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cash_drawer_open_requested", "success");
        var drawer = ServiceHelper.GetService<CashDrawerService>();
        if (drawer is null) { await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { success = false, errorCode = "drawer_unavailable", message = "Mother cash drawer service is unavailable." }); return; }
        int? expenseId = null;
        var till = ServiceHelper.GetService<TillExpenseService>();
        var source = $"client_cashier:{session.TerminalId}";
        if (till is not null && request.Amount.HasValue)
        {
            if (request.Reason == "Shopping") expenseId = (await till.CreateShoppingTakeAsync(new ShoppingTakeRequest { ItemName = request.Details ?? "Shopping", AmountTaken = request.Amount.Value, SourceArea = source })).Id;
            else if (request.Reason == "Delivery") expenseId = (await till.CreateDeliveryPayoutAsync(new DeliveryPayoutRequest { Amount = request.Amount.Value, SourceArea = source })).Id;
            else if (request.Reason == "Cash Count") expenseId = (await till.CreateCashCountAsync(new CashCountRequest { CountedCash = request.Amount.Value, SourceArea = source })).Id;
            else if (request.Reason == "Other" && request.Amount.Value > 0) expenseId = (await till.CreateOtherExpenseAsync(new OtherTillExpenseRequest { Reason = request.Details ?? "Other", AmountOut = request.Amount.Value, SourceArea = source }))?.Id;
        }
        var result = await drawer.OpenAsync(new CashDrawerOpenRequest
        {
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Client Cashier request" : request.Reason.Trim(),
            SourceArea = source,
            TillExpenseId = expenseId,
            RequestedByUserId = session.UserId,
            RequestedByName = "Client Cashier",
            RequestedByRole = UserRole.Cashier.ToString()
        });
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, "cash_drawer_opened", result.Success ? "success" : "failed");
        await WriteJsonAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, new { success = result.Success, printerName = result.PrinterName, message = result.Message, auditId = result.AuditId, openedUtc = DateTimeOffset.UtcNow });
    }

    private async Task<bool> EnsureFeatureAsync(HttpContext context, ClientSessionValidation session, string feature)
    {
        return await EnsureAnyFeatureAsync(context, session, feature);
    }

    private async Task<bool> EnsureAnyFeatureAsync(HttpContext context, ClientSessionValidation session, params string[] features)
    {
        var granted = await _clientAccess.GetGrantedFeaturesAsync(session.TerminalId, context.RequestAborted);
        if (features.Any(granted.Contains))
        {
            return true;
        }

        var denied = features.Length == 1 ? features[0] : string.Join(",", features);
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"feature_denied:{denied}", "denied");
        await WriteJsonAsync(context, HttpStatusCode.Forbidden, new
        {
            success = false,
            message = "This Client terminal is not allowed to use that feature."
        });
        return false;
    }

    private static string? FeatureForOrderType(string? orderType) =>
        (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "all" => null,
            "delivery" or "del" => PosFeatureKeys.Delivery,
            "dinein" or "dine_in" or "dine-in" or "table" or "tbl" or "restaurant" => PosFeatureKeys.DineIn,
            "collection" or "pickup" or "takeaway" => PosFeatureKeys.Collection,
            _ => PosFeatureKeys.Collection
        };

    private static object ToClientCustomer(CustomerDataRecord customer) => new
    {
        id = customer.Id,
        motherId = string.IsNullOrWhiteSpace(customer.CloudCustomerId)
            ? customer.Id.ToString()
            : customer.CloudCustomerId,
        name = customer.Name,
        phone = customer.PhoneNumber,
        phoneNumber = customer.PhoneNumber,
        // Email is not held in the Mother customer record and is intentionally
        // never invented or cached on Child terminals.
        email = (string?)null,
        fullAddress = customer.FullAddress,
        address = customer.FullAddress,
        city = customer.City,
        county = customer.County,
        postcode = customer.Postcode,
        loyaltyPoints = customer.PointsBalance
    };

    private async Task UpdateHeartbeatAsync(string terminalId, ClientHeartbeatRequest request, string? remoteIp)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        var clientStatus = string.IsNullOrWhiteSpace(request.Status) ? "online" : request.Status.Trim();

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var command = new MySqlCommand(@"
                UPDATE terminal_pairings
                SET last_seen_at = NOW(),
                    app_version = COALESCE(NULLIF(@appVersion, ''), app_version),
                    platform = COALESCE(NULLIF(@platform, ''), platform),
                    device_name = COALESCE(NULLIF(@deviceName, ''), device_name),
                    current_user_id = @currentUserId,
                    battery_status = COALESCE(NULLIF(@batteryStatus, ''), battery_status),
                    client_status = @clientStatus,
                    last_ip_address = COALESCE(NULLIF(@lastIpAddress, ''), last_ip_address),
                    last_sync_event_id = COALESCE(@lastSyncEventId, last_sync_event_id),
                    updated_at = NOW()
                WHERE terminal_id = @terminalId", connection, transaction))
            {
                command.Parameters.AddWithValue("@terminalId", terminalId);
                command.Parameters.AddWithValue("@appVersion", request.AppVersion ?? string.Empty);
                command.Parameters.AddWithValue("@platform", request.Platform ?? string.Empty);
                command.Parameters.AddWithValue("@deviceName", request.DeviceName ?? string.Empty);
                command.Parameters.AddWithValue("@currentUserId", (object?)request.CurrentUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("@batteryStatus", request.BatteryStatus ?? string.Empty);
                command.Parameters.AddWithValue("@clientStatus", clientStatus);
                command.Parameters.AddWithValue("@lastIpAddress", remoteIp ?? string.Empty);
                command.Parameters.AddWithValue("@lastSyncEventId", (object?)request.LastSyncEventId ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }

            var terminalName = await GetTerminalNameAsync(connection, transaction, terminalId);
            await UpsertTerminalHealthAsync(connection, transaction, terminalName ?? terminalId, request.AppVersion, remoteIp, clientStatus, string.Empty);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<string?> GetTerminalNameAsync(MySqlConnection connection, MySqlTransaction transaction, string terminalId)
    {
        await using var command = new MySqlCommand(
            "SELECT terminal_name FROM terminal_pairings WHERE terminal_id = @terminalId LIMIT 1",
            connection,
            transaction);
        command.Parameters.AddWithValue("@terminalId", terminalId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task UpsertTerminalHealthAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string terminalName,
        string? appVersion,
        string? remoteIp,
        string status,
        string error)
    {
        // Avoid executing DDL (schema upgrades) while a transaction is active —
        // ALTER/CREATE statements can cause implicit commits and invalidate the
        // active transaction. Ensure tables should be run by callers before
        // beginning a transaction. Only run EnsureTableAsync when no transaction
        // is provided.
        if (transaction == null)
        {
            await TerminalHealthService.EnsureTableAsync(connection);
        }
        await using var command = new MySqlCommand(@"
            INSERT INTO terminal_health
                (terminal_name, terminal_mode, database_host, app_version, last_seen_at, last_status, last_error)
            VALUES
                (@terminalName, 'Child', @databaseHost, @appVersion, NOW(), @status, @error)
            ON DUPLICATE KEY UPDATE
                terminal_mode = 'Child',
                database_host = VALUES(database_host),
                app_version = VALUES(app_version),
                last_seen_at = NOW(),
                last_status = VALUES(last_status),
                last_error = VALUES(last_error),
                updated_at = NOW()", connection, transaction);
        command.Parameters.AddWithValue("@terminalName", terminalName);
        command.Parameters.AddWithValue("@databaseHost", remoteIp ?? string.Empty);
        command.Parameters.AddWithValue("@appVersion", appVersion ?? string.Empty);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@error", error);
        await command.ExecuteNonQueryAsync();
    }

    private async Task StoreClientSessionAsync(string terminalId, User user, string sessionHash, DateTime expiresAt)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);

        await using var command = new MySqlCommand(@"
            INSERT INTO client_user_sessions
                (terminal_id, user_id, session_token_hash, expires_at, revoked_at, created_at, last_seen_at)
            VALUES
                (@terminalId, @userId, @sessionHash, @expiresAt, NULL, NOW(), NOW())", connection);
        command.Parameters.AddWithValue("@terminalId", terminalId);
        command.Parameters.AddWithValue("@userId", user.Id);
        command.Parameters.AddWithValue("@sessionHash", sessionHash);
        command.Parameters.AddWithValue("@expiresAt", expiresAt);
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateCurrentUserAsync(string terminalId, int userId)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);

        await using var command = new MySqlCommand(@"
            UPDATE terminal_pairings
            SET current_user_id = @userId,
                updated_at = NOW()
            WHERE terminal_id = @terminalId", connection);
        command.Parameters.AddWithValue("@terminalId", terminalId);
        command.Parameters.AddWithValue("@userId", userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> GetLastSyncEventIdAsync(MySqlConnection connection)
    {
        await using var command = new MySqlCommand("SELECT COALESCE(MAX(id), 0) FROM terminal_events", connection);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private async Task<Dictionary<string, string>> GetConfigurationVersionsAsync()
    {
        var versions = SyncSectionKeys.All.ToDictionary(section => section, _ => "0", StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureClientConnectionTablesAsync(connection);
            await EnsureConfigurationVersionsTableAsync(connection);
            await using var command = new MySqlCommand("SELECT section_key, version FROM configuration_versions", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                versions[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch (Exception ex)
        {
            // Layout/menu must still work when version tracking is unavailable.
            AppDiagnostics.Log($"configuration_versions read failed: {ex.GetType().Name}: {ex.Message}");
        }

        return versions;
    }

    private static async Task EnsureConfigurationVersionsTableAsync(MySqlConnection connection)
    {
        await using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS configuration_versions (
                section_key VARCHAR(64) NOT NULL PRIMARY KEY,
                version BIGINT NOT NULL DEFAULT 0,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> IncrementConfigurationVersionAsync(string section)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new MySqlCommand(@"
            INSERT INTO configuration_versions (section_key, version, updated_at)
            VALUES (@section, 1, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE version = version + 1, updated_at = UTC_TIMESTAMP()", connection, transaction))
        {
            command.Parameters.AddWithValue("@section", section);
            await command.ExecuteNonQueryAsync();
        }
        await using var select = new MySqlCommand("SELECT version FROM configuration_versions WHERE section_key = @section", connection, transaction);
        select.Parameters.AddWithValue("@section", section);
        var version = Convert.ToString(await select.ExecuteScalarAsync()) ?? "0";
        await transaction.CommitAsync();
        return version;
    }

    private static string? ResolveConfigurationSection(string eventType)
    {
        var key = eventType.Trim().ToLowerInvariant();
        return key switch
        {
            "menu.updated" => SyncSectionKeys.Menu,
            "category.updated" or "categories.updated" => SyncSectionKeys.Categories,
            "product.updated" or "products.updated" => SyncSectionKeys.Products,
            "availability.updated" => SyncSectionKeys.Availability,
            "branding.updated" => SyncSectionKeys.Branding,
            "floor.updated" or "floors.updated" => SyncSectionKeys.Floors,
            "table.updated" or "tables.updated" => SyncSectionKeys.Tables,
            "permissions.updated" => SyncSectionKeys.Permissions,
            "features.updated" => SyncSectionKeys.Features,
            "settings.updated" => SyncSectionKeys.Settings,
            "images.updated" => SyncSectionKeys.Images,
            _ => null
        };
    }

    private static async Task EnsureClientConnectionTablesAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged)
        {
            return;
        }

        await TerminalPairingService.EnsureTableAsync(connection);
        await TerminalPairingService.EnsureAttemptsTableAsync(connection);
        await TerminalHealthService.EnsureTableAsync(connection);
        await ClientTerminalAccessService.EnsureTableAsync(connection);

        var statements = new[]
        {
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS terminal_id VARCHAR(64) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS device_type VARCHAR(80) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS platform VARCHAR(80) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS device_id VARCHAR(160) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS device_name VARCHAR(160) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS token_hash VARCHAR(128) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_seen_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS app_version VARCHAR(40) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS current_user_id INT NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS battery_status VARCHAR(80) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS client_status VARCHAR(40) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_ip_address VARCHAR(45) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS last_sync_event_id BIGINT NOT NULL DEFAULT 0",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS tenant_slug VARCHAR(120) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS restaurant_slug VARCHAR(120) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS revoked_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_status VARCHAR(40) NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_connected_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_disconnected_at DATETIME NULL",
            "ALTER TABLE terminal_pairings ADD COLUMN IF NOT EXISTS websocket_last_message_at DATETIME NULL",
            @"CREATE TABLE IF NOT EXISTS client_user_sessions (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                terminal_id VARCHAR(64) NOT NULL,
                user_id INT NOT NULL,
                session_token_hash VARCHAR(128) NOT NULL,
                expires_at DATETIME NOT NULL,
                revoked_at DATETIME NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_seen_at DATETIME NULL,
                INDEX idx_client_user_sessions_terminal (terminal_id),
                INDEX idx_client_user_sessions_user (user_id),
                INDEX idx_client_user_sessions_token (session_token_hash),
                INDEX idx_client_user_sessions_expiry (expires_at)
            ) ENGINE=InnoDB",
            @"CREATE TABLE IF NOT EXISTS terminal_events (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                event_type VARCHAR(80) NOT NULL,
                entity_type VARCHAR(80) NULL,
                entity_id VARCHAR(80) NULL,
                payload_json JSON NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_terminal_events_created (created_at),
                INDEX idx_terminal_events_type (event_type)
            ) ENGINE=InnoDB"
            ,@"CREATE TABLE IF NOT EXISTS client_security_audit (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                terminal_id VARCHAR(64) NOT NULL,
                user_id INT NOT NULL,
                action VARCHAR(80) NOT NULL,
                outcome VARCHAR(40) NOT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_client_security_audit_terminal (terminal_id, created_at),
                INDEX idx_client_security_audit_user (user_id, created_at)
            ) ENGINE=InnoDB"
            ,@"CREATE TABLE IF NOT EXISTS client_print_audit (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                request_id VARCHAR(80) NOT NULL,
                terminal_id VARCHAR(64) NOT NULL,
                user_id INT NOT NULL,
                order_id VARCHAR(120) NOT NULL,
                document_type VARCHAR(40) NOT NULL,
                status VARCHAR(40) NOT NULL,
                message TEXT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                completed_at DATETIME NULL,
                UNIQUE KEY uq_client_print_audit_request_terminal (request_id, terminal_id),
                INDEX idx_client_print_audit_order_created (order_id, created_at),
                INDEX idx_client_print_audit_terminal_created (terminal_id, created_at)
            ) ENGINE=InnoDB"
            ,@"CREATE TABLE IF NOT EXISTS configuration_versions (
                section_key VARCHAR(40) PRIMARY KEY,
                version BIGINT NOT NULL DEFAULT 0,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB"
        };

        foreach (var statement in statements)
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string? GetBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header) &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        var terminalHeader = context.Request.Headers["X-Terminal-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(terminalHeader))
        {
            return terminalHeader.Trim();
        }

        var queryToken = context.Request.Query["token"].ToString();
        if (!string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.Trim();
        }

        // OrderWeb.Client originally used terminalToken in the WebSocket query.
        var clientQueryToken = context.Request.Query["terminalToken"].ToString();
        return string.IsNullOrWhiteSpace(clientQueryToken) ? null : clientQueryToken.Trim();
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
    {
        if (context.Request.ContentLength == 0)
        {
            return default;
        }

        // Read the body as text so we can accept both camelCase and snake_case
        // property names from different clients. Convert any JSON property
        // names that use snake_case to camelCase before deserializing so the
        // target record (which expects camelCase/PascalCase names) binds
        // correctly.
        using var reader = new StreamReader(context.Request.Body, leaveOpen: false);
        var json = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json)) return default;

        if (json.Contains("_"))
        {
            json = System.Text.RegularExpressions.Regex.Replace(
                json,
                "\"([a-z0-9_]+)\"(?=\\s*:)",
                m =>
                {
                    var key = m.Groups[1].Value;
                    if (!key.Contains("_")) return m.Value;
                    var parts = key.Split('_');
                    var camel = parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
                    return '"' + camel + '"';
                },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task WriteJsonAsync(HttpContext context, HttpStatusCode statusCode, object payload)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.AccessControlAllowOrigin = "*";
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions, context.RequestAborted);
    }

    private static async Task SendWebSocketJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string? GetRemoteIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();

    private static string NormalizePairingCode(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string GetRestaurantSlug() =>
        TerminalConfigurationService.GetConfiguration().DatabaseName.Trim();

    public void Dispose()
    {
        _reservationSync.SyncCompleted -= OnReservationSyncCompleted;
        StopAsync().GetAwaiter().GetResult();
        _lifetimeLock.Dispose();
    }

    private sealed class ClientWebSocketConnection
    {
        public ClientWebSocketConnection(string terminalId, WebSocket webSocket)
        {
            TerminalId = terminalId;
            WebSocket = webSocket;
            ConnectedAt = DateTime.Now;
            LastMessageAt = ConnectedAt;
        }

        public string TerminalId { get; }
        public WebSocket WebSocket { get; }
        public DateTime ConnectedAt { get; }
        public DateTime LastMessageAt { get; set; }
    }

    private sealed record ClientBootstrapRequest(
        string TerminalName,
        string PairingCode,
        string? DeviceType,
        string? Platform,
        string? DeviceId,
        string? DeviceName,
        string? AppVersion);

    private sealed record ClientLoginRequest(string Pin, string? TerminalToken = null);

    private sealed record ClientCustomerUpsertRequest(
        ClientCustomerAuth? Auth,
        string Name,
        string PhoneNumber,
        string? OrderTypes,
        string? FullAddress,
        string? City,
        string? County,
        string? Postcode,
        string? Email);

    private sealed record ClientCustomerAuth(
        string? TerminalId,
        string? TerminalToken,
        string? SessionToken,
        string? RestaurantSlug,
        string? LastIpAddress,
        string? AppVersion);

    private sealed record ClientPaymentRequest(
        string RequestId,
        string OrderId,
        string Method,
        decimal Amount,
        string? SessionToken = null,
        long? ExpectedOrderRevision = null,
        string? CorrelationId = null,
        string? GiftCardNumber = null,
        string? GiftCardIdempotencyKey = null,
        string? LoyaltyLookup = null,
        int? LoyaltyPoints = null,
        string? LoyaltyIdempotencyKey = null);

    private sealed record ClientPrintRequest(
        string RequestId,
        string OrderId,
        string? DocumentType,
        string? SessionToken = null);

    private sealed record CashierActionRequest(string? RequestId, string? SessionToken = null, string? Reason = null, string? PrinterTarget = null, decimal? Amount = null, string? Details = null);

    private sealed record ClientPrintAudit(string PrintJobId, string OrderId, string DocumentType, string Status, string? Message);

    private sealed record ClientHeartbeatRequest(
        string? TerminalId = null,
        string? TerminalToken = null,
        string? AppVersion = null,
        string? Platform = null,
        string? DeviceName = null,
        int? CurrentUserId = null,
        long? LastSyncEventId = null,
        string? BatteryStatus = null,
        string? Status = null);

    private sealed record ClientBootstrapResponse(
        bool Success,
        string TerminalId,
        string TerminalToken,
        string TerminalName,
        int ApiPort,
        string WebsocketUrl,
        bool LoginRequired,
        BootstrapRestaurantInfo Restaurant,
        BootstrapTerminalIdentity TerminalIdentity,
        BootstrapTerminalConfig TerminalConfig,
        BootstrapSyncInfo Sync,
        BootstrapAuthInfo Auth,
        BootstrapFeatureFlags Features,
        DateTimeOffset ServerTime);

    private sealed record BootstrapRestaurantInfo(string Name, string Slug);

    private sealed record BootstrapTerminalIdentity(
        string TerminalId,
        string TerminalName,
        string DeviceType,
        string Platform,
        string DeviceId,
        string DeviceName,
        string AppVersion);

    private sealed record BootstrapTerminalConfig(
        string MotherIp,
        int ApiPort,
        string WebsocketPath,
        string HeartbeatPath,
        string LoginPath,
        int HeartbeatSeconds);

    private sealed record BootstrapSyncInfo(long Checkpoint, bool FullSyncRequired);

    private sealed record BootstrapAuthInfo(
        bool LoginRequired,
        bool TerminalTokenRequired,
        bool UserSessionRequired,
        bool PinLoginEnabled);

    private sealed record BootstrapFeatureFlags(
        bool WebSocketEnabled,
        bool HeartbeatEnabled,
        bool MenuBootstrapEnabled,
        bool OrdersBootstrapEnabled,
        bool CustomerBootstrapEnabled);

    private sealed record ClientActivationResult(
        bool Success,
        string Message,
        string TerminalId,
        string Token,
        long LastSyncEventId,
        HttpStatusCode StatusCode,
        ClientBootstrapResponse? Bootstrap)
    {
        public static ClientActivationResult Ok(
            string terminalId,
            string token,
            long lastSyncEventId,
            ClientBootstrapResponse bootstrap) =>
            new(true, "Client terminal paired successfully.", terminalId, token, lastSyncEventId, HttpStatusCode.OK, bootstrap);

        public static ClientActivationResult Fail(string message, HttpStatusCode statusCode = HttpStatusCode.Unauthorized) =>
            new(false, message, string.Empty, string.Empty, 0, statusCode, null);
    }

    private sealed record TerminalTokenValidation(
        bool Success,
        string Message,
        string TerminalId,
        string TerminalName,
        HttpStatusCode StatusCode)
    {
        public static TerminalTokenValidation Ok(string terminalId, string terminalName) =>
            new(true, string.Empty, terminalId, terminalName, HttpStatusCode.OK);

        public static TerminalTokenValidation Fail(string message, HttpStatusCode statusCode = HttpStatusCode.Unauthorized) =>
            new(false, message, string.Empty, string.Empty, statusCode);
    }

    private sealed record ClientSessionValidation(
        bool Success,
        string Message,
        string TerminalId,
        int UserId,
        HttpStatusCode StatusCode)
    {
        public static ClientSessionValidation Ok(string terminalId, int userId) =>
            new(true, string.Empty, terminalId, userId, HttpStatusCode.OK);

        public static ClientSessionValidation Fail(string message, HttpStatusCode statusCode = HttpStatusCode.Unauthorized) =>
            new(false, message, string.Empty, 0, statusCode);
    }

    private sealed record ClientOrderUpsertHttpRequest(
        string? OrderId,
        string? OrderType,
        string? CustomerName,
        string? CustomerPhone,
        string? CustomerEmail,
        string? CustomerAddress,
        decimal DeliveryFee,
        string? Notes,
        string? ScheduledTime,
        int? TableId,
        string? TableNumber,
        int Guests,
        IReadOnlyList<ClientOrderLineHttpRequest>? Lines,
        int? ExpectedVersion = null,
        string? ExpectedUpdatedUtc = null,
        bool PrintKitchen = false,
        bool PrintReceipt = false,
        IReadOnlyList<string>? PrintDocuments = null,
        string? PrintRequestId = null);

    private sealed record ClientOrderVoidHttpRequest(string? OrderId);

    private sealed record ClientOrderLineHttpRequest(
        string? Id,
        string? ProductId,
        string? Name,
        int Quantity,
        decimal UnitPrice,
        string? Notes,
        IReadOnlyList<string>? Modifiers,
        string? VariantId = null,
        string? VariantName = null,
        decimal? VariantPrice = null,
        string? MealDealId = null,
        IReadOnlyList<string>? MealDealChoices = null,
        string? TastingMenuId = null);

    private sealed record ClientReservationCreateHttpRequest(
        string? ReservationDate,
        string? ReservationTime,
        int Covers,
        string? CustomerName,
        string? CustomerPhone,
        string? CustomerEmail,
        string? PromoCode,
        string? Notes,
        string? Allergies,
        string? TableNumber,
        string? Channel);

    private sealed record ClientReservationStatusHttpRequest(
        string? CloudId,
        string? Id,
        string? LocalId,
        string? Status);
}
