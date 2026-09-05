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
using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Services;
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
            app.MapPost("/api/client/bootstrap", HandleBootstrapAsync);
            app.MapPost("/api/client/login", HandleLoginAsync);
            app.MapGet("/api/client/customers/search", HandleCustomerSearchAsync);
            app.MapGet("/api/client/customers/field-access-policy", HandleCustomerFieldPolicyAsync);
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

    private async Task HandleBootstrapAsync(HttpContext context)
    {
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

        var permissions = await _permissionService.GetRolePermissionsAsync(login.User.Role);
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
            CustomerFieldPolicy: SerializeFieldPolicy(GetClientFieldPolicy()),
            ServerTime: DateTimeOffset.Now);
    }

    private async Task HandleCustomerSearchAsync(HttpContext context)
    {
        var terminal = await ValidateTerminalTokenAsync(context);
        if (!terminal.Success)
        {
            await WriteJsonAsync(context, terminal.StatusCode, new { success = false, message = terminal.Message });
            return;
        }

        var orderType = context.Request.Query["orderType"].ToString();
        var name = context.Request.Query["name"].ToString();
        var phone = context.Request.Query["phone"].ToString();
        var addressOrPostcode = context.Request.Query["addressOrPostcode"].ToString();

        var policy = GetClientFieldPolicy();
        if (!policy.AllowCustomerDirectory)
        {
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new
            {
                success = false,
                message = "Customer directory is disabled for Client terminals."
            });
            return;
        }

        var directory = ResolveCustomerDirectory();
        var kind = orderType.Trim().ToLowerInvariant() switch
        {
            "collection" or "col" => CustomerOrderKind.Collection,
            "delivery" or "del" => CustomerOrderKind.Delivery,
            _ => (CustomerOrderKind?)null
        };

        var result = await directory.SearchCustomersAsync(new CustomerSearchRequestDto(
            string.IsNullOrWhiteSpace(name) ? null : name,
            string.IsNullOrWhiteSpace(phone) ? null : phone,
            string.IsNullOrWhiteSpace(addressOrPostcode) ? null : addressOrPostcode,
            kind));

        if (!result.IsSuccess || result.Value == null)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new
            {
                success = false,
                message = result.Error?.Message ?? "Customer search failed."
            });
            return;
        }

        var projected = result.Value.Customers
            .Select(customer => CustomerFieldProjector.Project(customer, policy, CustomerFieldAccessScope.Search))
            .Select(ToClientCustomerState)
            .ToList();

        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            customers = projected,
            fieldPolicy = SerializeFieldPolicy(policy)
        });
    }

    private async Task HandleCustomerFieldPolicyAsync(HttpContext context)
    {
        var terminal = await ValidateTerminalTokenAsync(context);
        if (!terminal.Success)
        {
            await WriteJsonAsync(context, terminal.StatusCode, new { success = false, message = terminal.Message });
            return;
        }

        await WriteJsonAsync(context, HttpStatusCode.OK, new
        {
            success = true,
            policy = SerializeFieldPolicy(GetClientFieldPolicy())
        });
    }

    private static CustomerFieldAccessPolicy GetClientFieldPolicy() =>
        ServiceHelper.GetService<MotherClientCustomerFieldPolicyService>()?.GetClientPolicy()
        ?? CustomerFieldAccessPolicy.ClientDefault;

    private static ICustomerDirectoryService ResolveCustomerDirectory() =>
        ServiceHelper.GetService<ICustomerDirectoryService>()
        ?? new MotherCustomerDirectoryService(ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService());

    private static ClientCustomerFieldPolicyDto SerializeFieldPolicy(CustomerFieldAccessPolicy policy) =>
        new(
            SearchFields: policy.SearchFields.Select(field => field.ToString()).ToList(),
            CacheFields: policy.CacheFields.Select(field => field.ToString()).ToList(),
            DetailFields: policy.DetailFields.Select(field => field.ToString()).ToList(),
            AllowOrderHistory: policy.AllowOrderHistory,
            AllowCustomerDirectory: policy.AllowCustomerDirectory,
            AllowAssignCustomer: policy.AllowAssignCustomer);

    private static ClientCustomerState ToClientCustomerState(CustomerSummaryDto customer) =>
        new(
            Id: int.TryParse(customer.Id, out var parsedId) ? parsedId : 0,
            MotherId: customer.MotherId ?? customer.Id,
            Name: customer.Name,
            Phone: customer.Phone,
            PhoneNumber: customer.Phone,
            Email: customer.Email,
            FullAddress: customer.Address,
            Address: customer.Address,
            City: customer.City,
            County: customer.County,
            Postcode: customer.Postcode,
            LoyaltyPoints: customer.LoyaltyPoints ?? 0);

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

        return TerminalTokenValidation.Ok(reader.GetString("terminal_id"), reader.GetString("terminal_name"));
    }

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
        ClientCustomerFieldPolicyDto CustomerFieldPolicy,
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

    private sealed record ClientCustomerFieldPolicyDto(
        IReadOnlyList<string> SearchFields,
        IReadOnlyList<string> CacheFields,
        IReadOnlyList<string> DetailFields,
        bool AllowOrderHistory,
        bool AllowCustomerDirectory,
        bool AllowAssignCustomer);

    private sealed record ClientCustomerState(
        int Id,
        string? MotherId,
        string? Name,
        string? Phone,
        string? PhoneNumber,
        string? Email,
        string? FullAddress,
        string? Address,
        string? City,
        string? County,
        string? Postcode,
        int LoyaltyPoints);

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
}
