using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class OrderWebConnectionStatus
{
    public bool IsConfigured { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsWebSocketConnected { get; init; }
    public bool IsPollingActive { get; init; }
    public bool IsHeartbeatActive { get; init; }
    public bool IsAckRetryActive { get; init; }
    public bool IsApiHealthy { get; init; }
    public DateTime LastHealthCheckUtc { get; init; }
    public DateTime? LastSuccessfulConnectUtc { get; init; }
    public string StatusMessage { get; init; } = "Not started";
    public string? LastError { get; init; }

    public bool IsFullyOperational =>
        IsConfigured &&
        IsEnabled &&
        IsApiHealthy &&
        (IsWebSocketConnected || IsPollingActive);
}

/// <summary>
/// Keeps OrderWeb.net connected using saved database settings until an admin changes them.
/// Coordinates WebSocket, REST polling, heartbeat, ACK retry, and loyalty/gift-card API clients.
/// </summary>
public class OrderWebConnectionKeeperService
{
    private readonly DatabaseService _databaseService;
    private readonly OrderWebWebSocketService _webSocketService;
    private readonly CloudOrderService _cloudOrderService;
    private readonly HeartbeatService _heartbeatService;
    private readonly OrderWebRestApiService _restApiService;
    private readonly LoyaltyService _loyaltyService;
    private readonly ReceiptService _receiptService;
    private readonly OnlineOrderAutoPrintService _autoPrintService;
    private readonly ReservationSyncService _reservationSyncService;

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private string? _activeConfigFingerprint;
    private bool _isStarted;
    private bool _webSocketHandlerAttached;
    private OrderWebConnectionStatus _status = new();
    private DateTime _lastRemoteApiHealthCheckUtc = DateTime.MinValue;
    private static readonly TimeSpan RemoteApiHealthCheckInterval = TimeSpan.FromMinutes(5);

    public OrderWebConnectionStatus Status => _status;
    public event Action<OrderWebConnectionStatus>? StatusChanged;

    public OrderWebConnectionKeeperService(
        DatabaseService databaseService,
        OrderWebWebSocketService webSocketService,
        CloudOrderService cloudOrderService,
        HeartbeatService heartbeatService,
        OrderWebRestApiService restApiService,
        LoyaltyService loyaltyService,
        ReceiptService receiptService,
        OnlineOrderAutoPrintService autoPrintService,
        ReservationSyncService reservationSyncService)
    {
        _databaseService = databaseService;
        _webSocketService = webSocketService;
        _cloudOrderService = cloudOrderService;
        _heartbeatService = heartbeatService;
        _restApiService = restApiService;
        _loyaltyService = loyaltyService;
        _receiptService = receiptService;
        _autoPrintService = autoPrintService;
        _reservationSyncService = reservationSyncService;
    }

    public async Task StartAsync()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        AttachWebSocketHandlers();

        _receiptService.SetCloudOrderService(_cloudOrderService);
        _cloudOrderService.SetAutoPrintService(_autoPrintService);
        _cloudOrderService.SetWebSocketService(_webSocketService);
        _webSocketService.SetReservationSyncService(_reservationSyncService);

        await ApplyConfigurationAsync();
        StartHealthMonitor();

        System.Diagnostics.Debug.WriteLine("OrderWebConnectionKeeper started");
    }

    public async Task ApplyConfigurationAsync(CloudConfiguration? config = null, bool forceBackfill = false)
    {
        await _connectLock.WaitAsync();
        try
        {
            var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
            if (!onlineMasterCheck.Allowed)
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = false,
                    StatusMessage = onlineMasterCheck.Reason
                });
                return;
            }

            config ??= await _databaseService.GetCloudConfigurationAsync();
            if (config == null || !config.IsConfigured())
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = false,
                    StatusMessage = "OrderWeb settings are not configured"
                });
                return;
            }

            if (!config.IsEnabled)
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = true,
                    IsEnabled = false,
                    StatusMessage = "OrderWeb connection is disabled in settings"
                });
                return;
            }

            var endpointValidation = CloudEndpointSecurityPolicy.Validate(
                !string.IsNullOrWhiteSpace(config.RestApiBaseUrl) ? config.RestApiBaseUrl : config.ApiBaseUrl,
                config.WebSocketUrl);
            if (!endpointValidation.IsValid)
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = true,
                    IsEnabled = false,
                    StatusMessage = endpointValidation.Message,
                    LastError = endpointValidation.Message
                });
                return;
            }

            var fingerprint = BuildFingerprint(config);
            var configChanged = !string.Equals(fingerprint, _activeConfigFingerprint, StringComparison.Ordinal);
            _activeConfigFingerprint = fingerprint;

            var tenantSlug = config.TenantSlug.Trim();
            var apiKey = config.ApiKey.Trim();
            var restApiUrl = NormalizeApiBaseUrl(
                !string.IsNullOrWhiteSpace(config.RestApiBaseUrl) ? config.RestApiBaseUrl : config.ApiBaseUrl,
                tenantSlug);
            var wsUrl = NormalizeWebSocketUrl(config.WebSocketUrl, tenantSlug);

            _restApiService.Configure($"{restApiUrl}/{tenantSlug}", tenantSlug, apiKey);
            await _loyaltyService.ReinitializeAsync();

            var apiTest = await _cloudOrderService.TestConnectionAsync(restApiUrl, tenantSlug, apiKey);
            _lastRemoteApiHealthCheckUtc = DateTime.UtcNow;
            if (!apiTest.Success)
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = true,
                    IsEnabled = true,
                    IsApiHealthy = false,
                    LastError = apiTest.ErrorMessage,
                    StatusMessage = $"REST API unavailable: {apiTest.ErrorMessage}"
                });
            }

            if (configChanged || !_webSocketService.IsConnected)
            {
                _webSocketService.Configure(wsUrl, tenantSlug, apiKey);
                if (!_webSocketService.IsConnected)
                {
                    await _webSocketService.ConnectAsync();
                }
            }

            if (!_cloudOrderService.IsPolling)
            {
                await _cloudOrderService.StartPollingAsync();
            }

            if (!_heartbeatService.IsRunning)
            {
                await _heartbeatService.StartAsync();
            }

            _cloudOrderService.StartAckRetryService();

            if (configChanged || forceBackfill)
            {
                _ = Task.Run(async () =>
                {
                    var syncResult = await _cloudOrderService.SyncLastSevenDaysAsync();
                    System.Diagnostics.Debug.WriteLine($"OrderWeb keeper catch-up sync: {syncResult.Message}");
                });
            }

            RefreshStatus(apiHealthy: apiTest.Success, detail: BuildOperationalMessage(apiTest.Success));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OrderWeb keeper apply failed: {ex.Message}");
            RefreshStatus(apiHealthy: false, detail: "Connection error", error: ex.Message);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task EnsureHealthyAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigurationAsync();
            if (config == null || !config.IsConfigured() || !config.IsEnabled)
            {
                UpdateStatus(new OrderWebConnectionStatus
                {
                    IsConfigured = config?.IsConfigured() ?? false,
                    IsEnabled = config?.IsEnabled ?? false,
                    IsApiHealthy = false,
                    LastHealthCheckUtc = DateTime.UtcNow,
                    StatusMessage = config == null || !config.IsConfigured()
                        ? "OrderWeb settings are not configured"
                        : "OrderWeb sync is disabled — click Save Settings to enable"
                });
                return;
            }

            var fingerprint = BuildFingerprint(config);
            if (!string.Equals(fingerprint, _activeConfigFingerprint, StringComparison.Ordinal))
            {
                System.Diagnostics.Debug.WriteLine("OrderWeb config changed in database - reapplying");
                await ApplyConfigurationAsync(config);
                return;
            }

            var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
            if (!onlineMasterCheck.Allowed)
            {
                RefreshStatus(apiHealthy: false, detail: onlineMasterCheck.Reason);
                return;
            }

            var tenantSlug = config.TenantSlug.Trim();
            var restApiUrl = NormalizeApiBaseUrl(
                !string.IsNullOrWhiteSpace(config.RestApiBaseUrl) ? config.RestApiBaseUrl : config.ApiBaseUrl,
                tenantSlug);

            var apiHealthy = _status.IsApiHealthy;
            if (DateTime.UtcNow - _lastRemoteApiHealthCheckUtc >= RemoteApiHealthCheckInterval)
            {
                var apiTest = await _cloudOrderService.TestConnectionAsync(restApiUrl, tenantSlug, config.ApiKey.Trim());
                apiHealthy = apiTest.Success;
                _lastRemoteApiHealthCheckUtc = DateTime.UtcNow;
            }

            if (!_webSocketService.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("OrderWeb keeper: WebSocket offline, reconnecting");
                var wsUrl = NormalizeWebSocketUrl(config.WebSocketUrl, tenantSlug);
                _webSocketService.Configure(wsUrl, tenantSlug, config.ApiKey.Trim());
                await _webSocketService.ConnectAsync();
            }

            if (!_cloudOrderService.IsPolling)
            {
                System.Diagnostics.Debug.WriteLine("OrderWeb keeper: polling stopped, restarting");
                await _cloudOrderService.StartPollingAsync();
            }

            if (!_heartbeatService.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("OrderWeb keeper: heartbeat stopped, restarting");
                await _heartbeatService.StartAsync();
            }

            _cloudOrderService.StartAckRetryService();

            RefreshStatus(apiHealthy: apiHealthy, detail: BuildOperationalMessage(apiHealthy));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OrderWeb keeper health check failed: {ex.Message}");
            RefreshStatus(apiHealthy: false, detail: "Health check failed", error: ex.Message);
        }
    }

    public void Stop()
    {
        _heartbeatService.Stop();
        _cloudOrderService.StopPolling();
        _cloudOrderService.StopAckRetryService();
        _isStarted = false;
        RefreshStatus(apiHealthy: false, detail: "OrderWeb keeper stopped");
    }

    private void StartHealthMonitor()
    {
        System.Diagnostics.Debug.WriteLine("OrderWeb keeper health monitor is managed by background sync");
    }

    private void AttachWebSocketHandlers()
    {
        if (_webSocketHandlerAttached)
        {
            return;
        }

        _webSocketService.ConnectionStatusChanged += async (_, args) =>
        {
            if (args.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    var syncResult = await _cloudOrderService.SyncLastSevenDaysAsync();
                    System.Diagnostics.Debug.WriteLine($"OrderWeb reconnect catch-up: {syncResult.Message}");
                });
            }

            RefreshStatus(
                apiHealthy: _status.IsApiHealthy,
                detail: args.IsConnected
                    ? "Receiving orders in real-time"
                    : args.Message ?? "WebSocket disconnected - backup polling active");
        };

        _webSocketHandlerAttached = true;
    }

    private void RefreshStatus(bool apiHealthy, string detail, string? error = null)
    {
        UpdateStatus(new OrderWebConnectionStatus
        {
            IsConfigured = true,
            IsEnabled = true,
            IsWebSocketConnected = _webSocketService.IsConnected,
            IsPollingActive = _cloudOrderService.IsPolling,
            IsHeartbeatActive = _heartbeatService.IsRunning,
            IsAckRetryActive = _cloudOrderService.IsAckRetryActive,
            IsApiHealthy = apiHealthy,
            LastHealthCheckUtc = DateTime.UtcNow,
            LastSuccessfulConnectUtc = apiHealthy ? DateTime.UtcNow : _status.LastSuccessfulConnectUtc,
            StatusMessage = detail,
            LastError = error
        });
    }

    private void UpdateStatus(OrderWebConnectionStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    private static string BuildOperationalMessage(bool apiHealthy)
    {
        if (!apiHealthy)
        {
            return "OrderWeb REST API is unavailable";
        }

        return "OrderWeb connected - orders, ACK, gift card, and loyalty services are active";
    }

    private static string BuildFingerprint(CloudConfiguration config) =>
        string.Join('|',
            config.TenantSlug.Trim(),
            config.ApiKey.Trim(),
            NormalizeApiBaseUrl(
                !string.IsNullOrWhiteSpace(config.RestApiBaseUrl) ? config.RestApiBaseUrl : config.ApiBaseUrl,
                config.TenantSlug.Trim()),
            NormalizeWebSocketUrl(config.WebSocketUrl, config.TenantSlug.Trim()),
            config.IsEnabled,
            config.OnlineOrderMasterEnabled,
            config.OnlineOrderMasterTerminalName.Trim());

    private static string NormalizeApiBaseUrl(string? apiBaseUrl, string tenantSlug)
    {
        var baseUrl = (apiBaseUrl ?? "https://orderweb.net/api").Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(tenantSlug) &&
            baseUrl.EndsWith($"/{tenantSlug}", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^(tenantSlug.Length + 1)];
        }

        if (baseUrl.EndsWith("/pos/pull-orders", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/pos/pull-orders".Length].TrimEnd('/');
        }

        return OrderWebApiClient.NormalizeApiBaseUrl(baseUrl);
    }

    private static string NormalizeWebSocketUrl(string? websocketUrl, string tenantSlug)
    {
        var wsUrl = string.IsNullOrWhiteSpace(websocketUrl)
            ? "wss://orderweb.net/ws/pos"
            : websocketUrl.Trim();

        if (!string.IsNullOrWhiteSpace(tenantSlug) &&
            wsUrl.EndsWith($"/{tenantSlug}", StringComparison.OrdinalIgnoreCase))
        {
            wsUrl = wsUrl[..^(tenantSlug.Length + 1)];
        }

        return wsUrl;
    }
}
