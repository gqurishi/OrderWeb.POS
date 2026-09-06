using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using MySqlConnector;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Compatibility;
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
    private readonly ConcurrentDictionary<string, ClientWebSocketConnection> _clients = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _rateLimitWindows = new();
    private readonly SemaphoreSlim _lifetimeLock = new(1, 1);
    private WebApplication? _app;

    public ClientWebSocketBroadcastService(
        DatabaseService databaseService,
        AuthenticationService authenticationService,
        PermissionService permissionService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
        _permissionService = permissionService;
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
            app.MapPost("/api/client/login", HandleLoginAsync);
            app.MapGet("/api/client/customers/search", HandleCustomerSearchAsync);
            app.MapPost("/api/client/customers/upsert", HandleCustomerUpsertAsync);
            app.MapPost("/api/client/payments", HandlePaymentAsync);
            app.MapGet("/api/client/payments/{requestId}", HandlePaymentStatusAsync);
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

        var sessionToken = CreateToken();
        var expiresAt = DateTime.UtcNow.AddHours(12);
        await StoreClientSessionAsync(terminal.TerminalId, login.User, HashToken(sessionToken), expiresAt);
        await UpdateCurrentUserAsync(terminal.TerminalId, login.User.Id);
        await AuditSensitiveOperationAsync(terminal.TerminalId, login.User.Id, "client_session_created", "success");

        var permissions = await _permissionService.GetRolePermissionsAsync(login.User.Role);
        var capabilities = MotherCapabilityResolver.ForRole(login.User.Role);
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
                sessionToken,
                expiresAtUtc = expiresAt,
                restaurantName = TerminalConfigurationService.GetConfiguration().DatabaseName
            }
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
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.ViewCustomers)) return;

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
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.ManageCustomers)) return;

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
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.TakePayments)) return;

        var result = await new MotherPaymentService().TakePaymentAsync(new PaymentRequest(
            request.RequestId,
            session.TerminalId,
            string.Empty,
            request.OrderId,
            request.Method,
            request.Amount,
            DateTimeOffset.UtcNow,
            request.ExpectedOrderRevision,
            request.CorrelationId));

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
        await PublishDataChangedAsync("order.updated", request.OrderId);
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
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.TakePayments)) return;

        var result = await new MotherPaymentService().GetResultAsync($"client:{session.TerminalId}:{requestId}");
        if (!result.IsSuccess || result.Value == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { success = false, message = "Mother has no final result for this payment request yet." });
            return;
        }
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = true, payment = result.Value });
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

        var documentType = request.DocumentType?.Trim().ToLowerInvariant();
        var requiredCapability = documentType == "reprint"
            ? OrderWeb.Contracts.Capabilities.PosCapabilityKeys.ReprintReceipts
            : OrderWeb.Contracts.Capabilities.PosCapabilityKeys.PrintReceipts;
        if (!await EnsureCapabilityAsync(context, session, requiredCapability)) return;
        if (documentType is not "customer_receipt" and not "reprint" and not "kitchen_ticket")
        {
            await WriteJsonAsync(context, HttpStatusCode.UnprocessableEntity, new { success = false, message = "The requested document type is not supported." });
            return;
        }

        var duplicate = await GetPrintAuditAsync(request.RequestId, session.TerminalId);
        if (duplicate != null)
        {
            await WriteJsonAsync(context, HttpStatusCode.OK, new { success = duplicate.Status is "queued" or "printed" or "partial", print = duplicate });
            return;
        }
        if (!await TryBeginPrintAuditAsync(request.RequestId, session, request.OrderId, documentType))
        {
            var inFlight = await GetPrintAuditAsync(request.RequestId, session.TerminalId);
            await WriteJsonAsync(context, HttpStatusCode.Conflict, new
            {
                success = false,
                message = "Mother is already processing this print request. Check print history before retrying.",
                print = inFlight
            });
            return;
        }

        var order = await new OrderService().GetOrderByExternalIdAsync(request.OrderId);
        if (order == null)
        {
            await StorePrintAuditAsync(request.RequestId, session, request.OrderId, documentType, "failed", "Mother POS could not find this order.");
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { success = false, message = "Mother POS could not find this order." });
            return;
        }

        string status;
        string message;
        if (documentType == "kitchen_ticket")
        {
            var routing = ServiceHelper.GetService<OrderRoutingPrintService>() ?? new OrderRoutingPrintService();
            var kitchenOrder = ToKitchenPrintOrder(order);
            var result = await routing.PrintOrderAsync(kitchenOrder);
            status = result.HasFailures && result.AnyPrinted ? "partial" : result.AnyPrinted ? "printed" : "failed";
            message = result.HasFailures
                ? $"Kitchen print completed with failures: {string.Join("; ", result.FailedRoutes)}"
                : result.AnyPrinted ? "Mother printed the kitchen ticket." : "Mother could not print the kitchen ticket; check the configured printer.";
        }
        else
        {
            var receiptService = ServiceHelper.GetService<ReceiptService>();
            if (receiptService == null)
            {
                await StorePrintAuditAsync(request.RequestId, session, request.OrderId, documentType, "failed", "Mother receipt service is unavailable.");
                await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { success = false, message = "Mother receipt service is unavailable." });
                return;
            }

            var queued = await receiptService.PrintFullCustomerReceiptAsync(order);
            status = queued ? "queued" : "failed";
            message = queued ? "Mother queued the customer receipt." : "Mother could not queue the receipt; check the configured printer.";
        }

        await StorePrintAuditAsync(request.RequestId, session, request.OrderId, documentType, status, message);
        await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"client_print:{documentType}", status);
        var succeeded = status is "queued" or "printed" or "partial";
        await WriteJsonAsync(context, succeeded ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, new
        {
            success = succeeded,
            print = new { printJobId = request.RequestId, status, message }
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
        if (!await EnsureCapabilityAsync(context, session, OrderWeb.Contracts.Capabilities.PosCapabilityKeys.PrintReceipts)) return;
        var audit = string.IsNullOrWhiteSpace(requestId) ? null : await GetPrintAuditAsync(requestId, session.TerminalId);
        if (audit == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { success = false, message = "Mother has no print history for this request." });
            return;
        }
        await WriteJsonAsync(context, HttpStatusCode.OK, new { success = audit.Status is "queued" or "printed" or "partial", print = audit });
    }

    private static TableOrder ToKitchenPrintOrder(Order order)
    {
        var printOrder = new TableOrder
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
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

                var expiresAt = reader.IsDBNull(reader.GetOrdinal("pairing_expires_at"))
                    ? DateTime.MinValue
                    : reader.GetDateTime("pairing_expires_at");
                if (failureReason == null && expiresAt <= DateTime.Now)
                {
                    failureReason = "expired_code";
                    failureMessage = "Pairing code has expired. Create a new code on the Mother terminal.";
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
            SELECT s.user_id
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

        var userId = await command.ExecuteScalarAsync();
        if (userId == null || userId == DBNull.Value)
        {
            return ClientSessionValidation.Fail("Your staff session has expired or is no longer allowed.", HttpStatusCode.Unauthorized);
        }

        return ClientSessionValidation.Ok(terminal.TerminalId, Convert.ToInt32(userId));
    }

    private async Task<bool> EnsureCapabilityAsync(HttpContext context, ClientSessionValidation session, string capability)
    {
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new MySqlCommand("SELECT role FROM users WHERE id = @userId AND is_active = TRUE LIMIT 1", connection);
        command.Parameters.AddWithValue("@userId", session.UserId);
        var roleValue = await command.ExecuteScalarAsync(context.RequestAborted);
        if (!Enum.TryParse<UserRole>(roleValue?.ToString(), true, out var role) ||
            !MotherCapabilityResolver.ForRole(role).Contains(capability))
        {
            await AuditSensitiveOperationAsync(session.TerminalId, session.UserId, $"capability_denied:{capability}", "denied");
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new { success = false, message = "Your role is not allowed to perform this operation." });
            return false;
        }

        return true;
    }

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
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureClientConnectionTablesAsync(connection);
        var versions = SyncSectionKeys.All.ToDictionary(section => section, _ => "0", StringComparer.OrdinalIgnoreCase);
        await using var command = new MySqlCommand("SELECT section_key, version FROM configuration_versions", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions[reader.GetString(0)] = reader.GetString(1);
        }
        return versions;
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
        string? CorrelationId = null);

    private sealed record ClientPrintRequest(
        string RequestId,
        string OrderId,
        string? DocumentType,
        string? SessionToken = null);

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
}
