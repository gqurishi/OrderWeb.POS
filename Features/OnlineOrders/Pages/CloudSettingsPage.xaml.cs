using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class CloudSettingsPage : ContentPage
{
    private DatabaseService? _databaseService;
    private OrderWebWebSocketService? _webSocketService;
    private OrderWebRestApiService? _restApiService;
    private CloudOrderService? _cloudOrderService;
    private CloudConfiguration? _currentConfig;
    private bool _isApiKeyVisible = false;
    private bool _isConnecting = false;
    private System.Threading.Timer? _statusUpdateTimer;
    private bool _isUpdatingStatus;
    private bool _servicesInitialized;
    private DateTime _lastHeavyStatusRefreshAt = DateTime.MinValue;
    
    public CloudSettingsPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Settings");
    }
    
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_servicesInitialized)
        {
            InitializeServices();
            _servicesInitialized = true;
        }

        _ = LoadSettingsAsync();
        
        // Start status update timer (light heartbeat every 3 seconds)
        _statusUpdateTimer?.Dispose();
        _statusUpdateTimer = new System.Threading.Timer(
            _ => MainThread.BeginInvokeOnMainThread(() => UpdateSystemStatus()),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3)
        );
    }
    
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Stop status update timer
        _statusUpdateTimer?.Dispose();
        _statusUpdateTimer = null;

        if (_servicesInitialized && _webSocketService != null)
        {
            _webSocketService.NewOrderReceived -= OnNewOrderReceived;
            _webSocketService.ConnectionStatusChanged -= OnWebSocketStatusChanged;
            _servicesInitialized = false;
        }
    }

    // Tab Navigation Handlers - Easy navigation between settings tabs
    private async void OnBusinessTabClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("//businesssettings");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }

    private async void OnUserTabClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("//usermanagement");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }

    private async void OnCloudTabClicked(object? sender, EventArgs e)
    {
        // Already on this page, just highlight the tab
        UpdateTabHighlight("cloud");
    }
    
    private async void OnPostcodeTabClicked(object? sender, EventArgs e)
    {
        try
        {
            // Navigate to Postcode Lookup page  
            var postcodeLookupService = ServiceHelper.GetService<PostcodeLookupService>();
            if (postcodeLookupService != null)
            {
                var postcodePage = new PostcodeLookupPage(postcodeLookupService);
                await Navigation.PushAsync(postcodePage);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }

    private void UpdateTabHighlight(string activeTab)
    {
        // Reset all tabs to default styling
        ResetTabToDefault(BusinessTabBorder);
        ResetTabToDefault(UserTabBorder);
        ResetTabToDefault(CloudTabBorder);
        ResetTabToDefault(PostcodeTabBorder);

        // Set all label colors to default
        SetTabLabelColor(BusinessTabBorder, "#6B7280");
        SetTabLabelColor(UserTabBorder, "#6B7280");
        SetTabLabelColor(CloudTabBorder, "#6B7280");
        SetTabLabelColor(PostcodeTabBorder, "#6B7280");

        // Highlight active tab with elegant styling
        switch (activeTab.ToLower())
        {
            case "business":
                HighlightActiveTab(BusinessTabBorder, "#E0F2FE", "#0EA5E9");
                break;
            case "user":
                HighlightActiveTab(UserTabBorder, "#DCFCE7", "#10B981");
                break;
            case "cloud":
                HighlightActiveTab(CloudTabBorder, "#F3E8FF", "#8B5CF6");
                break;
            case "postcode":
                HighlightActiveTab(PostcodeTabBorder, "#FEF2F2", "#EF4444");
                break;
        }
    }
    
    private void ResetTabToDefault(Border tabBorder)
    {
        tabBorder.BackgroundColor = Colors.White;
        tabBorder.Stroke = Color.FromArgb("#E5E7EB");
        tabBorder.StrokeThickness = 1;
    }
    
    private void HighlightActiveTab(Border tabBorder, string backgroundColor, string borderColor)
    {
        tabBorder.BackgroundColor = Color.FromArgb(backgroundColor);
        tabBorder.Stroke = Color.FromArgb(borderColor);
        tabBorder.StrokeThickness = 2;
        SetTabLabelColor(tabBorder, borderColor);
    }
    
    private void SetTabLabelColor(Border tabBorder, string color)
    {
        if (tabBorder.Content is Label label)
        {
            label.TextColor = Color.FromArgb(color);
        }
    }

    private void InitializeServices()
    {
        try
        {
            _databaseService = ServiceHelper.GetService<DatabaseService>();
            _webSocketService = ServiceHelper.GetService<OrderWebWebSocketService>();
            _restApiService = ServiceHelper.GetService<OrderWebRestApiService>();
            _cloudOrderService = ServiceHelper.GetService<CloudOrderService>();

            if (_webSocketService != null)
            {
                _webSocketService.NewOrderReceived += OnNewOrderReceived;
                _webSocketService.ConnectionStatusChanged += OnWebSocketStatusChanged;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing services: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Update system status indicators
    /// </summary>
    private async void UpdateSystemStatus()
    {
        if (_isUpdatingStatus)
        {
            return;
        }

        try
        {
            _isUpdatingStatus = true;

            // Update connection status
            if (_webSocketService != null)
            {
                var isConnected = _webSocketService.IsConnected;
                StatusConnectionIcon.Text = isConnected ? "🟢" : "🔴";
                StatusConnectionText.Text = isConnected ? "Live" : "Offline";
                StatusConnectionText.TextColor = isConnected ? Color.FromArgb("#10B981") : Color.FromArgb("#DC2626");
            }
            
            // Update polling/backup status
            if (_cloudOrderService != null)
            {
                var isPolling = _cloudOrderService.IsPolling;
                StatusBackupIcon.Text = isPolling ? "✅" : "⏸️";
                StatusBackupText.Text = isPolling ? "Active" : "Stopped";
                StatusBackupText.TextColor = isPolling ? Color.FromArgb("#10B981") : Color.FromArgb("#6B7280");
                
                // Update last sync time
                if (_cloudOrderService.LastSyncTime != default)
                {
                    StatusLastCheckText.Text = _cloudOrderService.LastSyncTime.ToLocalTime().ToString("HH:mm:ss");
                }
            }
            
            // Refresh heavier DB status less frequently to reduce load.
            var shouldRunHeavyCheck = (DateTime.UtcNow - _lastHeavyStatusRefreshAt).TotalSeconds >= 15;
            if (shouldRunHeavyCheck && _databaseService != null)
            {
                using var connection = await _databaseService.GetConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM pending_print_jobs WHERE created_at > DATE_SUB(NOW(), INTERVAL 1 HOUR)";
                
                try
                {
                    var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    StatusPrintQueueText.Text = count == 0 ? "0 pending" : $"{count} pending";
                    StatusPrintQueueText.TextColor = count > 0 ? Color.FromArgb("#DC2626") : Color.FromArgb("#10B981");
                }
                catch
                {
                    StatusPrintQueueText.Text = "0 pending";
                }
                
                // Update device ID
                command.CommandText = "SELECT value FROM cloud_config WHERE `key` = 'device_id' LIMIT 1";
                var deviceId = await command.ExecuteScalarAsync();
                if (deviceId != null && !string.IsNullOrEmpty(deviceId.ToString()))
                {
                    DeviceInfoText.Text = deviceId.ToString();
                }

                _lastHeavyStatusRefreshAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating system status: {ex.Message}");
        }
        finally
        {
            _isUpdatingStatus = false;
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            if (_databaseService == null)
            {
                UpdateStatus("⚠️ Database not available", "#DC3545", "Please restart the application");
                return;
            }

            UpdateStatus("⏳ Loading...", "#007BFF", "Please wait");

            _currentConfig = await _databaseService.GetCloudConfigurationAsync();

            if (_currentConfig != null && !string.IsNullOrEmpty(_currentConfig.TenantSlug))
            {
                // Load saved settings
                TenantSlugEntry.Text = _currentConfig.TenantSlug;
                ApiKeyEntry.Text = _currentConfig.ApiKey;
                
                // Clean up REST API URL - remove tenant suffix if present
                var restUrl = _currentConfig.RestApiBaseUrl ?? "https://orderweb.net/api";
                if (restUrl.EndsWith($"/{_currentConfig.TenantSlug}"))
                {
                    restUrl = restUrl.Substring(0, restUrl.Length - _currentConfig.TenantSlug.Length - 1);
                }
                RestApiUrlEntry.Text = restUrl;
                
                // Load WebSocket URL from database - accept ANY format (multi-tenant support)
                var wsUrl = _currentConfig.WebSocketUrl ?? "wss://orderweb.net:9011";
                
                System.Diagnostics.Debug.WriteLine($"📥 Loaded WebSocket URL from database: {wsUrl}");
                System.Diagnostics.Debug.WriteLine($"   Note: Multi-tenant URLs with paths like /ws/pos/tenant are fully supported");
                
                WebSocketUrlEntry.Text = wsUrl;
                
                ConnectButton.IsEnabled = true;
                SyncOrdersButton.IsEnabled = true;
                SyncHistoricalButton.IsEnabled = true;
                
                System.Diagnostics.Debug.WriteLine($"✅ Settings loaded: Restaurant={_currentConfig.TenantSlug}");
                System.Diagnostics.Debug.WriteLine($"✅ Credentials are configured and ready!");
                
                // Only auto-connect if NOT already connected
                if (_webSocketService != null && !_webSocketService.IsConnected)
                {
                    System.Diagnostics.Debug.WriteLine("🔄 Not connected - starting auto-connect...");
                    UpdateStatus("Auto-connecting...", "#007BFF", "Connecting to OrderWeb.net");
                    _ = Task.Run(async () => 
                    {
                        await Task.Delay(500);
                        await ConnectToServicesAsync(silentMode: true);
                    });
                }
                else if (_webSocketService != null && _webSocketService.IsConnected)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Already connected - skipping auto-connect");
                    UpdateStatus("Connected", "#28A745", "Receiving orders in real-time");
                }
                else
                {
                    UpdateStatus("⚪ Ready", "#6C757D", "Click Connect to start");
                }
            }
            else
            {
                // Set defaults
                RestApiUrlEntry.Text = "https://orderweb.net/api";
                WebSocketUrlEntry.Text = "wss://orderweb.net:9011";
                UpdateStatus("⚪ Not Configured", "#6C757D", "Enter your OrderWeb.net credentials");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            UpdateStatus("❌ Error", "#DC3545", ex.Message);
        }
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        // Enable buttons if basic fields are filled
        bool hasBasicInfo = !string.IsNullOrWhiteSpace(TenantSlugEntry.Text) && 
                           !string.IsNullOrWhiteSpace(ApiKeyEntry.Text);
        
        ConnectButton.IsEnabled = hasBasicInfo;
        SyncOrdersButton.IsEnabled = hasBasicInfo;
    }

    private void OnToggleApiKeyClicked(object sender, EventArgs e)
    {
        _isApiKeyVisible = !_isApiKeyVisible;
        ApiKeyEntry.IsPassword = !_isApiKeyVisible;
        ToggleApiKeyButton.Text = _isApiKeyVisible ? "👁️ Hide" : "👁️ Show";
    }



    private async void OnSaveSettingsClicked(object sender, EventArgs e)
    {
        try
        {
            if (_databaseService == null)
            {
                await ShowAlertAsync("Error", "Database service not available");
                return;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(TenantSlugEntry.Text))
            {
                await ShowAlertAsync("Validation Error", "Please enter Restaurant ID");
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiKeyEntry.Text))
            {
                await ShowAlertAsync("Validation Error", "Please enter API Key");
                return;
            }

            UpdateStatus("💾 Saving...", "#007BFF", "Please wait");

            var tenantSlug = TenantSlugEntry.Text.Trim();
            var wsUrl = WebSocketUrlEntry.Text?.Trim() ?? "wss://orderweb.net:9011";
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine("💾 SAVE SETTINGS CLICKED");
            System.Diagnostics.Debug.WriteLine($"📝 Saving WebSocket URL: {wsUrl}");
            System.Diagnostics.Debug.WriteLine($"   Multi-tenant format supported: wss://orderweb.net/ws/pos/{{tenant}}?apiKey={{key}}");
            System.Diagnostics.Debug.WriteLine($"   Port-based format supported: wss://orderweb.net:9011");
            System.Diagnostics.Debug.WriteLine($"   Query parameters will be preserved");

            System.Diagnostics.Debug.WriteLine($"💾 Saving to database:");
            System.Diagnostics.Debug.WriteLine($"   Restaurant ID: {tenantSlug}");
            System.Diagnostics.Debug.WriteLine($"   WebSocket URL: {wsUrl}");
            System.Diagnostics.Debug.WriteLine($"   REST API URL: {RestApiUrlEntry.Text?.Trim()}");

            var config = new CloudConfiguration
            {
                TenantSlug = tenantSlug,
                ApiKey = ApiKeyEntry.Text.Trim(),
                RestApiBaseUrl = RestApiUrlEntry.Text?.Trim() ?? "https://orderweb.net/api",
                WebSocketUrl = wsUrl,
                IsEnabled = true,
                ConnectionTimeout = 30,
                MaxRetryAttempts = 3,
                AutoPrintEnabled = true,
                NotificationsEnabled = true,
                PollingIntervalSeconds = 60
            };

            bool saved = await _databaseService.SaveCloudConfigurationAsync(config);

            if (saved)
            {
                _currentConfig = config;
                await ToastNotification.ShowAsync(
                    "Success",
                    "Settings saved successfully!",
                    Services.NotificationType.Success,
                    2500
                );
                UpdateStatus("Saved", "#28A745", "Settings saved");

                // Auto-connect after saving
                await Task.Delay(1000);
                await ConnectToServicesAsync();
            }
            else
            {
                await ToastNotification.ShowAsync(
                    "Error",
                    "Failed to save settings to database",
                    Services.NotificationType.Error,
                    3000
                );
                UpdateStatus("❌ Save Failed", "#DC3545", "Database error");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            await ShowAlertAsync("Error", $"Failed to save: {ex.Message}");
            UpdateStatus("❌ Error", "#DC3545", ex.Message);
        }
    }

    private async void OnConnectClicked(object sender, EventArgs e)
    {
        await ConnectToServicesAsync();
    }
    
    private async void OnSyncOrdersClicked(object sender, EventArgs e)
    {
        try
        {
            if (_cloudOrderService == null)
            {
                await ToastNotification.ShowAsync(
                    "Error",
                    "Cloud service not initialized. Please connect first.",
                    Services.NotificationType.Error,
                    3000
                );
                return;
            }
            
            SyncOrdersButton.IsEnabled = false;
            SyncOrdersButton.Text = "⏳ Syncing last 7 days...";
            
            System.Diagnostics.Debug.WriteLine("🔄 Quick sync: Fetching last 7 days of orders...");
            
            var sevenDaysAgo = DateTime.Today.AddDays(-7);
            var syncResult = await _cloudOrderService.SyncOrdersByDateAsync(sevenDaysAgo);
            var totalOrders = syncResult.OrdersFound;
            
            System.Diagnostics.Debug.WriteLine($"✅ Quick sync complete: {syncResult.Message}");
            
            if (totalOrders > 0)
            {
                await ToastNotification.ShowAsync(
                    "Sync Complete",
                    $"Found {totalOrders} orders from last 7 days",
                    Services.NotificationType.Success,
                    3000
                );
            }
            else
            {
                await ToastNotification.ShowAsync(
                    "No Orders",
                    "No orders found in the last 7 days",
                    Services.NotificationType.Info,
                    3000
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Sync error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            await ToastNotification.ShowAsync(
                "Sync Error",
                $"Failed: {ex.Message}",
                Services.NotificationType.Error,
                4000
            );
        }
        finally
        {
            SyncOrdersButton.Text = "🔄 Sync Orders Now";
            SyncOrdersButton.IsEnabled = true;
        }
    }
    private async Task ConnectToServicesAsync(bool silentMode = false)
    {
        if (_isConnecting) return;
        
        // Skip if already connected (prevents reconnection when navigating between pages)
        if (_webSocketService != null && _webSocketService.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine("✅ Already connected - skipping connection attempt");
            UpdateStatus("Connected", "#28A745", "Receiving orders in real-time");
            return;
        }

        try
        {
            _isConnecting = true;
            ConnectButton.IsEnabled = false;
            UpdateStatus("🔌 Connecting...", "#007BFF", "Starting services");

            var tenantId = TenantSlugEntry.Text?.Trim() ?? "";
            var apiKey = ApiKeyEntry.Text?.Trim() ?? "";
            var restApiUrl = RestApiUrlEntry.Text?.Trim() ?? "https://orderweb.net/api";
            var wsUrl = WebSocketUrlEntry.Text?.Trim() ?? "wss://orderweb.net:9011";
            
            // Validate configuration
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(apiKey))
            {
                UpdateStatus("Configuration Error", "#DC3545", "Restaurant ID and API Key are required");
                await ToastNotification.ShowAsync(
                    "Error",
                    "Please enter your Restaurant ID and API Key",
                    Services.NotificationType.Error,
                    3000
                );
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine("🔌 CONNECTING TO ORDERWEB.NET");
            System.Diagnostics.Debug.WriteLine($"🏪 Restaurant: {tenantId}");
            System.Diagnostics.Debug.WriteLine($"🔑 API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...");
            System.Diagnostics.Debug.WriteLine($"🌐 REST API: {restApiUrl}");
            System.Diagnostics.Debug.WriteLine($"📡 WebSocket: {wsUrl}");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            // CRITICAL FIX: Strip tenant from WebSocket URL if present
            if (!string.IsNullOrEmpty(tenantId) && wsUrl.EndsWith($"/{tenantId}"))
            {
                wsUrl = wsUrl.Substring(0, wsUrl.Length - tenantId.Length - 1);
                System.Diagnostics.Debug.WriteLine($"🔧 Cleaned WebSocket URL: {wsUrl}");
            }

            // Configure REST API - construct full URL with tenant
            if (_restApiService != null)
            {
                _restApiService.Configure($"{restApiUrl}/{tenantId}", tenantId, apiKey);
                System.Diagnostics.Debug.WriteLine($"✅ REST API configured: {restApiUrl}/{tenantId}");
            }

            // Configure and connect WebSocket - pass URL as-is (service handles tenant automatically)
            if (_webSocketService != null)
            {
                _webSocketService.Configure(wsUrl, tenantId, apiKey);
                bool connected = await _webSocketService.ConnectAsync();

                if (connected)
                {
                    UpdateStatus("Connected", "#28A745", "Receiving orders in real-time");
                    if (!silentMode)
                    {
                        await ToastNotification.ShowAsync(
                            "Success",
                            "Connected to OrderWeb.net! You will now receive orders in real-time.",
                            Services.NotificationType.Success,
                            3000
                        );
                    }
                }
                else
                {
                    UpdateStatus("Connection Failed", "#DC3545", "Check your credentials");
                    if (!silentMode)
                    {
                        await ToastNotification.ShowAsync(
                            "Connection Failed",
                            "Could not connect to OrderWeb.net. Please check your settings.",
                            Services.NotificationType.Error,
                            4000
                        );
                    }
                }
            }
            
            // CRITICAL: Start polling service (backup delivery method)
            if (_cloudOrderService != null)
            {
                System.Diagnostics.Debug.WriteLine("🔄 Starting REST polling service...");
                await _cloudOrderService.StartPollingAsync();
                System.Diagnostics.Debug.WriteLine("✅ REST polling started!");
                
                // Sync last 2 months with SINGLE API call using 'since' parameter
                System.Diagnostics.Debug.WriteLine("📦 Syncing last 2 months of orders (one request)...");
                var twoMonthsAgo = DateTime.Today.AddDays(-60);
                var syncResult = await _cloudOrderService.SyncOrdersByDateAsync(twoMonthsAgo);
                
                System.Diagnostics.Debug.WriteLine($"✅ Initial sync complete: {syncResult.Message}");
                
                if (!silentMode && syncResult.OrdersFound > 0)
                {
                    await ToastNotification.ShowAsync(
                        "Orders Synced",
                        $"Found {syncResult.OrdersFound} orders from the last 2 months",
                        Services.NotificationType.Info,
                        3000
                    );
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error connecting: {ex.Message}");
            UpdateStatus("Connection Error", "#DC3545", ex.Message);
            await ToastNotification.ShowAsync(
                "Connection Error",
                ex.Message,
                Services.NotificationType.Error,
                4000
            );
        }
        finally
        {
            _isConnecting = false;
            ConnectButton.IsEnabled = true;
        }
    }

    private void UpdateStatus(string status, string colorHex, string detail)
    {
        ConnectionStatusText.Text = status;
        ConnectionStatusText.TextColor = Color.FromArgb(colorHex);
        ConnectionStatusDescription.Text = detail;
        
        // Update status frame background and icon based on status
        if (status.Contains("Connected") && !status.Contains("Disconnected"))
        {
            ConnectionStatusFrame.BackgroundColor = Color.FromArgb("#D1FAE5"); // Green
            ConnectionStatusIcon.Text = "✅";
        }
        else if (status.Contains("Disconnected") || status.Contains("Failed") || status.Contains("Error"))
        {
            ConnectionStatusFrame.BackgroundColor = Color.FromArgb("#FEE2E2"); // Red
            ConnectionStatusIcon.Text = "❌";
        }
        else
        {
            ConnectionStatusFrame.BackgroundColor = Color.FromArgb("#FEF3C7"); // Yellow
            ConnectionStatusIcon.Text = "⚠️";
        }
    }
    
    private void UpdateConnectionStatusUI(bool isConnected)
    {
        if (isConnected)
        {
            StatusConnectionIcon.Text = "🟢";
            StatusConnectionText.Text = "Live";
            StatusConnectionText.TextColor = Color.FromArgb("#10B981");
        }
        else
        {
            StatusConnectionIcon.Text = "🔴";
            StatusConnectionText.Text = "Offline";
            StatusConnectionText.TextColor = Color.FromArgb("#DC2626");
        }
        
        // Update last check time
        StatusLastCheckText.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void OnWebSocketStatusChanged(object? sender, ConnectionStatusEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.IsConnected)
            {
                UpdateStatus("Connected", "#28A745", "Receiving orders in real-time");
                UpdateConnectionStatusUI(true);
            }
            else
            {
                UpdateStatus("Disconnected", "#DC3545", e.Message ?? "Connection lost");
                UpdateConnectionStatusUI(false);
                
                // Auto-reconnect if disconnected
                if (!_isConnecting)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(5000); // Wait 5 seconds before reconnect
                        await ConnectToServicesAsync();
                    });
                }
            }
        });
    }

    private void OnNewOrderReceived(object? sender, OrderReceivedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await ShowAlertAsync("New Order!", $"Order #{e.OrderId} received from OrderWeb.net\nCustomer: {e.CustomerName}\nTotal: £{e.TotalAmount:F2}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling new order: {ex.Message}");
            }
        });
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        try
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(title, message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Alert error: {ex.Message}");
        }
    }

    private async void OnSyncHistoricalClicked(object? sender, EventArgs e)
    {
        if (_cloudOrderService == null)
        {
            await ToastNotification.ShowAsync(
                "Error",
                "Cloud service not initialized. Please connect first.",
                Services.NotificationType.Error,
                3000
            );
            return;
        }

        try
        {
            SyncHistoricalButton.IsEnabled = false;
            SyncHistoricalButton.Text = "⏳ Syncing last 2 months...";
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine("📅 MANUAL HISTORICAL SYNC STARTED");
            System.Diagnostics.Debug.WriteLine("📦 Fetching orders from last 60 days...");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            var sixtyDaysAgo = DateTime.Today.AddDays(-60);
            var syncResult = await _cloudOrderService.SyncOrdersByDateAsync(sixtyDaysAgo);
            var totalOrders = syncResult.OrdersFound;
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"✅ HISTORICAL SYNC COMPLETE");
            System.Diagnostics.Debug.WriteLine($"📊 Total: {totalOrders} orders");
            System.Diagnostics.Debug.WriteLine($"🧾 Result: {syncResult.Message}");
            System.Diagnostics.Debug.WriteLine($"📅 Date range: {DateTime.Today.AddDays(-59):MMM dd} - {DateTime.Today:MMM dd, yyyy}");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            await ToastNotification.ShowAsync(
                "Historical Sync Complete",
                $"Found {totalOrders} orders from the last 2 months",
                Services.NotificationType.Success,
                4000
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Historical sync error: {ex.Message}");
            await ToastNotification.ShowAsync(
                "Sync Error",
                ex.Message,
                Services.NotificationType.Error,
                4000
            );
        }
        finally
        {
            SyncHistoricalButton.IsEnabled = true;
            SyncHistoricalButton.Text = "📅 Sync Last 2 Months (Historical Orders)";
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        try
        {
            // Get authentication service
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
            }

            // Navigate to login page immediately
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Logout error: {ex.Message}");
            // Still navigate to login even if logout service fails
            await Shell.Current.GoToAsync("//login");
        }
    }

    private async void OnBusinessSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//businesssettings");
    }

    private async void OnUserManagementClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//usermanagement");
    }
}

public static class ServiceHelper
{
    public static T? GetService<T>() where T : class
    {
        try
        {
            var current = Application.Current?.Handler?.MauiContext?.Services;
            if (current == null)
                return null;
                
            return current.GetService(typeof(T)) as T;
        }
        catch
        {
            return null;
        }
    }
}
