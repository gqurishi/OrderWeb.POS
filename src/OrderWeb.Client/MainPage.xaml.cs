using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Views.Layout;
using OrderWeb.Client.Views.Printing;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;

namespace OrderWeb.Client;

public partial class MainPage : ContentPage
{
    private const string PageBackground = "#FFFFFF";
    private const string TopBarBackground = "#FFFFFF";
    private const string LightSurface = "#F6F9FC";
    private const string MainText = "#1F2937";
    private const string SidebarText = "#1E293B";
    private const string MutedText = "#6B7280";
    private const string SecondaryText = "#374151";
    private const string AccentBlue = "#3B82F6";
    private const string DashboardLabelBlue = "#1D4ED8";
    private const string SidebarSelected = "#E3F2FD";
    private const string PrimaryAction = "#6366F1";
    private const string Success = "#059669";
    private const string Warning = "#F59E0B";
    private const string Danger = "#DC2626";
    private const string BorderLight = "#E5E7EB";
    private const double SidebarWidth = 280;
    private const double SidebarIconSize = 32;
    private const double HeaderMenuIconSize = 66;
    private const double HeaderLogoutIconSize = 42;
    private const double SidebarLogoSize = 100;
    private const double DashboardIconSize = 190;
    private const string LastMotherIpKey = "client_pos_last_mother_ip";
    private const string LastPairingCodeKey = "client_pos_last_pairing_code";
    private const string LastTerminalNameKey = "client_pos_last_terminal_name";
    private bool IsCompactLayout => Width > 0 && Width < 900;
    private double ResponsiveDashboardIconSize => IsCompactLayout ? 150 : DashboardIconSize;
    private double ResponsiveManagerIconSize => IsCompactLayout ? 150 : DashboardIconSize;

    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherBootstrapClient _bootstrapClient;
    private readonly MotherAuthClient _authClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly MotherCustomerClient _customerClient;
    private readonly MotherPrintClient _printClient = new();
    private readonly MotherEventClient _motherEvents;
    private readonly MotherConfigurationVersionClient _configurationVersions;
    private readonly MotherHeartbeatClient _motherHeartbeat;
    private readonly MotherLayoutClient _layoutClient;
    private readonly MotherMenuClient _menuClient;
    private ClientAuthenticationService? _sharedLoginAuth;
    private LoginViewModel? _sharedLoginViewModel;
    private IReadOnlyList<CachedFloor> _cachedFloors = Array.Empty<CachedFloor>();
    private CachedFloor? _selectedCachedFloor;
    private CachedTable? _selectedCachedTable;
    private IReadOnlyList<CachedMenuCategory> _cachedCategories = Array.Empty<CachedMenuCategory>();
    private CachedMenuCategory? _selectedCachedCategory;
    private IReadOnlyList<CachedProduct> _cachedProducts = Array.Empty<CachedProduct>();
    private IReadOnlyList<PrintRequestState> _recentPrintRequests = Array.Empty<PrintRequestState>();
    private MotherOrderState? _currentOrder;
    private CacheStatus? _cacheStatus;
    private int _guests = 4;
    private string? _pendingCustomerOrderId;
    private string _pin = string.Empty;
    private string? _loginStatusMessage;
    private bool _loginMotherUnreachable;
    private bool _adminBlockedVisible;
    private string _connectionStatus = "Connected";
    private LoginSession? _currentSession;
    private Label? _timeLabel;
    private Label? _dateLabel;
    private Label? _statusLabel;
    private bool _useLoginClockFormat;
    private bool _terminalDisabled;
    private string _terminalDisabledReason = "This Client POS has been disabled by the Mother POS.";
    private bool _posSidebarOpen;
    private string _posSelectedMenu = "Dashboard";
    private string _liveOrderFilter = "All";
    private bool _isViewingOrderScreen;
    private bool _openOrderLiveReloadInFlight;
    private VisualElement? _posSidebarView;
    private Grid? _posSidebarOverlay;
    private Entry? _motherIpEntry;
    private Entry? _pairingCodeEntry;
    private Entry? _terminalNameEntry;
    private BootstrapRequest? _lastBootstrapRequest;
    private bool _bootstrapInProgress;
    private bool _hasPairedMother;
    private bool _motherConnectBusy;
    private readonly IDispatcherTimer _clockTimer;
    private ApplicationShellFrame? _activeApplicationFrame;
    private DashboardViewModel? _sharedDashboardViewModel;
    private RestaurantTablesView? _restaurantTablesView;
    private readonly Label[] _cashierSummaryLabels = new Label[8];
    private Label? _cashierDataStatusLabel;
    private MotherCashierClient? _cashierClient;
    private readonly IDispatcherTimer _cashierRefreshTimer;

    public MainPage()
    {
        InitializeComponent();
        _bootstrapClient = new MotherBootstrapClient();
        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _customerClient = new MotherCustomerClient();
        _cashierClient = new MotherCashierClient(_cache);
        _motherEvents = new MotherEventClient(_cache);
        _configurationVersions = new MotherConfigurationVersionClient(_cache);
        _motherHeartbeat = new MotherHeartbeatClient(_cache, () => _currentSession);
        _layoutClient = new MotherLayoutClient(_cache);
        _menuClient = new MotherMenuClient(_cache);
        _motherEvents.TerminalControlReceived += OnMotherTerminalControlReceived;
        _motherEvents.AuthoritativeDataChanged += OnMotherAuthoritativeDataChanged;
        _motherEvents.ConnectionChanged += OnMotherConnectionChanged;
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        _cashierRefreshTimer = Dispatcher.CreateTimer();
        _cashierRefreshTimer.Interval = TimeSpan.FromSeconds(45);
        _cashierRefreshTimer.Tick += async (_, _) => await RefreshCashierDashboardAsync();
        ShowCheckingMother();
        _ = InitializeCacheAsync();
    }

    private async Task InitializeCacheAsync()
    {
        await _cache.InitializeAsync();
        _cacheStatus = await _cache.GetStatusAsync();
        _terminalDisabled = await _cache.IsTerminalDisabledAsync();
        _terminalDisabledReason = await _cache.GetTerminalDisabledReasonAsync();
        await _motherEvents.StartAsync();
        if (!_terminalDisabled)
        {
            await _motherHeartbeat.StartAsync();
        }
        if (_terminalDisabled)
        {
            MainThread.BeginInvokeOnMainThread(ShowTerminalDisabled);
            return;
        }

        _hasPairedMother = await HasPairedMotherAsync();
        if (!_hasPairedMother)
        {
            MainThread.BeginInvokeOnMainThread(() => ShowConnect());
            return;
        }

        var settings = await _cache.GetMotherConnectionAsync();
        var motherAddress = SavedMotherAddress(settings);
        if (!string.IsNullOrWhiteSpace(motherAddress))
        {
            Preferences.Set(LastMotherIpKey, motherAddress);
        }
        MainThread.BeginInvokeOnMainThread(() => ShowCheckingMother(motherAddress));
        var probe = await _offlinePolicy.ProbeAsync();
        await ApplyMotherProbeAsync(probe, settings?.ApiBaseUrl, updateEndpoints: false);
    }

    private async Task<bool> HasPairedMotherAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        return settings is not null
            && !string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            && !string.IsNullOrWhiteSpace(settings.WebSocketUrl)
            && !string.IsNullOrWhiteSpace(settings.TerminalId)
            && !string.IsNullOrWhiteSpace(settings.TerminalToken);
    }

    private void ShowCheckingMother(string? motherAddress = null)
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        var address = string.IsNullOrWhiteSpace(motherAddress)
            ? Preferences.Get(LastMotherIpKey, string.Empty)
            : motherAddress;
        var status = string.IsNullOrWhiteSpace(address)
            ? "Looking up the saved Mother POS connection."
            : $"Connecting to {address}";

        Root.Children.Add(new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromArgb(PageBackground),
            Padding = 32,
            WidthRequest = 620,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = "OrderWeb Client POS", FontSize = 38, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Checking Mother POS…", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = status, FontSize = 16, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center },
                    new ActivityIndicator { IsRunning = true, Color = Color.FromArgb(AccentBlue), HeightRequest = 42, WidthRequest = 42, HorizontalOptions = LayoutOptions.Center }
                }
            }
        });
    }

    private void ShowConnect(string? problemMessage = null, bool reconnectMode = false, bool requirePairingCode = false)
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        var pairingRequired = requirePairingCode || !reconnectMode;
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        _motherIpEntry = new Entry { Placeholder = "Mother POS IP address", Text = SavedMotherAddress(), FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground) };
        _pairingCodeEntry = new Entry
        {
            Placeholder = pairingRequired ? "6-digit code from Mother POS" : "Optional — only if Mother issued a new code",
            Text = reconnectMode ? string.Empty : Preferences.Get(LastPairingCodeKey, string.Empty),
            FontSize = 18,
            HeightRequest = 58,
            BackgroundColor = Color.FromArgb(PageBackground),
            Keyboard = Keyboard.Numeric
        };
        _terminalNameEntry = new Entry { Placeholder = "Terminal name", Text = LastTerminalName(), FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground) };

        var children = new List<IView>
        {
            new Label { Text = "OrderWeb Client POS", FontSize = 38, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
            new Label
            {
                Text = reconnectMode
                    ? "Reconnect with the same details, update the Mother IP, or enter a new pairing code."
                    : "Connect this device to the Mother POS.",
                FontSize = 17,
                TextColor = Color.FromArgb(MutedText),
                HorizontalTextAlignment = TextAlignment.Center
            }
        };

        if (!string.IsNullOrWhiteSpace(problemMessage))
        {
            children.Add(new Label
            {
                Text = problemMessage,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(Danger),
                HorizontalTextAlignment = TextAlignment.Center
            });
        }

        children.Add(Field("Mother IP Address", _motherIpEntry));
        children.Add(Field(
            pairingRequired ? "Pairing Code" : "Pairing Code (optional)",
            _pairingCodeEntry));
        children.Add(Field("Terminal Name", _terminalNameEntry));
        children.Add(ClientDeviceIdPanel());
        if (reconnectMode)
        {
            if (pairingRequired)
            {
                children.Add(PrimaryButton("New pairing code", PrimaryAction, async (_, _) => await StartNewPairingAsync()));
                children.Add(OutlineButton("Reconnect", async (_, _) => await ReconnectToMotherAsync()));
                children.Add(OutlineButton("Update IP", async (_, _) => await UpdateMotherIpAsync()));
            }
            else
            {
                children.Add(PrimaryButton("Reconnect", PrimaryAction, async (_, _) => await ReconnectToMotherAsync()));
                children.Add(OutlineButton("Update IP", async (_, _) => await UpdateMotherIpAsync()));
                children.Add(OutlineButton("New pairing code", async (_, _) => await StartNewPairingAsync()));
            }
        }
        else
        {
            children.Add(PrimaryButton("Connect & Bootstrap", PrimaryAction, async (_, _) => await StartBootstrapAsync()));
        }

        children.Add(CacheStatusPanel());
        children.Add(new Label
        {
            Text = reconnectMode
                ? "Reconnect uses the saved Mother. Update IP keeps this terminal pairing. A new pairing code is only needed if Mother rejected this till."
                : "SQLite cache is used for offline continuity after pairing.",
            FontSize = 13,
            TextColor = Color.FromArgb("#94A3B8"),
            HorizontalTextAlignment = TextAlignment.Center
        });

        var stack = new VerticalStackLayout { Spacing = 18 };
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        var panel = new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromArgb(PageBackground),
            Padding = 32,
            WidthRequest = 620,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = stack
        };

        Root.Children.Add(new ScrollView
        {
            Content = panel,
            VerticalOptions = LayoutOptions.Fill
        });
    }

    private static string SavedMotherAddress(MotherConnectionSettings? settings = null)
    {
        var saved = Preferences.Get(LastMotherIpKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        return HostFromApiBaseUrl(settings?.ApiBaseUrl);
    }

    private static string HostFromApiBaseUrl(string? apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        return apiBaseUrl;
    }

    private static string LastTerminalName()
    {
        var saved = Preferences.Get(LastTerminalNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        return string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "Client POS" : DeviceInfo.Name;
    }

    private string CurrentRestaurantName()
    {
        return "Restaurant POS";
    }

    private void ShowTerminalDisabled()
    {
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        _currentSession = null;
        _pin = string.Empty;
        _posSidebarOpen = false;

        Root.Children.Add(new Border
        {
            Stroke = Color.FromArgb("#FECACA"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromArgb("#FEF2F2"),
            Padding = 32,
            WidthRequest = 620,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    new Label { Text = "Client POS Disabled", FontSize = 36, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Danger), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = string.IsNullOrWhiteSpace(_terminalDisabledReason) ? "This terminal has been disabled by the Mother POS." : _terminalDisabledReason, FontSize = 17, TextColor = Color.FromArgb("#7F1D1D"), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Ask the Mother POS operator to create a new pairing code before this terminal can be used again.", FontSize = 14, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center },
                    PrimaryButton("Enter New Pairing Code", PrimaryAction, async (_, _) =>
                    {
                        await _cache.ClearTerminalDisabledAsync();
                        _terminalDisabled = false;
                        _terminalDisabledReason = string.Empty;
                        ShowConnect();
                    })
                }
            }
        });
    }

    private async void OnMotherTerminalControlReceived(object? sender, MotherTerminalControlEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _currentSession = null;
            _pin = string.Empty;

            if (string.Equals(e.EventType, "TERMINAL_DISABLED", StringComparison.OrdinalIgnoreCase))
            {
                _terminalDisabled = true;
                _terminalDisabledReason = e.Message;
                await _motherHeartbeat.StopAsync();
                ShowTerminalDisabled();
                return;
            }

            Logout();
            await DisplayAlert("Mother POS", e.Message, "OK");
        });
    }

    private async void OnMotherAuthoritativeDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        // Events carry no business data. Fetch fresh authoritative snapshots
        // from Mother instead of merging event payloads into the Client cache.
        await RefreshAuthoritativeClientCacheAsync();
        _cacheStatus = await _cache.GetStatusAsync();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _connectionStatus = "Updates available";

            if (IsOrderUpdatedEvent(e) &&
                _isViewingOrderScreen &&
                _currentOrder is not null &&
                (string.IsNullOrWhiteSpace(e.Version) ||
                 string.Equals(e.Version.Trim(), _currentOrder.OrderId, StringComparison.OrdinalIgnoreCase)))
            {
                await ReloadOpenOrderFromMotherLiveAsync();
                return;
            }

            RefreshCurrentPosPage();
        });
    }

    private static bool IsOrderUpdatedEvent(MotherDataChangedEventArgs e) =>
        !string.IsNullOrWhiteSpace(e.EventType) &&
        e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase);

    private async Task ReloadOpenOrderFromMotherLiveAsync()
    {
        if (_currentOrder is null || _openOrderLiveReloadInFlight)
        {
            return;
        }

        _openOrderLiveReloadInFlight = true;
        try
        {
            var previousVersion = _currentOrder.Version;
            var previousUpdated = _currentOrder.UpdatedUtc;
            var opened = await _orderClient.OpenOrderForEditAsync(_currentOrder.OrderId);
            var changedElsewhere =
                opened.State.Version != previousVersion ||
                !string.Equals(opened.State.UpdatedUtc, previousUpdated, StringComparison.Ordinal);
            await ApplyMotherOrderResultAsync(opened);
            if (changedElsewhere)
            {
                ShowToast("Order updated from another terminal");
            }

            ShowOrder();
        }
        catch
        {
            ShowToast("Order changed on Mother — returning to Live Order");
            _currentOrder = null;
            _isViewingOrderScreen = false;
            ShowLiveOrders(_liveOrderFilter);
        }
        finally
        {
            _openOrderLiveReloadInFlight = false;
        }
    }

    private void OnMotherConnectionChanged(object? sender, MotherConnectionChangedEventArgs e)
    {
        if (e.Connected)
        {
            // Reconnect never proves that no notification was missed, so fetch
            // authoritative snapshots before using cached data as current.
            _ = RefreshAuthoritativeClientCacheAsync();
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _connectionStatus = e.Connected
                ? "Mother online"
                : e.Status.Contains("Reconnect", StringComparison.OrdinalIgnoreCase)
                    ? "Reconnect required"
                    : "Mother offline";
            RefreshCurrentPosPage();
        });
    }

    private async Task RefreshAuthoritativeClientCacheAsync()
    {
        if (_currentSession is null || !await _offlinePolicy.IsMotherOnlineAsync())
        {
            return;
        }

        await _configurationVersions.CompareAsync();
        await _menuClient.RefreshCacheAsync();
        var layout = await _layoutClient.GetLayoutAsync();
        if (layout is not null)
        {
            await _cache.ReplaceLayoutAsync(
                new FloorSnapshotDto(layout.Version, layout.Floors),
                new TableSnapshotDto(layout.Version, layout.Tables));
        }

        var orders = await _orderClient.GetOpenOrdersAsync();
        if (orders is not null)
        {
            await _cache.ReplaceOperationalOrdersAsync(orders);
        }

        _cacheStatus = await _cache.GetStatusAsync();
    }

    private async Task StartBootstrapAsync(BootstrapRequest? retryRequest = null)
    {
        if (_bootstrapInProgress)
        {
            return;
        }

        var request = retryRequest ?? CreateBootstrapRequestFromForm();
        if (request is null)
        {
            return;
        }

        _bootstrapInProgress = true;
        _lastBootstrapRequest = request;
        var progressView = ShowBootstrapProgress("Connecting", 0.05);

        try
        {
            var progress = new Progress<BootstrapProgress>(item =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateBootstrapProgress(progressView, item.Message, item.Progress);
                });
            });

            var payload = await _bootstrapClient.RequestBootstrapAsync(request, progress);
            _connectionStatus = "Syncing";
            UpdateBootstrapProgress(progressView, "Saving bootstrap to SQLite cache...", 0.96);
            RememberBootstrapRequest(request);
            await _cache.SaveBootstrapAsync(payload);
            await _cache.ClearTerminalDisabledAsync();
            _terminalDisabled = false;
            _terminalDisabledReason = string.Empty;
            _hasPairedMother = true;
            await _motherEvents.StartAsync();
            await _motherHeartbeat.StartAsync();
            _cacheStatus = await _cache.GetStatusAsync();
            _connectionStatus = "Connected";
            UpdateBootstrapProgress(progressView, "Preparing POS...", 1.0);
            await Task.Delay(250);
            ShowLogin();
        }
        catch (Exception ex)
        {
            RememberBootstrapRequest(request);
            _connectionStatus = "Mother Offline";
            var diagnostics = ex is BootstrapException bootstrapException
                ? bootstrapException.Diagnostics ?? _bootstrapClient.LastDiagnostics
                : _bootstrapClient.LastDiagnostics;
            await _cache.QueuePendingActionAsync("bootstrap_failed", new { request.TerminalName, request.MotherIpAddress, error = ex.Message, diagnostics });
            ShowBootstrapFailure(request, ex.Message, diagnostics);
        }
        finally
        {
            _bootstrapInProgress = false;
        }
    }

    private BootstrapRequest? CreateBootstrapRequestFromForm()
    {
        var motherIpAddress = _motherIpEntry?.Text?.Trim() ?? string.Empty;
        var pairingCode = _pairingCodeEntry?.Text?.Trim() ?? string.Empty;
        var terminalName = _terminalNameEntry?.Text?.Trim() ?? string.Empty;
        RememberBootstrapForm(motherIpAddress, pairingCode, terminalName);

        if (string.IsNullOrWhiteSpace(motherIpAddress))
        {
            _ = DisplayAlert("Mother IP required", "Enter the Mother POS IP address shown on the Mother POS terminal setup screen.", "OK");
            return null;
        }

        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            _ = DisplayAlert("Pairing code required", "Enter the pairing code generated by Mother POS.", "OK");
            return null;
        }

        return new BootstrapRequest(
            motherIpAddress,
            pairingCode,
            string.IsNullOrWhiteSpace(terminalName) ? "Client POS" : terminalName);
    }

    private static void RememberBootstrapRequest(BootstrapRequest request)
    {
        RememberBootstrapForm(request.MotherIpAddress, request.PairingCode, request.TerminalName);
    }

    private static void RememberBootstrapForm(string motherIpAddress, string pairingCode, string terminalName)
    {
        if (!string.IsNullOrWhiteSpace(motherIpAddress))
        {
            Preferences.Set(LastMotherIpKey, motherIpAddress.Trim());
        }

        if (!string.IsNullOrWhiteSpace(pairingCode))
        {
            Preferences.Set(LastPairingCodeKey, pairingCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(terminalName))
        {
            Preferences.Set(LastTerminalNameKey, terminalName.Trim());
        }
    }

    private async Task ReconnectToMotherAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var apiBaseUrl = settings?.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            var motherIpAddress = _motherIpEntry?.Text?.Trim() ?? SavedMotherAddress();
            if (string.IsNullOrWhiteSpace(motherIpAddress))
            {
                await DisplayAlertAsync("Mother IP required", "Enter the Mother POS IP address, then tap Reconnect.", "OK");
                return;
            }

            apiBaseUrl = MotherBootstrapClient.BuildApiBaseUrl(motherIpAddress);
        }

        await ProbeMotherAndContinueAsync(apiBaseUrl, updateEndpoints: false);
    }

    private async Task UpdateMotherIpAsync()
    {
        var motherIpAddress = _motherIpEntry?.Text?.Trim() ?? string.Empty;
        var terminalName = _terminalNameEntry?.Text?.Trim() ?? string.Empty;
        RememberBootstrapForm(motherIpAddress, string.Empty, terminalName);
        if (string.IsNullOrWhiteSpace(motherIpAddress))
        {
            await DisplayAlertAsync("Mother IP required", "Enter the new Mother POS IP address, then tap Update IP.", "OK");
            return;
        }

        if (!_hasPairedMother)
        {
            await DisplayAlertAsync("Pairing code required", "This terminal is not paired yet. Enter a pairing code, then tap New pairing code.", "OK");
            return;
        }

        await ProbeMotherAndContinueAsync(MotherBootstrapClient.BuildApiBaseUrl(motherIpAddress), updateEndpoints: true);
    }

    private async Task StartNewPairingAsync()
    {
        var pairingCode = _pairingCodeEntry?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            await DisplayAlertAsync("Pairing code required", "Enter the new pairing code generated by Mother POS.", "OK");
            return;
        }

        await StartBootstrapAsync();
    }

    private async Task ProbeMotherAndContinueAsync(string apiBaseUrl, bool updateEndpoints)
    {
        if (_motherConnectBusy)
        {
            return;
        }

        _motherConnectBusy = true;
        var address = HostFromApiBaseUrl(apiBaseUrl);
        await MainThread.InvokeOnMainThreadAsync(() => ShowCheckingMother(address));
        try
        {
            var probe = await _offlinePolicy.ProbeAsync(apiBaseUrl);
            await ApplyMotherProbeAsync(probe, apiBaseUrl, updateEndpoints);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => ShowConnect(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? $"Cannot reach Mother POS at {address}."
                    : ex.Message,
                reconnectMode: true));
        }
        finally
        {
            _motherConnectBusy = false;
        }
    }

    private async Task ApplyMotherProbeAsync(MotherProbeResult probe, string? apiBaseUrl, bool updateEndpoints)
    {
        switch (probe.Status)
        {
            case MotherProbeStatus.Online:
                if (updateEndpoints && !string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    await SaveMotherEndpointsIfChangedAsync(apiBaseUrl);
                }

                _connectionStatus = "Mother online";
                await MainThread.InvokeOnMainThreadAsync(() => ShowLogin());
                return;
            case MotherProbeStatus.PairingInvalid:
                await MainThread.InvokeOnMainThreadAsync(() => ShowConnect(probe.Message, reconnectMode: true, requirePairingCode: true));
                return;
            case MotherProbeStatus.TerminalDisabled:
                _terminalDisabled = true;
                _terminalDisabledReason = probe.Message;
                await _cache.MarkTerminalDisabledAsync(probe.Message);
                await _motherHeartbeat.StopAsync();
                await MainThread.InvokeOnMainThreadAsync(ShowTerminalDisabled);
                return;
            default:
                await MainThread.InvokeOnMainThreadAsync(() => ShowConnect(probe.Message, reconnectMode: true));
                return;
        }
    }

    private async Task SaveMotherEndpointsIfChangedAsync(string apiBaseUrl)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null)
        {
            return;
        }

        if (string.Equals(settings.ApiBaseUrl.TrimEnd('/'), apiBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var webSocketUrl = MotherBootstrapClient.BuildWebSocketUrl(apiBaseUrl, settings.WebSocketUrl);
        await _motherEvents.StopAsync();
        await _motherHeartbeat.StopAsync();
        await _cache.UpdateMotherEndpointsAsync(apiBaseUrl, webSocketUrl);
        await _motherEvents.StartAsync();
        await _motherHeartbeat.StartAsync();
        RememberBootstrapForm(HostFromApiBaseUrl(apiBaseUrl), string.Empty, LastTerminalName());
    }

    private VerticalStackLayout ShowBootstrapProgress(string message, double progress)
    {
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);

        var status = new Label
        {
            Text = message,
            FontSize = 21,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(MainText),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var progressBar = new ProgressBar
        {
            Progress = progress,
            ProgressColor = Color.FromArgb(AccentBlue),
            HeightRequest = 10
        };

        var percent = new Label
        {
            Text = $"{Math.Round(progress * 100)}%",
            FontSize = 15,
            TextColor = Color.FromArgb(MutedText),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var steps = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                BootstrapStep("Connecting", progress >= 0.10),
                BootstrapStep("Downloading menu", progress >= 0.35),
                BootstrapStep("Downloading tables", progress >= 0.55),
                BootstrapStep("Downloading open orders", progress >= 0.78),
                BootstrapStep("Preparing POS", progress >= 0.92)
            }
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 24,
            WidthRequest = 640,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Bootstrap Sync", FontSize = 40, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Initial data is being downloaded from Mother POS and saved into local SQLite cache.", FontSize = 17, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center },
                status,
                progressBar,
                percent,
                steps,
                new Label { Text = "Mother POS remains the source of truth. This device is preparing its local cache.", FontSize = 13, TextColor = Color.FromArgb("#B45309"), HorizontalTextAlignment = TextAlignment.Center }
            }
        };

        Root.Children.Add(stack);
        return stack;
    }

    private void UpdateBootstrapProgress(VerticalStackLayout stack, string message, double progress)
    {
        if (stack.Children[2] is Label status)
        {
            status.Text = message;
        }

        if (stack.Children[3] is ProgressBar progressBar)
        {
            progressBar.Progress = progress;
        }

        if (stack.Children[4] is Label percent)
        {
            percent.Text = $"{Math.Round(progress * 100)}%";
        }

        if (stack.Children[5] is VerticalStackLayout steps)
        {
            steps.Children.Clear();
            steps.Children.Add(BootstrapStep("Connecting", progress >= 0.10));
            steps.Children.Add(BootstrapStep("Downloading menu", progress >= 0.35));
            steps.Children.Add(BootstrapStep("Downloading tables", progress >= 0.55));
            steps.Children.Add(BootstrapStep("Downloading open orders", progress >= 0.78));
            steps.Children.Add(BootstrapStep("Preparing POS", progress >= 0.92));
        }
    }

    private View BootstrapStep(string text, bool done)
    {
        return new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = done ? "✓" : "○", FontSize = 20, TextColor = Color.FromArgb(done ? "#10B981" : "#94A3B8"), WidthRequest = 28, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = text, FontSize = 16, FontAttributes = done ? FontAttributes.Bold : FontAttributes.None, TextColor = Color.FromArgb(done ? "#111827" : "#64748B"), WidthRequest = 260 }
            }
        };
    }

    private void ShowBootstrapFailure(BootstrapRequest request, string message, BootstrapDiagnostics? diagnostics)
    {
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);

        var isRateLimited = IsTooManyPairingAttempts(message, diagnostics);
        var stack = new VerticalStackLayout
        {
            Spacing = 18,
            WidthRequest = 760,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Bootstrap Failed", FontSize = 40, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Danger), HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = BootstrapFailureMessage(message, isRateLimited), FontSize = 17, TextColor = Color.FromArgb(SecondaryText), HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = $"Mother: {request.MotherIpAddress} | Terminal: {request.TerminalName}", FontSize = 14, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center },
                BootstrapDiagnosticsPanel(diagnostics),
                isRateLimited
                    ? new Label { Text = "Use Reset Pairing, create a fresh Mother code, or reset this Client device id during testing.", FontSize = 14, TextColor = Color.FromArgb("#B45309"), HorizontalTextAlignment = TextAlignment.Center }
                    : PrimaryButton("Retry Bootstrap", "#2563EB", async (_, _) => await StartBootstrapAsync(request)),
                OutlineButton("Reset Pairing", (_, _) => ShowConnect())
            }
        };

        Root.Children.Add(stack);
    }

    private View ClientDeviceIdPanel()
    {
        return new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12),
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label { Text = "Client Device ID", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MutedText) },
                    new Label { Text = MotherBootstrapClient.GetDeviceId(), FontSize = 13, TextColor = Color.FromArgb(SecondaryText), LineBreakMode = LineBreakMode.CharacterWrap },
                    OutlineButton("Reset Device ID", async (_, _) => await ResetClientDeviceIdAsync())
                }
            }
        };
    }

    private async Task ResetClientDeviceIdAsync()
    {
        var confirmed = await DisplayAlert(
            "Reset Device ID",
            "Use this only during testing when Mother has blocked this Client device. After resetting, create a new Mother pairing code.",
            "Reset",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        MotherBootstrapClient.ResetDeviceId();
        Preferences.Remove(LastPairingCodeKey);
        ShowConnect();
    }

    private static View BootstrapDiagnosticsPanel(BootstrapDiagnostics? diagnostics)
    {
        var text = diagnostics is null
            ? "Bootstrap Debug\nNo diagnostics captured before failure."
            : $"""
              Bootstrap Debug
              URL: {diagnostics.Endpoint}
              Device ID: {diagnostics.DeviceId}
              HTTP Status: {(diagnostics.StatusCode.HasValue ? diagnostics.StatusCode.Value.ToString() : "-")}
              Error Type: {diagnostics.ErrorType ?? "-"}
              Request JSON: {diagnostics.RequestJson}
              Raw Response: {diagnostics.RawResponseBody ?? "-"}
              """;

        return new Border
        {
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Padding = 14,
            Content = new ScrollView
            {
                HeightRequest = 220,
                Content = new Label
                {
                    Text = text,
                    FontSize = 12,
                    TextColor = Color.FromArgb(SecondaryText),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };
    }

    private static bool IsTooManyPairingAttempts(string message, BootstrapDiagnostics? diagnostics)
    {
        return message.Contains("too many pairing attempts", StringComparison.OrdinalIgnoreCase) ||
            diagnostics?.RawResponseBody?.Contains("too many pairing attempts", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string BootstrapFailureMessage(string message, bool isRateLimited)
    {
        return isRateLimited
            ? "Mother POS blocked this Client for too many pairing attempts. Create a new Mother pairing code, or reset this Client device id during testing."
            : message;
    }

    private void SetConnectMessage(Border panel, string message)
    {
        if (panel.Content is not VerticalStackLayout stack)
        {
            return;
        }

        stack.Children.Insert(stack.Children.Count - 1, new Label
        {
            Text = message,
            TextColor = Color.FromArgb(AccentBlue),
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        });
    }

    private void ShowLogin(bool resetPin = true)
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        if (resetPin)
        {
            _pin = string.Empty;
            _loginStatusMessage = null;
            _loginMotherUnreachable = false;
            _adminBlockedVisible = false;
        }

        Root.Children.Clear();
        Root.BackgroundColor = Colors.White;
        _useLoginClockFormat = false;
        _timeLabel = null;
        _dateLabel = null;
        _statusLabel = null;

        EnsureSharedLoginWired();
        _sharedLoginViewModel!.SetRestaurantName(CurrentRestaurantName());

        // Same shared LoginView Mother uses (colors, keypad, clock, Clock In/Out).
        Root.Children.Add(new LoginView { ViewModel = _sharedLoginViewModel });

        if (_adminBlockedVisible)
        {
            Root.Children.Add(BuildAdminBlockedOverlay());
        }
    }

    private void EnsureSharedLoginWired()
    {
        if (_sharedLoginAuth is null)
        {
            _sharedLoginAuth = new ClientAuthenticationService(
                _authClient,
                () => !IsMotherUnavailable());
        }

        if (_sharedLoginViewModel is not null)
        {
            return;
        }

        _sharedLoginViewModel = new LoginViewModel(_sharedLoginAuth);
        _sharedLoginViewModel.LoginSucceeded += OnSharedLoginSucceeded;
        _sharedLoginViewModel.ClockInOutRequested += (_, _) => ShowClockTimeModal();
        _sharedLoginViewModel.MinimizeRequested += (_, _) => ClientWindowService.MinimizeMainWindow();
    }

    private async void OnSharedLoginSucceeded(object? sender, UserSession session)
    {
        try
        {
            var login = _sharedLoginAuth?.LastSuccessfulLogin;
            if (login is null)
            {
                login = new LoginSession(
                    session.User.UserId,
                    session.User.DisplayName,
                    session.User.Role,
                    session.User.Permissions.ToList(),
                    session.SessionId,
                    session.ExpiresAtUtc);
            }

            if (string.Equals(login.Role, "Cashier", StringComparison.OrdinalIgnoreCase) && IsMotherUnavailable())
            {
                ShowLogin();
                return;
            }

            await _cache.SaveLoginSessionAsync(login);
            ClientHostAccess.ApplyFromSession(login);
            _currentSession = login;
            _pin = string.Empty;
            _loginStatusMessage = null;
            _loginMotherUnreachable = false;
            _ = RefreshMotherOperationalCacheAsync();

            if (string.Equals(login.Role, "Cashier", StringComparison.OrdinalIgnoreCase))
            {
                ShowCashierDashboard();
                return;
            }

            ShowDashboard();
        }
        catch (Exception ex)
        {
            _loginStatusMessage = ex.Message;
            ShowLogin(false);
        }
    }

    private void ShowAdminBlockedLogin()
    {
        _pin = string.Empty;
        _loginStatusMessage = null;
        _loginMotherUnreachable = false;
        _adminBlockedVisible = true;
        ShowLogin(false);
    }

    private View BuildAdminBlockedOverlay()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromRgba(15, 23, 42, 0.46),
            InputTransparent = false,
            ZIndex = 20
        };

        void Dismiss()
        {
            _adminBlockedVisible = false;
            ShowLogin();
        }

        var dismissLayer = new BoxView { Color = Colors.Transparent };
        dismissLayer.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Dismiss) });
        overlay.Children.Add(dismissLayer);

        var okButton = new Button
        {
            Text = "Use another PIN",
            BackgroundColor = Color.FromArgb(PrimaryAction),
            TextColor = Colors.White,
            FontSize = 17,
            FontFamily = "OpenSansBold",
            CornerRadius = 14,
            HeightRequest = 52,
            Padding = 0
        };
        okButton.Clicked += (_, _) => Dismiss();

        overlay.Children.Add(new Border
        {
            WidthRequest = 460,
            MaximumWidthRequest = 520,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = new Thickness(32, 28),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.22f, Radius = 28, Offset = new Point(0, 10) },
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 64,
                        HeightRequest = 64,
                        BackgroundColor = Color.FromArgb("#EEF2FF"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 32 },
                        HorizontalOptions = LayoutOptions.Center,
                        Content = new Label
                        {
                            Text = "i",
                            FontSize = 30,
                            FontFamily = "OpenSansBold",
                            TextColor = Color.FromArgb(PrimaryAction),
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment = TextAlignment.Center
                        }
                    },
                    new Label
                    {
                        Text = "Admin can't sign in here",
                        FontSize = 24,
                        FontFamily = "OpenSansBold",
                        TextColor = Color.FromArgb(MainText),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = "Client POS is for restaurant staff only. Administrators must use the Mother POS terminal.",
                        FontSize = 15,
                        FontFamily = "OpenSansRegular",
                        TextColor = Color.FromArgb(MutedText),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    okButton
                }
            }
        });
        return overlay;
    }

    private View BuildPinDots()
    {
        var dots = new HorizontalStackLayout { Spacing = 22, HorizontalOptions = LayoutOptions.Center };
        for (var i = 0; i < 4; i++)
        {
            dots.Children.Add(new Border
            {
                WidthRequest = 18,
                HeightRequest = 18,
                StrokeShape = new RoundRectangle { CornerRadius = 9 },
                BackgroundColor = i < _pin.Length ? Color.FromArgb(PrimaryAction) : Color.FromArgb(BorderLight),
                StrokeThickness = 0
            });
        }

        return dots;
    }

    private View BuildKeypad()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(100),
                new ColumnDefinition(100),
                new ColumnDefinition(100)
            },
            RowDefinitions =
            {
                new RowDefinition(100),
                new RowDefinition(100),
                new RowDefinition(100),
                new RowDefinition(100)
            },
            ColumnSpacing = 18,
            RowSpacing = 18,
            HorizontalOptions = LayoutOptions.Center
        };

        var values = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "X" };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var button = RoundKey(value, (_, _) => OnPinKey(value));
            grid.Children.Add(button);
            SetRow(button, i / 3);
            SetColumn(button, i % 3);
        }

        return grid;
    }

    private Button RoundKey(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(PageBackground),
            TextColor = text == "X" ? Color.FromArgb(Danger) : Color.FromArgb(MainText),
            BorderColor = Color.FromArgb("#252525"),
            BorderWidth = 3,
            CornerRadius = 50,
            WidthRequest = 100,
            HeightRequest = 100,
            MinimumWidthRequest = 100,
            MinimumHeightRequest = 100,
            Padding = 0,
            FontFamily = "OpenSansRegular",
            FontAttributes = FontAttributes.None,
            FontSize = text == "X" ? 32 : text.Length == 1 ? 36 : 18,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.16f, Radius = 8, Offset = new Point(0, 4) }
        };
        button.Clicked += click;
        return button;
    }

    private async void OnPinKey(string value)
    {
        _loginStatusMessage = null;
        _loginMotherUnreachable = false;
        if (value == "Clear")
        {
            _pin = string.Empty;
        }
        else if (value == "X")
        {
            _pin = _pin.Length > 0 ? _pin[..^1] : string.Empty;
        }
        else if (_pin.Length < 4)
        {
            _pin += value;
        }

        if (_pin.Length == 4)
        {
            await LoginAsync(new LoginRequest("PIN", _pin));
            return;
        }

        ShowLogin(false);
    }

    private async Task LoginAsync(LoginRequest request)
    {
        try
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = "Checking PIN...";
                _statusLabel.TextColor = Color.FromArgb(SecondaryText);
                _statusLabel.IsVisible = true;
            }

            var session = await _authClient.LoginAsync(request);
            if (IsMotherOnlyRole(session.Role))
            {
                ShowAdminBlockedLogin();
                return;
            }

            if (session.Role == "Staff")
            {
                _pin = string.Empty;
                _loginStatusMessage = "Staff PIN is for Clock In/Out only.";
                _loginMotherUnreachable = false;
                ShowLogin(false);
                return;
            }

            if (string.Equals(session.Role, "Cashier", StringComparison.OrdinalIgnoreCase) && IsMotherUnavailable())
            {
                _pin = string.Empty;
                _loginStatusMessage = "Cashier access requires a live connection to Mother POS.";
                _loginMotherUnreachable = true;
                ShowLogin(false);
                return;
            }

            await _cache.SaveLoginSessionAsync(session);
            ClientHostAccess.ApplyFromSession(session);
            _currentSession = session;
            _pin = string.Empty;
            _ = RefreshMotherOperationalCacheAsync();
            if (string.Equals(session.Role, "Cashier", StringComparison.OrdinalIgnoreCase))
            {
                ShowCashierDashboard();
                return;
            }
            ShowDashboard();
        }
        catch (LoginException ex)
        {
            _pin = string.Empty;
            if (ex.IsPairingInvalid)
            {
                _loginStatusMessage = null;
                _loginMotherUnreachable = false;
                ShowConnect(
                    "This terminal is no longer paired with Mother POS. Enter a new pairing code.",
                    reconnectMode: true,
                    requirePairingCode: true);
                return;
            }

            if (string.Equals(ex.ErrorCode, "admin_mother_only", StringComparison.OrdinalIgnoreCase))
            {
                await _cache.ClearLoginSessionAsync();
                ShowAdminBlockedLogin();
                return;
            }

            _loginStatusMessage = string.IsNullOrWhiteSpace(ex.Message) ? "Wrong PIN" : ex.Message;
            _loginMotherUnreachable = ex.IsMotherUnreachable;
            ShowLogin(false);
        }
        catch (Exception ex)
        {
            _pin = string.Empty;
            _loginStatusMessage = string.IsNullOrWhiteSpace(ex.Message) ? "Could not reach Mother POS." : ex.Message;
            _loginMotherUnreachable = true;
            ShowLogin(false);
        }
    }

    private Button ChangeMotherLinkButton()
    {
        var button = new Button
        {
            Text = "Can't connect / Change Mother",
            BackgroundColor = Colors.Transparent,
            BorderColor = Colors.Transparent,
            BorderWidth = 0,
            TextColor = Color.FromArgb(AccentBlue),
            FontSize = 15,
            FontFamily = "OpenSansSemibold",
            Padding = 0,
            HeightRequest = 36
        };
        button.Clicked += (_, _) => OpenMotherReconnectFromLogin();
        return button;
    }

    private void OpenMotherReconnectFromLogin()
    {
        _currentSession = null;
        _pin = string.Empty;
        _loginStatusMessage = null;
        _loginMotherUnreachable = false;
        if (_hasPairedMother)
        {
            var address = SavedMotherAddress();
            ShowConnect(
                string.IsNullOrWhiteSpace(address)
                    ? "Update the Mother IP or pairing code if this terminal cannot connect."
                    : $"Cannot reach Mother POS? Update the details for {address} or enter a new pairing code.",
                reconnectMode: true);
            return;
        }

        ShowConnect();
    }

    private async void ShowClockTimeModal()
    {
        await Navigation.PushModalAsync(new ClockTimeModal(), false);
    }

    private void ShowCashierDashboard()
    {
        if (_currentSession is null || !string.Equals(_currentSession.Role, "Cashier", StringComparison.OrdinalIgnoreCase))
        {
            ShowLogin();
            return;
        }

        // Financial values and final actions remain Mother-authoritative. The
        // Client deliberately renders no cached totals and provides no local
        // order, refund, discount, or void workflow for this role.
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        var online = !IsMotherUnavailable();
        var status = online ? "Connected to Mother POS" : "Mother POS unavailable — read-only access disabled";
        var content = new VerticalStackLayout
        {
            Padding = new Thickness(32),
            Spacing = 18,
            HorizontalOptions = LayoutOptions.Center,
            // Account for the page padding as well as four cards. Without this
            // the right-most Variance card can extend beyond narrow Client POS
            // windows and clip "Not counted".
            MaximumWidthRequest = 820,
            Children =
            {
                new Label { Text = "Cashier Dashboard", FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText) },
                new Label { Text = $"{_currentSession.UserName} · {LastTerminalName()} · {status}", FontSize = 15, TextColor = Color.FromArgb(SecondaryText) },
                BuildCashierSummaryGrid(online)
            }
        };

        var reports = new Button { Text = "Z Report Preview", IsEnabled = online, HeightRequest = 64, BackgroundColor = Color.FromArgb("#1D4ED8"), TextColor = Colors.White };
        reports.Clicked += async (_, _) => await ShowCashierZPreviewAsync(reports);
        var drawerAllowed = ClientHostAccess.Features.Contains(PosFeatureKeys.Payments);
        var drawer = new Button { Text = "Open Cash Drawer", IsVisible = drawerAllowed, IsEnabled = online && drawerAllowed, HeightRequest = 64, BackgroundColor = Color.FromArgb("#0F766E"), TextColor = Colors.White };
        drawer.Clicked += async (_, _) => await OpenCashierDrawerAsync(drawer);
        var print = new Button { Text = "Print Z Report", IsEnabled = online, HeightRequest = 58, BackgroundColor = Color.FromArgb("#7C3AED"), TextColor = Colors.White };
        print.Clicked += async (_, _) => await PrintCashierZReportAsync(print);
        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14 };
        actions.Add(drawer); actions.Add(reports, 1); actions.Add(print, 2);
        content.Children.Add(new Label { Text = "Quick actions", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText) });
        content.Children.Add(actions);
        // Use the same shared header/sidebar shell as Mother POS. The shell
        // owns the identity and connection bar; the Cashier content below is
        // intentionally limited to read-only summaries and Z-report actions.
        if (content.Children.Count >= 2)
        {
            content.Children.RemoveAt(0);
            content.Children.RemoveAt(0);
        }
        content.Children.Insert(0, new Label { Text = $"Business date: {DateTime.Today:dddd, dd MMMM yyyy}", FontSize = 15, TextColor = Color.FromArgb(SecondaryText) });
        // Keep the status value for stale-data safety checks, but the shared
        // top bar already presents connection state so it need not be repeated.
        _cashierDataStatusLabel = new Label { Text = "Refreshing…", IsVisible = false };
        var frame = SharedAppFrame("Dashboard", new ScrollView { Content = content }, "dashboard");
        frame.SetSidebarVisible(false);
        frame.MenuItems = new[]
        {
            new ApplicationNavigationItem("dashboard", "Dashboard", "dashboard.png"),
            new ApplicationNavigationItem("cashdrawer", "Cash Drawer", "cashdrawer.png")
        };
        Root.Children.Add(frame);
        _ = RefreshCashierDashboardAsync();
        if (!_cashierRefreshTimer.IsRunning) _cashierRefreshTimer.Start();
    }

    private Grid BuildCashierSummaryGrid(bool online)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 12, RowSpacing = 12 };
        var cards = new[] { ("TOTAL ORDERS", "—"), ("TOTAL SALES", "£0.00"), ("CASH TOTAL", "£0.00"), ("CARD TOTAL", "£0.00"), ("VOIDS", "0"), ("DISCOUNTS", "£0.00"), ("EXPECTED CASH", "£0.00"), ("VARIANCE", online ? "Not counted" : "Offline") };
        for (var i = 0; i < cards.Length; i++)
        {
            var (title, value) = cards[i];
            var valueLabel = new Label { Text = value, FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText) };
            _cashierSummaryLabels[i] = valueLabel;
            var card = new Border { Stroke = Color.FromArgb("#DCE5F2"), StrokeThickness = 1, BackgroundColor = Colors.White, Padding = 18, Content = new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = title, FontSize = 12, TextColor = Color.FromArgb(SecondaryText) }, valueLabel } } };
            Grid.SetColumn(card, i % 4); Grid.SetRow(card, i / 4); grid.Children.Add(card);
        }
        return grid;
    }

    private async Task RefreshCashierDashboardAsync()
    {
        if (_cashierClient is null || !string.Equals(_currentSession?.Role, "Cashier", StringComparison.OrdinalIgnoreCase)) return;
        if (_cashierDataStatusLabel != null) _cashierDataStatusLabel.Text = "Refreshing…";
        var result = await _cashierClient.GetDashboardAsync();
        var summary = result.Summary;
        if (summary != null)
        {
            var values = new[] { summary.TotalOrders.ToString(), $"£{summary.TotalSales:N2}", $"£{summary.CashTotal:N2}", $"£{summary.CardTotal:N2}", summary.VoidCount.ToString(), $"£{summary.DiscountTotal:N2}", $"£{summary.ExpectedCash:N2}", summary.Variance is { } variance ? $"£{variance:N2}" : "Not counted" };
            for (var i = 0; i < values.Length; i++) if (_cashierSummaryLabels[i] != null) _cashierSummaryLabels[i].Text = values[i];
        }
        if (_cashierDataStatusLabel != null)
            _cashierDataStatusLabel.Text = result.Success ? $"Connected — live data · Last updated {summary?.GeneratedUtc.LocalDateTime:HH:mm}" : result.IsStale ? "Connection lost — data may be stale" : result.Message;
    }

    private bool CanRunCashierLiveAction() => _cashierClient is not null && !IsMotherUnavailable() && _cashierDataStatusLabel?.Text?.Contains("stale", StringComparison.OrdinalIgnoreCase) != true;

    private async Task ShowCashierZPreviewAsync(Button button)
    {
        if (!CanRunCashierLiveAction()) { await DisplayAlertAsync("Mother connection", "Z Report preview requires live Mother POS data.", "OK"); return; }
        button.IsEnabled = false;
        try
        {
            var result = await _cashierClient!.GetZReportPreviewAsync();
            if (!result.Success || result.Preview is null) { await DisplayAlertAsync("Z Report Preview", result.Message, "OK"); return; }
            var printRequested = await new ZReportPreviewDialogPage(result.Preview, CanRunCashierLiveAction()).ShowAsync(Navigation);
            await RefreshCashierDashboardAsync();
            if (printRequested) await PrintCashierZReportAsync(button);
        }
        finally { button.IsEnabled = CanRunCashierLiveAction(); }
    }

    private async Task PrintCashierZReportAsync(Button button)
    {
        if (!CanRunCashierLiveAction()) { await DisplayAlertAsync("Mother connection", "Z Report printing requires a live Mother POS connection.", "OK"); return; }
        if (!await new PrintZReportConfirmDialogPage().ShowAsync(Navigation)) return;
        button.IsEnabled = false;
        try { var result = await _cashierClient!.PrintZReportAsync(); await DisplayAlertAsync(result.Success ? "Z Report Printed" : "Print Failed", result.Message, "OK"); await RefreshCashierDashboardAsync(); }
        finally { button.IsEnabled = CanRunCashierLiveAction(); }
    }

    private async Task OpenCashierDrawerAsync(Button button)
    {
        if (!CanRunCashierLiveAction()) { await DisplayAlertAsync("Mother connection", "Cash drawer opening requires a live Mother POS connection.", "OK"); return; }
        var reason = await new CashDrawerReasonDialogPage().ShowAsync(Navigation);
        if (string.IsNullOrWhiteSpace(reason)) return;
        CashDrawerFormResult? form = null;
        if (reason is "Shopping" or "Delivery" or "Cash Count" or "Other")
        {
            form = reason switch
            {
                "Shopping" => await new CashDrawerFormDialogPage("Shopping", "Record cash taken from the till before opening the drawer.", "Take & Open", "#0F8278", "Shopping item or purpose", "Amount out").ShowAsync(Navigation),
                "Delivery" => await new CashDrawerFormDialogPage("Delivery payout", "Record cash paid out for delivery before opening the drawer.", "Pay & Open", "#0F8278", null, "Amount out").ShowAsync(Navigation),
                "Cash Count" => await new CashDrawerFormDialogPage("Cash count", "Enter the cash counted in the drawer.", "Record & Open", "#0F8278", null, "Counted cash").ShowAsync(Navigation),
                _ => await new CashDrawerFormDialogPage("Other till expense", "Enter the reason for opening the cash drawer.", "Continue", "#2563EB", "Reason", "Amount out").ShowAsync(Navigation)
            };
            if (form is null) return;
        }
        form ??= new CashDrawerFormResult(null, null);
        if (!await new CashDrawerConfirmDialogPage(reason).ShowAsync(Navigation)) return;
        button.IsEnabled = false;
        try { var result = await _cashierClient!.OpenCashDrawerAsync(reason, form.Amount, form.Details); await DisplayAlertAsync(result.Success ? "Cash Drawer" : "Cash Drawer Failed", result.Message, "OK"); await RefreshCashierDashboardAsync(); }
        finally { button.IsEnabled = CanRunCashierLiveAction(); }
    }

    private void ShowDashboard()
    {
        if (string.Equals(_currentSession?.Role, "Cashier", StringComparison.OrdinalIgnoreCase))
        {
            ShowCashierDashboard();
            return;
        }

        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        _posSelectedMenu = "Dashboard";
        _isViewingOrderScreen = false;
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        if (IsMotherOnlyRole(_currentSession?.Role))
        {
            _currentSession = null;
            _ = _cache.ClearLoginSessionAsync();
            ShowAdminBlockedLogin();
            return;
        }

        if (_currentSession?.Role == "Staff")
        {
            _currentSession = null;
            ShowLogin();
            _statusLabel!.Text = "Staff PIN is for Clock In/Out only.";
            _statusLabel.TextColor = Color.FromArgb(Danger);
            _statusLabel.IsVisible = true;
            return;
        }

        Root.Children.Add(SharedAppFrame(DashboardTitle(), BuildSharedDashboard(), "dashboard"));
    }

    private View BuildSharedDashboard()
    {
        var session = _currentSession;
        var capabilities = ClientCapabilityResolver.ForRole(session?.Role, session?.Permissions);
        var routes = ClientHostAccess.DashboardRoutesForRole(session?.Role);
        var viewModel = new DashboardViewModel
        {
            Title = DashboardTitle(),
            Subtitle = string.Empty,
            IsOffline = IsMotherUnavailable()
        };
        viewModel.TileSelected += (_, tile) => NavigateFromSharedDashboard(tile.Route);
        _sharedDashboardViewModel = viewModel;
        ApplySharedDashboardState(viewModel, capabilities, routes);
        _ = RefreshSharedDashboardStateAsync(viewModel, capabilities, routes);
        return new DashboardView { ViewModel = viewModel };
    }

    private async Task RefreshSharedDashboardStateAsync(
        DashboardViewModel viewModel,
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string> routes)
    {
        try
        {
            var status = await _cache.GetStatusAsync();
            if (!ReferenceEquals(viewModel, _sharedDashboardViewModel)) return;
            _cacheStatus = status;
            ApplySharedDashboardState(viewModel, capabilities, routes);
        }
        catch
        {
            if (!ReferenceEquals(viewModel, _sharedDashboardViewModel)) return;
            viewModel.IsOffline = true;
            ApplySharedDashboardState(viewModel, capabilities, routes);
        }
    }

    private void ApplySharedDashboardState(
        DashboardViewModel viewModel,
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string> routes)
    {
        viewModel.IsOffline = IsMotherUnavailable();
        viewModel.ApplyCapabilities(capabilities, ClientHostAccess.FeaturesForRole(_currentSession?.Role), routes);
    }

    private bool IsMotherUnavailable() => _connectionStatus.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                                         _connectionStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase);

    private void NavigateFromSharedDashboard(string route)
    {
        switch (route)
        {
            case "restaurant": OnClientSidebarMenuSelected(this, "Restaurant"); break;
            case "collection": OnClientSidebarMenuSelected(this, "Collection"); break;
            case "delivery": OnClientSidebarMenuSelected(this, "Delivery"); break;
            case "reservation": OnClientSidebarMenuSelected(this, "Reservation"); break;
            case "liveorder": OnClientSidebarMenuSelected(this, "Live Order"); break;
            case "openorders": OnClientSidebarMenuSelected(this, "Live Order"); break;
            case "customers": ShowSharedCustomerFlow(); break;
        }
    }

    private View UserDashboard()
    {
        var tiles = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            RowSpacing = IsCompactLayout ? 42 : 80,
            ColumnSpacing = IsCompactLayout ? 38 : 150,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center
        };

        AddUserDashboardTile(tiles, "Restaurant", "restaurant.png", 0, 0, ShowRestaurantLayout);
        AddUserDashboardTile(tiles, "Delivery", "delivery.png", 0, 1, () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()));
        AddUserDashboardTile(tiles, "Collection", "collection.png", 0, 2, () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()));
        AddUserDashboardTile(tiles, "Live Order", "liveorder.png", 1, 1, ShowLiveOrders);

        var main = new ScrollView
        {
            BackgroundColor = Color.FromArgb(PageBackground),
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Padding = 30,
                MaximumWidthRequest = 900,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { tiles }
            }
        };

        return SharedAppFrame("Dashboard", main, "dashboard");
    }

    private View ManagerDashboard()
    {
        var content = ManagerDashboardContent();
        return SharedAppFrame("Dashboard", content, "dashboard");
    }

    private View ManagerDashboardContent()
    {
        var content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 30,
                Spacing = 42,
                MaximumWidthRequest = 900,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    ManagerMainActions()
                }
            }
        };
        return content;
    }

    private Grid ManagerMainActions()
    {
        var compact = IsCompactLayout;
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = compact ? 38 : 150,
            RowSpacing = compact ? 42 : 80,
            HorizontalOptions = LayoutOptions.Center
        };

        if (compact)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AddManagerActionTile(grid, "Restaurant", "restaurant.png", 0, 0, ShowRestaurantLayout);
            AddManagerActionTile(grid, "Delivery", "delivery.png", 0, 1, () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()));
            AddManagerActionTile(grid, "Collection", "collection.png", 1, 0, () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()));
            AddManagerActionTile(grid, "Live Order", "liveorder.png", 1, 1, ShowLiveOrders);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AddManagerActionTile(grid, "Restaurant", "restaurant.png", 0, 0, ShowRestaurantLayout);
            AddManagerActionTile(grid, "Delivery", "delivery.png", 0, 1, () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()));
            AddManagerActionTile(grid, "Collection", "collection.png", 0, 2, () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddManagerActionTile(grid, "Live Order", "liveorder.png", 1, 1, ShowLiveOrders);
        }
        return grid;
    }

    private void AddManagerActionTile(Grid grid, string label, string imageSource, int row, int column, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();

        var stack = new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Image { Source = imageSource, WidthRequest = ResponsiveManagerIconSize, HeightRequest = ResponsiveManagerIconSize, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = label, FontSize = 18, FontFamily = "InterBold", TextColor = Color.FromArgb(DashboardLabelBlue), HorizontalTextAlignment = TextAlignment.Center }
            }
        };
        stack.GestureRecognizers.Add(tap);
        grid.Children.Add(stack);
        SetRow(stack, row);
        SetColumn(stack, column);
    }

    private Grid ManagerToolGrid()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 28,
            RowSpacing = 24
        };

        AddManagerTool(grid, "Live Order", "liveorder.png", 0, 0, ShowLiveOrders);
        AddManagerTool(grid, "Gift Cards", "giftcards.png", 0, 1, () => OpenManagerToolPage(new GiftCardPage()));
        AddManagerTool(grid, "Loyalty Points", "loyalty.png", 0, 2, () => OpenManagerToolPage(new LoyaltyPage()));
        AddManagerTool(grid, "Reservation", "reservation.png", 1, 0, () => OpenManagerToolPage(new ReservationPage()));
        AddManagerTool(grid, "Order History", "orderhistory.png", 1, 1, () => OpenManagerToolPage(new OrderHistoryPage()));
        AddManagerTool(grid, "Cash Drawer", "giftcard.png", 1, 2, () => OpenManagerToolPage(new CashDrawerPage()));
        return grid;
    }

    private async void OpenManagerToolPage(ContentPage page)
    {
        // Mother Collection/Delivery temporary routes slide in from the side.
        if (page is Pages.Orders.CollectionOrderPage or Pages.Orders.DeliveryOrderPage)
        {
            await ClientSideNavigation.PushFromSideAsync(Navigation, page);
            return;
        }

        await Navigation.PushAsync(page, false);
    }

    private void AddManagerTool(Grid grid, string label, string imageSource, int row, int column, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();

        var card = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb(LightSurface),
            Padding = 22,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(54),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 18,
                Children =
                {
                    new Image { Source = imageSource, WidthRequest = 48, HeightRequest = 48, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = label, FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(SidebarText), VerticalTextAlignment = TextAlignment.Center }
                }
            }
        };
        SetColumn(((Grid)card.Content).Children[1], 1);
        card.GestureRecognizers.Add(tap);
        grid.Children.Add(card);
        SetRow(card, row);
        SetColumn(card, column);
    }

    private View PosDashboardShell(View content)
    {
        return content;
    }

    private View PosTopHeader(bool showWelcome)
    {
        var topBar = new ClientTopBar
        {
            RestaurantName = CurrentRestaurantName(),
            ConnectionStatus = _connectionStatus,
            HorizontalOptions = LayoutOptions.Fill
        };
        topBar.MenuClicked += (_, _) => TogglePosSidebar();
        topBar.LogoutClicked += (_, _) => Logout();
        return topBar;
    }

    private View PosHeaderBrand()
    {
        return new HorizontalStackLayout
        {
            Spacing = 26,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                PosMenuButton(),
                new VerticalStackLayout
                {
                    Spacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "Welcome to", FontSize = 14, FontFamily = "InterMedium", TextColor = Color.FromArgb(MutedText) },
                        new Label { Text = CurrentRestaurantName(), FontSize = 24, FontFamily = "InterBold", TextColor = Color.FromArgb(MainText) }
                    }
                },
            }
        };
    }

    private ImageButton PosMenuButton()
    {
        var menuButton = new ImageButton
        {
            Source = "mian.png",
            WidthRequest = HeaderMenuIconSize,
            HeightRequest = HeaderMenuIconSize,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        menuButton.Clicked += (_, _) => TogglePosSidebar();
        return menuButton;
    }

    private async void TogglePosSidebar()
    {
        if (_posSidebarOpen)
        {
            await ClosePosSidebarAsync();
            return;
        }

        await OpenPosSidebarAsync();
    }

    private async Task OpenPosSidebarAsync()
    {
        if (_posSidebarOpen)
        {
            return;
        }

        _posSidebarOpen = true;

        var overlay = new Grid
        {
            BackgroundColor = Colors.Transparent,
            InputTransparent = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var closeLayer = new BoxView
        {
            BackgroundColor = Color.FromRgba(255, 255, 255, 0.01),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(SidebarWidth, 0, 0, 0)
        };
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (_, _) => ClosePosSidebar();
        closeLayer.GestureRecognizers.Add(closeTap);

        var sidebar = PosSidebar();
        sidebar.TranslationX = -SidebarWidth;
        _posSidebarView = sidebar;
        _posSidebarOverlay = overlay;

        overlay.Children.Add(closeLayer);
        overlay.Children.Add(sidebar);
        Root.Children.Add(overlay);

        await sidebar.TranslateTo(0, 0, 260, Easing.CubicOut);
    }

    private async void ClosePosSidebar()
    {
        await ClosePosSidebarAsync();
    }

    private async Task ClosePosSidebarAsync()
    {
        if (!_posSidebarOpen)
        {
            return;
        }

        await AnimatePosSidebarClosedAsync();
        _posSidebarOpen = false;
        if (_posSidebarOverlay != null)
        {
            Root.Children.Remove(_posSidebarOverlay);
            _posSidebarOverlay = null;
        }
        _posSidebarView = null;
    }

    private async Task AnimatePosSidebarClosedAsync()
    {
        if (_posSidebarView == null)
        {
            return;
        }

        await _posSidebarView.TranslateTo(-SidebarWidth, 0, 200, Easing.CubicIn);
    }

    private void RefreshCurrentPosPage()
    {
        if (_currentSession is null)
        {
            return;
        }

        switch (_posSelectedMenu)
        {
            case "Restaurant":
                ShowRestaurantLayout();
                break;
            case "Live Order":
                // Keep an open order on screen; list refresh must not wipe mid-edit.
                if (_isViewingOrderScreen && _currentOrder is not null)
                {
                    break;
                }

                ShowLiveOrders(_liveOrderFilter);
                break;
            case "Collection":
            case "Delivery":
            case "Cash Drawer":
            case "Gift Cards":
            case "Loyalty Points":
            case "Reservation":
            case "Order History":
                // Pushed tool / customer-form pages must stay put on Mother sync.
                // Re-opening them restarts the side-slide and clears in-progress input.
                break;
            default:
                ShowDashboard();
                break;
        }
    }

    private void RunFromPosSidebar(Action action)
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        _posSidebarOpen = false;
        action();
    }

    private async void RunFromPosSidebar(string selectedMenu, Action action)
    {
        _posSelectedMenu = selectedMenu;
        await AnimatePosSidebarClosedAsync();
        _posSidebarOpen = false;
        if (_posSidebarOverlay != null)
        {
            Root.Children.Remove(_posSidebarOverlay);
            _posSidebarOverlay = null;
        }
        _posSidebarView = null;
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        action();
    }

    private View PosSidebar()
    {
        var sidebar = new ClientSidebar
            {
                Role = _currentSession?.Role ?? "User",
                SelectedMenu = _posSelectedMenu,
                ShowFooter = _currentSession?.Role == "Manager",
                HorizontalOptions = LayoutOptions.Start,
            };
        sidebar.MenuItemSelected += OnClientSidebarMenuSelected;
        sidebar.UpdateAllClicked += OnClientSidebarUpdateAllClicked;
        return sidebar;
    }

    private void OnClientSidebarMenuSelected(object? sender, string selectedMenu)
    {
        if (!ClientHostAccess.CanOpenMenu(selectedMenu))
        {
            return;
        }

        switch (selectedMenu)
        {
            case "Dashboard":
                RunFromPosSidebar(selectedMenu, ShowDashboard);
                break;
            case "Cash Drawer":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new CashDrawerPage()));
                break;
            case "Live Order":
                RunFromPosSidebar(selectedMenu, ShowLiveOrders);
                break;
            case "Restaurant":
                RunFromPosSidebar(selectedMenu, ShowRestaurantLayout);
                break;
            case "Collection":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()));
                break;
            case "Delivery":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()));
                break;
            case "Customers":
                RunFromPosSidebar(selectedMenu, ShowSharedCustomerFlow);
                break;
            case "Gift Cards":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new GiftCardPage()));
                break;
            case "Loyalty Points":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new LoyaltyPage()));
                break;
            case "Reservation":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new ReservationPage()));
                break;
            case "Recent Customers":
                RunFromPosSidebar(selectedMenu, ShowSharedCustomerFlow);
                break;
            case "Order History":
                RunFromPosSidebar(selectedMenu, () => OpenManagerToolPage(new OrderHistoryPage()));
                break;
        }
    }

    private async void OnClientSidebarUpdateAllClicked(object? sender, EventArgs e)
    {
        var frame = _activeApplicationFrame;
        if (frame != null)
        {
            frame.LoadingMessage = "Refreshing terminal data…";
            frame.IsLoading = true;
        }
        try
        {
            await AnimatePosSidebarClosedAsync();
            _posSidebarOpen = false;
            _cacheStatus = await _cache.GetStatusAsync();
            ShowToast("Connect to Mother POS to sync data.");
            if (frame != null) frame.IsLoading = false;
            ShowDashboard();
        }
        catch (Exception ex)
        {
            if (frame != null)
            {
                frame.IsLoading = false;
                frame.ErrorTitle = "Refresh failed";
                frame.ErrorMessage = ex.Message;
            }
        }
    }

    private View PosSidebarBrand()
    {
        return new VerticalStackLayout
        {
            Padding = new Thickness(20, 32, 20, 28),
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Image { Source = "companylogo.png", WidthRequest = SidebarLogoSize, HeightRequest = SidebarLogoSize, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = "Restaurant Management", FontSize = 16, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb("#718096"), HorizontalTextAlignment = TextAlignment.Center }
            }
        };
    }

    private View PosSidebarMenu()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(16, 12),
            VerticalOptions = LayoutOptions.Start
        };

        foreach (var item in PosSidebarItemsForRole())
        {
            stack.Children.Add(PosSidebarItem(item.Label, item.ImageSource, item.Action));
        }

        var scroll = new ScrollView
        {
            Margin = new Thickness(0, 8, 0, 0),
            BackgroundColor = Color.FromArgb(PageBackground),
            Content = stack
        };
        SetRow(scroll, 1);
        return scroll;
    }

    private View PosSidebarItem(string text, string imageSource, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();

        var row = new HorizontalStackLayout
        {
            Spacing = 20,
            Padding = new Thickness(16, 14),
            BackgroundColor = _posSelectedMenu == text ? Color.FromArgb(SidebarSelected) : Colors.Transparent,
            Children =
            {
                new Image { Source = imageSource, WidthRequest = SidebarIconSize, HeightRequest = SidebarIconSize, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontSize = 16, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(SidebarText), VerticalTextAlignment = TextAlignment.Center }
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private IEnumerable<PosSidebarMenuItem> PosSidebarItemsForRole()
    {
        var role = _currentSession?.Role ?? "User";
        var userItems = new[]
        {
            new PosSidebarMenuItem("Dashboard", "dashboard.png", () => RunFromPosSidebar("Dashboard", ShowDashboard)),
            new PosSidebarMenuItem("Cash Drawer", "giftcard.png", () => RunFromPosSidebar("Cash Drawer", () => OpenManagerToolPage(new CashDrawerPage()))),
            new PosSidebarMenuItem("Live Order", "liveorder.png", () => RunFromPosSidebar("Live Order", ShowLiveOrders)),
            new PosSidebarMenuItem("Restaurant", "restaurant.png", () => RunFromPosSidebar("Restaurant", ShowRestaurantLayout)),
            new PosSidebarMenuItem("Collection", "collection.png", () => RunFromPosSidebar("Collection", () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()))),
            new PosSidebarMenuItem("Delivery", "delivery.png", () => RunFromPosSidebar("Delivery", () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()))),
            new PosSidebarMenuItem("Reservation", "reservation.png", () => RunFromPosSidebar("Reservation", () => OpenManagerToolPage(new ReservationPage())))
        };

        if (role == "User")
        {
            return userItems.Where(item => ClientHostAccess.CanOpenMenu(item.Label));
        }

        if (role == "Manager")
        {
            return new[]
            {
                new PosSidebarMenuItem("Dashboard", "dashboard.png", () => RunFromPosSidebar("Dashboard", ShowDashboard)),
                new PosSidebarMenuItem("Cash Drawer", "giftcard.png", () => RunFromPosSidebar("Cash Drawer", () => OpenManagerToolPage(new CashDrawerPage()))),
                new PosSidebarMenuItem("Live Order", "liveorder.png", () => RunFromPosSidebar("Live Order", ShowLiveOrders)),
                new PosSidebarMenuItem("Restaurant", "restaurant.png", () => RunFromPosSidebar("Restaurant", ShowRestaurantLayout)),
                new PosSidebarMenuItem("Collection", "collection.png", () => RunFromPosSidebar("Collection", () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()))),
                new PosSidebarMenuItem("Delivery", "delivery.png", () => RunFromPosSidebar("Delivery", () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()))),
                new PosSidebarMenuItem("Gift Cards", "giftcards.png", () => RunFromPosSidebar("Gift Cards", () => OpenManagerToolPage(new GiftCardPage()))),
                new PosSidebarMenuItem("Loyalty Points", "loyalty.png", () => RunFromPosSidebar("Loyalty Points", () => OpenManagerToolPage(new LoyaltyPage()))),
                new PosSidebarMenuItem("Reservation", "reservation.png", () => RunFromPosSidebar("Reservation", () => OpenManagerToolPage(new ReservationPage()))),
                new PosSidebarMenuItem("Order History", "orderhistory.png", () => RunFromPosSidebar("Order History", () => OpenManagerToolPage(new OrderHistoryPage())))
            }.Where(item => ClientHostAccess.CanOpenMenu(item.Label));
        }

        return userItems.Where(item => ClientHostAccess.CanOpenMenu(item.Label));
    }

    private sealed record PosSidebarMenuItem(string Label, string ImageSource, Action Action);

    private void AddUserDashboardTile(Grid grid, string label, string imageSource, int row, int column, Action action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();

        var stack = new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image { Source = imageSource, WidthRequest = ResponsiveDashboardIconSize, HeightRequest = ResponsiveDashboardIconSize, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = label, FontSize = 18, FontFamily = "InterBold", TextColor = Color.FromArgb(DashboardLabelBlue), HorizontalTextAlignment = TextAlignment.Center }
            }
        };
        stack.GestureRecognizers.Add(tap);

        grid.Children.Add(stack);
        SetRow(stack, row);
        SetColumn(stack, column);
    }

    private View DashboardContent()
    {
        var wrapper = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(70, 28, 70, 40)
        };

        wrapper.Children.Add(RoleBanner());

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowSpacing = 42,
            ColumnSpacing = 46,
            Padding = new Thickness(0, 18, 0, 0)
        };

        var tiles = DashboardTilesForRole().ToList();
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            AddTile(grid, tile.Label, tile.Icon, i / 3, i % 3, RequirePermission(tile.Permission, tile.Action));
        }

        wrapper.Children.Add(grid);
        return wrapper;
    }

    private IEnumerable<DashboardTile> DashboardTilesForRole()
    {
        var role = _currentSession?.Role ?? "Staff";

        var staffTiles = new[]
        {
            new DashboardTile("Restaurant", "restaurant.png", "client.restaurant", ShowRestaurantLayout),
            new DashboardTile("Collection", "collection.png", "client.collection", () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage())),
            new DashboardTile("Delivery", "delivery.png", "client.delivery", () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage())),
            new DashboardTile("Live Order", "liveorder.png", "client.live_orders", ShowLiveOrders),
            new DashboardTile("Logout", "outred.png", "client.dashboard", Logout)
        };

        if (role == "Staff")
        {
            return staffTiles.Where(tile => tile.Label == "Logout" || ClientHostAccess.CanOpenMenu(tile.Label));
        }

        var managerTiles = new[]
        {
            new DashboardTile("Discounts", "foodmenu.png", "client.discount", () => ShowToast("Discount tools are available for Manager and above.")),
            new DashboardTile("Refunds/Voids", "settings.png", "client.refunds_voids", () => ShowToast("Refund and void requests must be confirmed by Mother POS.")),
            new DashboardTile("Reservations", "reservation.png", "client.reservations", () => OpenManagerToolPage(new ReservationPage())),
            new DashboardTile("Gift Cards", "giftcards.png", "client.gift_cards", () => OpenManagerToolPage(new GiftCardPage())),
            new DashboardTile("Loyalty", "loyalty.png", "client.loyalty", () => OpenManagerToolPage(new LoyaltyPage()))
        };

        if (role == "Manager")
        {
            return staffTiles.Concat(managerTiles).Where(tile => tile.Label == "Logout" || ClientHostAccess.CanOpenMenu(tile.Label));
        }

        return staffTiles.Where(tile => tile.Label == "Logout" || ClientHostAccess.CanOpenMenu(tile.Label));
    }

    private View RoleBanner()
    {
        var session = _currentSession;
        var title = session == null
            ? "Not logged in"
            : $"{session.UserName} - {session.Role}";
        var detail = session?.Role == "Super Admin"
            ? "Client access: business/admin features only. Deep system setup must be done on Mother POS."
            : "Dashboard options follow the current role permissions from Mother POS.";

        return new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 14,
            BackgroundColor = Color.FromArgb(session?.Role == "Super Admin" ? "#FEF3C7" : "#F8FAFC"),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText) },
                            new Label { Text = detail, FontSize = 13, TextColor = Color.FromArgb(MutedText) }
                        }
                    },
                    StatusPill(session?.Role ?? "No Session", session?.Role == "Super Admin" ? "#F59E0B" : "#2563EB")
                }
            }
        };
    }

    private EventHandler RequirePermission(string permission, Action action)
    {
        return (_, _) =>
        {
            if (_terminalDisabled)
            {
                ShowTerminalDisabled();
                return;
            }

            if (_currentSession == null || !_currentSession.HasPermission(permission))
            {
                ShowToast("This user role does not have permission for this client feature.");
                return;
            }

            action();
        };
    }

    private static bool IsMotherOnlyRole(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

    private void Logout()
    {
        _currentSession = null;
        _ = _cache.ClearLoginSessionAsync();
        ClientHostAccess.Clear();
        _pin = string.Empty;
        _loginStatusMessage = null;
        _loginMotherUnreachable = false;
        _posSidebarOpen = false;
        ShowLogin();
    }

    private sealed record DashboardTile(string Label, string Icon, string Permission, Action Action);

    private void AddTile(Grid grid, string label, string icon, int row, int column, EventHandler click)
    {
        var button = new Button
        {
            Text = label,
            FontSize = 22,
            TextColor = Color.FromArgb(AccentBlue),
            BackgroundColor = Colors.Transparent,
            ImageSource = null
        };
        button.Clicked += click;

        var stack = new VerticalStackLayout
        {
            Spacing = 18,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image { Source = icon, WidthRequest = ResponsiveDashboardIconSize, HeightRequest = ResponsiveDashboardIconSize, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                button
            }
        };

        grid.Children.Add(stack);
        SetRow(stack, row);
        SetColumn(stack, column);
    }

    private async void ShowRestaurantLayout()
    {
        _posSelectedMenu = "Restaurant";
        _isViewingOrderScreen = false;
        await LoadSharedRestaurantLayoutAsync(null);
    }

    private async Task LoadSharedRestaurantLayoutAsync(int? preferredFloorId)
    {
        var tablesView = new RestaurantTablesView
        {
            ConnectionStatus = _connectionStatus,
            IsLoading = true
        };
        _restaurantTablesView = tablesView;
        tablesView.FloorSelected += (_, floorId) =>
        {
            _selectedCachedFloor = _cachedFloors.FirstOrDefault(floor => string.Equals(floor.Id.ToString(), floorId, StringComparison.OrdinalIgnoreCase));
        };
        tablesView.TableSelected += (_, selected) => OnSharedRestaurantTableSelected(selected.Table);

        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        Root.Children.Add(SharedAppFrame("Restaurant", tablesView, "restaurant"));

        try
        {
            await RefreshMotherOperationalCacheAsync();
            _cachedFloors = await _cache.GetFloorsWithTablesAsync();
            _selectedCachedFloor = preferredFloorId.HasValue
                ? _cachedFloors.FirstOrDefault(floor => floor.Id == preferredFloorId.Value)
                : _cachedFloors.FirstOrDefault(floor => floor.Tables.Count > 0) ?? _cachedFloors.FirstOrDefault();
            _cacheStatus = await _cache.GetStatusAsync();

            var version = _cachedFloors.SelectMany(floor => floor.Tables).Select(table => table.Version).DefaultIfEmpty(1).Max().ToString();
            var floors = new FloorSnapshotDto(version, _cachedFloors
                .Select(floor => new FloorDto(floor.Id.ToString(), floor.Name, floor.SortOrder))
                .ToList());
            var tables = new TableSnapshotDto(version, _cachedFloors.SelectMany(floor => floor.Tables)
                .Select(table => new RestaurantTableDto(
                    table.Id.ToString(), table.FloorId.ToString(), table.TableNumber, table.Seats, table.Status,
                    table.PositionX, table.PositionY, table.CurrentOrderId, table.Version, table.Covers,
                    table.CurrentTotal, table.SessionStatus, table.DesignIcon))
                .ToList());

            tablesView.Bind(floors, tables, _selectedCachedFloor?.Id.ToString());
            tablesView.ConnectionStatus = _connectionStatus;
            tablesView.IsLoading = false;
        }
        catch (Exception ex)
        {
            tablesView.IsLoading = false;
            if (_activeApplicationFrame is { } frame)
            {
                frame.ErrorTitle = "Unable to load tables";
                frame.ErrorMessage = ex.Message;
            }
        }
    }

    private Task RefreshMotherOperationalCacheAsync() => RefreshRestaurantLayoutCacheAsync();

    private async Task RefreshRestaurantLayoutCacheAsync()
    {
        if (_currentSession is null || IsMotherUnavailable())
        {
            return;
        }

        try
        {
            var layoutTask = _layoutClient.GetLayoutAsync();
            var menuTask = _menuClient.RefreshCacheAsync();
            var layout = await layoutTask;
            await menuTask;
            if (layout is null)
            {
                return;
            }

            await _cache.ReplaceLayoutAsync(new FloorSnapshotDto(layout.Version, layout.Floors), new TableSnapshotDto(layout.Version, layout.Tables));
            _cachedFloors = await _cache.GetFloorsWithTablesAsync();
        }
        catch
        {
            // Keep the last cached floor plan if Mother cannot send a fresh snapshot.
        }
    }

    private void OnSharedRestaurantTableSelected(RestaurantTableDto table)
    {
        var cached = _cachedFloors.SelectMany(floor => floor.Tables)
            .FirstOrDefault(item => string.Equals(item.Id.ToString(), table.Id, StringComparison.OrdinalIgnoreCase));
        if (cached is null) return;

        if (!string.IsNullOrWhiteSpace(cached.CurrentOrderId))
        {
            _ = OpenTableOrderAsync(cached, Math.Max(cached.Covers, 1));
            return;
        }

        ShowSharedGuestDialog(cached);
    }

    private void ShowSharedGuestDialog(CachedTable table)
    {
        if (_activeApplicationFrame is not { } frame) return;

        _restaurantTablesView?.SetHighlightedTable(table.Id.ToString());

        var picker = new GuestCountControl
        {
            TableTitle = $"Table {table.TableNumber}"
        };
        picker.ResetCustomEntry();
        picker.Cancelled += (_, _) =>
        {
            _restaurantTablesView?.ClearHighlightedTable();
            frame.DialogContent = null;
        };
        picker.CoverConfirmed += async (_, covers) =>
        {
            _restaurantTablesView?.ClearHighlightedTable();
            frame.DialogContent = null;
            await OpenTableOrderAsync(table, covers);
        };

        frame.DialogContent = picker;
    }

    private async Task LoadRestaurantLayoutAsync(int? floorId)
    {
        Root.Children.Clear();
        _cachedFloors = await _cache.GetFloorsWithTablesAsync();
        _cachedFloors = RestaurantLayoutFallback(_cachedFloors);
        _selectedCachedFloor = floorId.HasValue
            ? _cachedFloors.FirstOrDefault(floor => floor.Id == floorId.Value) ?? _cachedFloors.FirstOrDefault()
            : _selectedCachedFloor == null
                ? _cachedFloors.FirstOrDefault(floor => floor.Tables.Count > 0) ?? _cachedFloors.FirstOrDefault()
                : _cachedFloors.FirstOrDefault(floor => floor.Id == _selectedCachedFloor.Id && floor.Tables.Count > 0)
                    ?? _cachedFloors.FirstOrDefault(floor => floor.Tables.Count > 0)
                    ?? _cachedFloors.FirstOrDefault();

        var floorButtons = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        foreach (var floor in _cachedFloors)
        {
            floorButtons.Children.Add(RestaurantFloorButton(floor));
        }

        var tableCanvas = new AbsoluteLayout
        {
            BackgroundColor = Color.FromArgb(PageBackground)
        };

        var selectedTables = (_selectedCachedFloor?.Tables ?? Array.Empty<CachedTable>()).ToList();
        if (selectedTables.Count == 0)
        {
            tableCanvas.Children.Add(new VerticalStackLayout
            {
                Spacing = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = "table_1.png", WidthRequest = 60, HeightRequest = 60, Opacity = 0.35 },
                    new Label
                    {
                        Text = "No Tables on This Floor",
                        FontSize = 18,
                        FontFamily = "OpenSansSemibold",
                        TextColor = Color.FromArgb("#9CA3AF"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            });
            SetAbsoluteLayout(tableCanvas.Children[0], new Rect(0.5, 0.5, -1, -1), AbsoluteLayoutFlags.PositionProportional);
        }
        else
        {
            for (var index = 0; index < selectedTables.Count; index++)
            {
                var table = selectedTables[index];
                var card = TableCard(table);
                var (x, y) = TablePosition(table, index);
                tableCanvas.Children.Add(card);
                SetAbsoluteLayout(card, new Rect(x, y, 148, 148), AbsoluteLayoutFlags.None);
            }
        }

        var page = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(150),
                new RowDefinition(76),
                new RowDefinition(GridLength.Star)
            },
            BackgroundColor = Color.FromArgb(PageBackground),
        };

        page.Children.Add(RestaurantHeader("Restaurant Layout"));

        var floorRow = new Grid
        {
            Padding = new Thickness(20, 15),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(260),
            },
            BackgroundColor = Color.FromArgb(PageBackground)
        };
        floorRow.Children.Add(floorButtons);
        floorRow.Children.Add(new Label
        {
            Text = $"Synced {DateTime.Now:HH:mm:ss}",
            FontSize = 12,
            FontFamily = "InterMedium",
            TextColor = Color.FromArgb("#047857"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        });
        SetColumn(floorRow.Children[1], 1);

        var floorBar = new Border
        {
            BackgroundColor = Color.FromArgb(PageBackground),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            Content = floorRow
        };
        page.Children.Add(floorBar);
        SetRow(floorBar, 1);

        page.Children.Add(tableCanvas);
        SetRow(tableCanvas, 2);
        Root.Children.Add(page);
    }

    private View RestaurantHeader(string title)
    {
        var menuButton = new ImageButton
        {
            Source = "mian.png",
            WidthRequest = 35,
            HeightRequest = 35,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };
        menuButton.Clicked += (_, _) => TogglePosSidebar();

        var logoutButton = new ImageButton
        {
            Source = "outred.png",
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            Margin = new Thickness(0, 10, 0, 0),
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End
        };
        logoutButton.Clicked += (_, _) => Logout();

        _useLoginClockFormat = false;
        _dateLabel = new Label
        {
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(MainText),
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.End
        };
        _timeLabel = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0369A1"),
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.End
        };
        UpdateClock();

        var header = new Border
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            StrokeThickness = 0,
            Content = new Grid
            {
                Padding = new Thickness(20, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(120),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(270)
                },
                Children =
                {
                    menuButton,
                    new Label
                    {
                        Text = title,
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(MainText),
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center
                    },
                    new VerticalStackLayout
                    {
                        Spacing = 5,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.End,
                        Children =
                        {
                            _dateLabel,
                            _timeLabel,
                            logoutButton
                        }
                    }
                }
            }
        };
        var headerGrid = (Grid)header.Content;
        SetColumn(headerGrid.Children[1], 1);
        SetColumn(headerGrid.Children[2], 2);
        return header;
    }

    private static IReadOnlyList<CachedFloor> RestaurantLayoutFallback(IReadOnlyList<CachedFloor> floors)
    {
        if (floors.Any(floor => floor.Tables.Count > 0))
        {
            return floors;
        }

        return new[]
        {
            new CachedFloor(1, "1st Floor", 1, new[]
            {
                new CachedTable(10, 1, "10", 4, "Available", 0m, null, 0, null, null, 0, 1, 52, 64),
                new CachedTable(11, 1, "11", 4, "Available", 0m, null, 0, null, null, 0, 1, 264, 64),
                new CachedTable(12, 1, "12", 4, "Occupied", 0m, "demo-order-12", 4, null, "Ordering", 0, 1, 476, 64),
                new CachedTable(13, 1, "13", 4, "Available", 0m, null, 0, null, null, 0, 1, 52, 308),
                new CachedTable(14, 1, "14", 4, "Available", 0m, null, 0, null, null, 0, 1, 264, 308)
            }),
            new CachedFloor(2, "2nd Floor", 2, new[]
            {
                new CachedTable(20, 2, "20", 4, "Available", 0m, null, 0, null, null, 0, 1, 52, 64),
                new CachedTable(21, 2, "21", 4, "Available", 0m, null, 0, null, null, 0, 1, 264, 64),
                new CachedTable(22, 2, "22", 4, "Available", 0m, null, 0, null, null, 0, 1, 476, 64),
                new CachedTable(23, 2, "23", 4, "Available", 0m, null, 0, null, null, 0, 1, 688, 64)
            })
        };
    }

    private Button RestaurantFloorButton(CachedFloor floor)
    {
        var selected = floor.Id == _selectedCachedFloor?.Id;
        var button = new Button
        {
            Text = $"{floor.Name} ({floor.Tables.Count})",
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            BackgroundColor = Color.FromArgb(selected ? AccentBlue : "#F6F9FC"),
            TextColor = selected ? Colors.White : Color.FromArgb("#1F2937"),
            CornerRadius = 22,
            HeightRequest = 46,
            Padding = new Thickness(22, 0)
        };
        button.Clicked += async (_, _) => await LoadRestaurantLayoutAsync(floor.Id);
        return button;
    }

    private View TableCard(CachedTable table)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ShowGuestDialog(table);
        var (bg, border, text) = GetCachedTableColors(table);
        var tableImage = TableAssetFor(table);

        var card = new Border
        {
            WidthRequest = 148,
            HeightRequest = 148,
            Padding = 14,
            Stroke = border,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = bg,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.14f, Radius = 8, Offset = new Point(0, 3) },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = tableImage, WidthRequest = 58, HeightRequest = 58, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = table.TableNumber, FontSize = 20, FontFamily = "InterBold", TextColor = text, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "●", FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = border, HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static (double X, double Y) TablePosition(CachedTable table, int index)
    {
        if (table.PositionX > 0 || table.PositionY > 0)
        {
            return (Math.Max(22, table.PositionX), Math.Max(36, table.PositionY));
        }

        const double cardWidth = 148;
        const double horizontalGap = 64;
        const double verticalGap = 96;
        var column = index % 5;
        var row = index / 5;
        return (52 + column * (cardWidth + horizontalGap), 64 + row * (cardWidth + verticalGap));
    }

    private static void SetAbsoluteLayout(IView view, Rect bounds, AbsoluteLayoutFlags flags)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Microsoft.Maui.Controls.AbsoluteLayout.LayoutBoundsProperty, bounds);
            bindable.SetValue(Microsoft.Maui.Controls.AbsoluteLayout.LayoutFlagsProperty, flags);
        }
    }

    private static (Color Bg, Color Border, Color Text) GetCachedTableColors(CachedTable table)
    {
        if (table.SessionStatus?.Equals("Payment", StringComparison.OrdinalIgnoreCase) == true
            || table.SessionStatus?.Equals("Cleaning", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb(Danger));
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId) || table.Status.Equals("Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb(Danger));
        }

        if (table.Status.Equals("Reserved", StringComparison.OrdinalIgnoreCase))
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb(Danger));
        }

        return (Color.FromArgb("#ECFDF5"), Color.FromArgb("#86EFAC"), Color.FromArgb(Success));
    }

    private static string TableStatusText(CachedTable table)
    {
        if (table.SessionStatus?.Equals("Payment", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Payment";
        }

        if (table.SessionStatus?.Equals("Cleaning", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Needs attention";
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId) || table.Status.Equals("Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return "Occupied";
        }

        return table.Status.Equals("Reserved", StringComparison.OrdinalIgnoreCase) ? "Reserved" : "Available";
    }

    private static string TableDetailText(CachedTable table)
    {
        if (table.CurrentTotal > 0)
        {
            return $"{Math.Max(table.Covers, 1)} covers · {Money(table.CurrentTotal)}";
        }

        return $"{table.Seats} seats";
    }

    private static string TableAssetFor(CachedTable table)
    {
        if (table.SessionStatus?.Equals("Cleaning", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "table_4.png";
        }

        if (table.Status.Equals("Reserved", StringComparison.OrdinalIgnoreCase))
        {
            return "table_3.png";
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId) || table.Status.Equals("Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return "table_2.png";
        }

        return "table_1.png";
    }

    private async void ShowGuestDialog(CachedTable table)
    {
        _selectedCachedTable = table;
        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId))
        {
            await OpenTableOrderAsync(table, Math.Max(table.Covers, 1));
            return;
        }

        var overlay = new Grid { BackgroundColor = Color.FromRgba(0, 0, 0, 0.48), InputTransparent = false };
        var numbers = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            Padding = 20
        };

        for (var i = 1; i <= 12; i++)
        {
            var guests = i;
            var button = new Button
            {
                Text = i.ToString(),
                FontSize = 18,
                FontFamily = "OpenSansSemibold",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb(MainText),
                CornerRadius = 10,
                HeightRequest = 56,
                Padding = 0
            };
            button.Clicked += async (_, _) =>
            {
                _guests = guests;
                Root.Children.Remove(overlay);
                await OpenTableOrderAsync(table, guests);
            };
            numbers.Children.Add(button);
            SetRow(button, (i - 1) / 4);
            SetColumn(button, (i - 1) % 4);
        }

        var close = new Button { Text = "X", FontSize = 16, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(Danger), BackgroundColor = Color.FromArgb("#FEE2E2"), CornerRadius = 18, WidthRequest = 36, HeightRequest = 36, Padding = 0 };
        close.Clicked += (_, _) => Root.Children.Remove(overlay);

        var otherGuestEntry = new Entry
        {
            Placeholder = "Other number...",
            FontSize = 15,
            FontFamily = "OpenSansRegular",
            BackgroundColor = Color.FromArgb(PageBackground),
            Keyboard = Keyboard.Numeric,
            HeightRequest = 48,
            TextColor = Color.FromArgb(MainText),
            PlaceholderColor = Color.FromArgb("#9CA3AF")
        };

        var goButton = new Button
        {
            Text = "Go",
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            BackgroundColor = Color.FromArgb(AccentBlue),
            TextColor = Color.FromArgb(PageBackground),
            CornerRadius = 8,
            WidthRequest = 64,
            HeightRequest = 48
        };
        goButton.Clicked += async (_, _) =>
        {
            _guests = int.TryParse(otherGuestEntry.Text, out var enteredGuests) && enteredGuests > 0 ? enteredGuests : 4;
            Root.Children.Remove(overlay);
            await OpenTableOrderAsync(table, _guests);
        };

        var body = new Grid { BackgroundColor = Color.FromArgb(TopBarBackground), Children = { numbers } };

        var footer = new Grid
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            Padding = new Thickness(20, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(64)
            },
            ColumnSpacing = 12,
            Children =
            {
                otherGuestEntry,
                goButton
            }
        };

        var modal = new Border
        {
            WidthRequest = 380,
            BackgroundColor = Color.FromArgb(PageBackground),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.25f, Radius = 24, Offset = new Point(0, 8) },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Grid
                    {
                        Padding = new Thickness(24, 20),
                        BackgroundColor = Color.FromArgb("#F9FAFB"),
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                        Children =
                        {
                            new VerticalStackLayout
                            {
                                Spacing = 4,
                                Children =
                                {
                                    new Label { Text = $"Table {table.TableNumber}", FontSize = 20, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(MainText) },
                                    new Label { Text = "How many guests?", FontSize = 14, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb(MutedText) }
                                }
                            },
                            close
                        }
                    },
                    body,
                    footer
                }
            }
        };
        SetColumn(close, 1);
        SetRow(body, 1);
        SetColumn(goButton, 1);
        SetRow(footer, 2);
        SetRow(modal, 0);
        overlay.Children.Add(modal);
        Root.Children.Add(overlay);
    }

    private async Task OpenTableOrderAsync(CachedTable table, int covers)
    {
        var decision = _offlinePolicy.Evaluate(
            ClientOperation.OpenCollectionOrder,
            await _offlinePolicy.IsMotherOnlineAsync());
        if (!decision.Allowed)
        {
            ShowToast(decision.Message);
            return;
        }

        _selectedCachedTable = table;
        _guests = Math.Max(covers, 1);
        _connectionStatus = "Syncing";
        try
        {
            var result = await _orderClient.OpenOrCreateTableOrderAsync(table, _guests, _currentSession);
            await ApplyMotherOrderResultAsync(result);
            await LoadOrderMenuAsync();
            await RefreshRestaurantLayoutCacheAsync();
            _connectionStatus = "Connected";
            ShowOrder();
        }
        catch (Exception ex)
        {
            _connectionStatus = "Connected";
            ShowToast(ex.Message);
        }
    }

    private async Task ApplyMotherOrderResultAsync(MotherCommandResult result)
    {
        _currentOrder = result.State;
        await _cache.SaveOrderStateAsync(result.State);
        if (result.ConflictDetected)
        {
            _connectionStatus = "Connected";
            ShowToast(string.IsNullOrWhiteSpace(result.Message)
                ? "Order updated elsewhere — reload"
                : result.Message);
        }
    }

    private async Task LoadOrderMenuAsync()
    {
        await _menuClient.RefreshCacheAsync();
        _cachedCategories = await _cache.GetMenuCategoriesAsync();
        _selectedCachedCategory = _cachedCategories.FirstOrDefault();
        if (_selectedCachedCategory != null)
        {
            _cachedProducts = await _cache.GetProductsByCategoryAsync(_selectedCachedCategory.Id);
        }
    }

    private void ShowOrder()
    {
        _isViewingOrderScreen = true;
        Root.Children.Clear();
        Root.Children.Add(AppFrame(OrderScreenTitle(), OrderContent(), false));
    }

    private string OrderScreenTitle()
    {
        return _currentOrder?.OrderType switch
        {
            "Collection" => "Collection Order",
            "Delivery" => "Delivery Order",
            _ => "Table Order"
        };
    }

    private View OrderContent()
    {
        var compact = IsCompactLayout;
        var grid = new Grid
        {
            ColumnSpacing = compact ? 0 : 18,
            RowSpacing = compact ? 18 : 0,
            Padding = compact ? 16 : 24,
            BackgroundColor = Color.FromArgb(PageBackground)
        };
        if (compact)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2.1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(560));
        }

        var left = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb(LightSurface),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(18),
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(60),
                    new RowDefinition(58),
                    new RowDefinition(GridLength.Star)
                },
                RowSpacing = 18,
                Children =
                {
                    new Entry
                    {
                        Placeholder = "Search menu items...",
                        FontSize = 16,
                        FontFamily = "OpenSansRegular",
                        BackgroundColor = Color.FromArgb("#F1F5F9"),
                        HeightRequest = 48,
                        TextColor = Color.FromArgb(MainText),
                        PlaceholderColor = Color.FromArgb("#94A3B8")
                    },
                    CategoryTabs(),
                    MenuItemsGrid()
                }
            }
        };
        SetRow(((Grid)left.Content).Children[1], 1);
        SetRow(((Grid)left.Content).Children[2], 2);

        var right = new Border
        {
            WidthRequest = compact ? -1 : 560,
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb(LightSurface),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 18,
            Content = OrderSummary()
        };

        grid.Children.Add(left);
        grid.Children.Add(right);
        if (compact)
        {
            SetRow(right, 1);
        }
        else
        {
            SetColumn(right, 1);
        }
        return grid;
    }

    private View CategoryTabs()
    {
        var row = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };
        foreach (var category in _cachedCategories)
        {
            var selected = category == _selectedCachedCategory;
            var button = new Button
            {
                Text = category.Name,
                BackgroundColor = Color.FromArgb(selected ? PrimaryAction : "#F1F5F9"),
                TextColor = selected ? Color.FromArgb(PageBackground) : Color.FromArgb("#334155"),
                FontSize = 15,
                FontFamily = "OpenSansSemibold",
                CornerRadius = 8,
                WidthRequest = 158,
                HeightRequest = 48,
                Padding = new Thickness(14, 0)
            };
            button.Clicked += async (_, _) =>
            {
                _selectedCachedCategory = category;
                _cachedProducts = await _cache.GetProductsByCategoryAsync(category.Id);
                ShowOrder();
            };
            row.Children.Add(button);
        }

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = row
        };
    }

    private View MenuItemsGrid()
    {
        var items = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            Direction = FlexDirection.Row,
            AlignContent = FlexAlignContent.Start,
            Padding = new Thickness(0, 24, 0, 0)
        };
        foreach (var item in _cachedProducts)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await AddProductToOrderAsync(item);

            var card = new Border
            {
                WidthRequest = 174,
                HeightRequest = 126,
                Margin = new Thickness(0, 0, 28, 28),
                Stroke = Color.FromArgb(BorderLight),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Color.FromArgb(PageBackground),
                Padding = 14,
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label
                        {
                            Text = item.Name,
                            FontSize = 16,
                            FontFamily = "OpenSansSemibold",
                            TextColor = Color.FromArgb(MainText),
                            LineBreakMode = LineBreakMode.WordWrap,
                            MaxLines = 2
                        },
                        new Label
                        {
                            Text = Money(item.Price),
                            FontSize = 18,
                            FontFamily = "OpenSansBold",
                            TextColor = Color.FromArgb(DashboardLabelBlue)
                        }
                    }
                }
            };
            card.GestureRecognizers.Add(tap);
            items.Children.Add(card);
        }

        return new ScrollView { Content = items };
    }

    private async Task AddProductToOrderAsync(CachedProduct product)
    {
        if (_currentOrder == null)
        {
            ShowToast("Open a table order first.");
            return;
        }

        if (product.ModifierGroups.Count > 0)
        {
            var dialog = new OrderOptionSelectionDialog("Choose Addons", "Select the options for this item.");
            dialog.SetOptions(product.ModifierGroups.SelectMany(group => group.Modifiers)
                .Select(modifier => new OrderDialogOption(modifier.Id.ToString(), modifier.Name)));
            PresentOrderDialog(dialog, async () =>
            {
                var selected = product.ModifierGroups.SelectMany(group => group.Modifiers)
                    .Where(modifier => dialog.SelectedIds.Contains(modifier.Id.ToString()))
                    .Select(modifier => modifier.Name)
                    .ToList();
                await AddProductToOrderAsync(product, selected);
            });
            return;
        }

        await AddProductToOrderAsync(product, []);
    }

    private async Task AddProductToOrderAsync(CachedProduct product, IReadOnlyList<string> selectedModifiers)
    {
        if (_currentOrder is null) return;
        if (IsCustomerHubOrderType(_currentOrder.OrderType))
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.EditCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                ShowToast(decision.Message);
                return;
            }
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.AddItemAsync(_currentOrder, product, selectedModifiers);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private static bool IsCustomerHubOrderType(string? orderType) =>
        OrderWeb.Contracts.Access.CustomerOrderHubRules.IsCustomerHubOrderType(orderType);

    private View OrderSummary()
    {
        var lines = new VerticalStackLayout { Spacing = 10 };
        foreach (var line in _currentOrder?.Lines ?? Array.Empty<MotherOrderLine>())
        {
            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(42), new ColumnDefinition(42), new ColumnDefinition(42), new ColumnDefinition(80) },
                Children =
                {
                    new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = $"{line.Quantity} x {line.Name}", FontSize = 15, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(MainText) },
                            new Label { Text = LineMeta(line), FontSize = 12, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb(MutedText) }
                        }
                    },
                    SmallActionButton("-", async (_, _) => await UpdateOrderLineQuantityAsync(line, line.Quantity - 1)),
                    SmallActionButton("+", async (_, _) => await UpdateOrderLineQuantityAsync(line, line.Quantity + 1)),
                    SmallActionButton("X", async (_, _) => await RemoveOrderLineAsync(line)),
                    new Label { Text = Money(line.Quantity * line.UnitPrice), FontSize = 15, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.End }
                }
            };
            SetColumn(row.Children[1], 1);
            SetColumn(row.Children[2], 2);
            SetColumn(row.Children[3], 3);
            SetColumn(row.Children[4], 4);
            lines.Children.Add(row);
        }

        if (lines.Children.Count == 0)
        {
            lines.Children.Add(new Label
            {
                Text = "No items added yet.",
                FontSize = 15,
                FontFamily = "OpenSansRegular",
                TextColor = Color.FromArgb("#94A3B8"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 48, 0, 0)
            });
        }

        var panel = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(92),
                new RowDefinition(GridLength.Star),
                new RowDefinition(250)
            }
        };

        var paymentButton = PrimaryButton("PAYMENT", "#F59E0B", (_, _) => ShowPayment());
        paymentButton.WidthRequest = 170;
        paymentButton.HeightRequest = 58;
        paymentButton.CornerRadius = 8;

        var headerContent = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(170) },
            ColumnSpacing = 12,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                new Label
                {
                    Text = OrderPanelTitle(),
                    FontSize = 20,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb(MainText),
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.NoWrap
                },
                        new Label { Text = OrderHeaderDetail(), FontSize = 13, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb(MutedText) }
                    }
                },
                paymentButton
            }
        };
        SetColumn(paymentButton, 1);

        panel.Children.Add(new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb(PageBackground),
            Padding = 14,
            Content = headerContent
        });
        panel.Children.Add(new ScrollView { Content = lines, Margin = new Thickness(0, 18) });
        SetRow(panel.Children[1], 1);

        var totalPanel = new Border
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 18,
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "TOTAL",
                        FontSize = 15,
                        FontFamily = "OpenSansRegular",
                        TextColor = Color.FromArgb(MutedText),
                        VerticalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = Money(_currentOrder?.Total ?? 0m),
                        FontSize = 24,
                        FontFamily = "OpenSansBold",
                        TextColor = Color.FromArgb("#10B981"),
                        VerticalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };
        SetColumn(((Grid)totalPanel.Content).Children[1], 1);

        var serviceButton = ActionButton("SERVICE", "#8B5CF6", async (_, _) => await QueueClientActionAsync("service_charge", "Service charge request queued for Mother POS."));
        var notesButton = ActionButton("NOTES", AccentBlue, async (_, _) => await AddNoteToFirstLineAsync());
        var voidButton = ActionButton("VOID", "#EF4444", (_, _) => ShowVoidOrderDialog());
        var moreButton = ActionButton("MORE ▼", "#64748B", (_, _) => ShowMoreOptions());
        var quickActions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14,
            Children =
            {
                serviceButton,
                notesButton,
                voidButton,
                moreButton
            }
        };
        SetColumn(notesButton, 1);
        SetColumn(voidButton, 2);
        SetColumn(moreButton, 3);

        var kitchenButton = ActionButton("SEND TO KITCHEN", PrimaryAction, async (_, _) => await SendCurrentOrderToKitchenAsync());
        var printButton = ActionButton("PRINT", PrimaryAction, async (_, _) => await RequestPrintAsync("bill", _currentOrder?.OrderId));
        var mainActions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12,
            Children =
            {
                kitchenButton,
                printButton
            }
        };
        SetColumn(printButton, 1);

        var controls = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                totalPanel,
                quickActions,
                mainActions
            }
        };

        panel.Children.Add(new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb(PageBackground),
            Padding = 16,
            Content = controls
        });
        SetRow(panel.Children[2], 2);
        return panel;
    }

    private string OrderPanelTitle()
    {
        if (_currentOrder?.OrderType is "Collection" or "Delivery")
        {
            return $"Order # {_currentOrder.OrderNumber ?? "New"}";
        }

        return $"Order # Table {_currentOrder?.TableNumber ?? _selectedCachedTable?.TableNumber ?? "-"}";
    }

    private static string LineMeta(MotherOrderLine line)
    {
        var modifiers = line.Modifiers.Count == 0 ? "" : string.Join(", ", line.Modifiers);
        if (!string.IsNullOrWhiteSpace(line.Notes) && !string.IsNullOrWhiteSpace(modifiers))
        {
            return $"{modifiers} · {line.Notes}";
        }

        return string.Concat(modifiers, line.Notes);
    }

    private string OrderHeaderDetail()
    {
        if (_currentOrder?.OrderType is "Collection" or "Delivery")
        {
            return _currentOrder.OrderType;
        }

        return $"Guests = {_currentOrder?.Guests ?? _guests}";
    }

    private Button SmallActionButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = text == "X" ? Color.FromArgb("#FEE2E2") : Color.FromArgb("#F1F5F9"),
            TextColor = text == "X" ? Color.FromArgb(Danger) : Color.FromArgb(MainText),
            CornerRadius = 6,
            WidthRequest = 34,
            HeightRequest = 34,
            Padding = 0
        };
        button.Clicked += click;
        return button;
    }

    private async Task UpdateOrderLineQuantityAsync(MotherOrderLine line, int quantity)
    {
        if (_currentOrder == null || quantity <= 0)
        {
            return;
        }

        if (!await EnsureCollectionMutationAllowedAsync(ClientOperation.EditCollectionOrder))
        {
            return;
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.UpdateQuantityAsync(_currentOrder, line, quantity);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private async Task RemoveOrderLineAsync(MotherOrderLine line)
    {
        if (_currentOrder == null)
        {
            return;
        }

        if (!await EnsureCollectionMutationAllowedAsync(ClientOperation.EditCollectionOrder))
        {
            return;
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.RemoveItemAsync(_currentOrder, line);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private async Task<bool> EnsureCollectionMutationAllowedAsync(ClientOperation operation)
    {
        if (_currentOrder is null || !IsCustomerHubOrderType(_currentOrder.OrderType))
        {
            return true;
        }

        var decision = _offlinePolicy.Evaluate(operation, await _offlinePolicy.IsMotherOnlineAsync());
        if (!decision.Allowed)
        {
            ShowToast(decision.Message);
            return false;
        }

        return true;
    }

    private async Task AddNoteToFirstLineAsync()
    {
        if (_currentOrder?.Lines.FirstOrDefault() is not { } line)
        {
            ShowToast("Add an item before adding notes.");
            return;
        }

        var dialog = new OrderNoteDialog();
        PresentOrderDialog(dialog, async () =>
        {
            if (string.IsNullOrWhiteSpace(dialog.Note)) return;
            if (!await EnsureCollectionMutationAllowedAsync(ClientOperation.EditCollectionOrder))
            {
                return;
            }

            _connectionStatus = "Syncing";
            var result = await _orderClient.AddNoteAsync(_currentOrder, line, dialog.Note.Trim());
            await ApplyMotherOrderResultAsync(result);
            _connectionStatus = "Connected";
            ShowOrder();
        });
    }

    private async Task SendCurrentOrderToKitchenAsync()
    {
        if (_currentOrder == null)
        {
            return;
        }

        if (IsCustomerHubOrderType(_currentOrder.OrderType))
        {
            var decision = _offlinePolicy.Evaluate(
                ClientOperation.PrintCollectionOrder,
                await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                ShowToast(decision.Message);
                return;
            }
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.SendToKitchenAsync(_currentOrder);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowToast(result.Message);
        ShowOrder();
    }

    private async Task RefreshLatestOrderAsync()
    {
        if (_currentOrder == null)
        {
            return;
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.RefreshLatestAsync(_currentOrder);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private Button ActionButton(string text, string color, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Color.FromArgb(PageBackground),
            FontSize = 15,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 8,
            HeightRequest = 60
        };
        button.Clicked += click;
        return button;
    }

    private void ShowMoreOptions()
    {
        var dialog = new MoreOrderOptionsDialog();
        dialog.SetOptions(
        [
            new OrderDialogOption("quick-note", "Quick note", "Add a common kitchen instruction"),
            new OrderDialogOption("quantity", "Quantity", "Set a line quantity"),
            new OrderDialogOption("discount", "Discount", "Requires Mother validation"),
            new OrderDialogOption("manager", "Manager approval", "Ask a manager to approve"),
            new OrderDialogOption("previous", "Previous orders", "View permitted prior orders"),
            new OrderDialogOption("tasting", "Tasting menu", "Review courses before adding"),
            new OrderDialogOption("courses", "Course progress", "Track tasting-menu courses")
        ]);
        PresentOrderDialog(dialog, () =>
        {
            var action = dialog.SelectedIds.FirstOrDefault();
            switch (action)
            {
                case "quick-note": ShowQuickNoteDialog(); break;
                case "quantity": ShowQuantityDialog(_currentOrder?.Lines.FirstOrDefault()); break;
                case "discount": ShowDiscountDialog(); break;
                case "manager": ShowManagerApprovalDialog(); break;
                case "previous": ShowPreviousOrdersDialog(); break;
                case "tasting": ShowTastingMenuDialog(); break;
                case "courses": ShowCourseProgressDialog(); break;
            }
            return Task.CompletedTask;
        });
    }

    private async void ShowVoidOrderDialog()
    {
        if (_currentOrder is null) { ShowToast("There is no open order to void."); return; }
        var dialog = new VoidOrderConfirmationDialog();
        PresentOrderDialog(dialog, async () =>
        {
            if (IsCustomerHubOrderType(_currentOrder.OrderType))
            {
                var decision = _offlinePolicy.Evaluate(
                    ClientOperation.VoidCollectionOrder,
                    await _offlinePolicy.IsMotherOnlineAsync());
                if (!decision.Allowed)
                {
                    ShowToast(decision.Message);
                    return;
                }

                try
                {
                    _connectionStatus = "Syncing";
                    var result = await _orderClient.VoidCollectionOrderAsync(_currentOrder);
                    await ApplyMotherOrderResultAsync(result);
                    await RefreshRestaurantLayoutCacheAsync();
                    _connectionStatus = "Connected";
                    ShowToast(result.Message);
                    ShowOrder();
                }
                catch (Exception ex)
                {
                    _connectionStatus = "Connected";
                    ShowToast(ex.Message);
                }

                return;
            }

            await QueueClientActionAsync("void_order", "Void request queued for Mother POS. It is not confirmed until Mother accepts it.");
            ShowToast("Void request is pending Mother confirmation.");
        });
    }

    private void ShowQuickNoteDialog()
    {
        if (_currentOrder?.Lines.FirstOrDefault() is not { } line) { ShowToast("Add an item before adding a note."); return; }
        var dialog = new OrderOptionSelectionDialog("Quick Notes", "Choose a kitchen instruction.", false);
        dialog.SetOptions([new("no-onions", "No onions"), new("extra-spicy", "Extra spicy"), new("allergy", "Allergy — check with manager")]);
        PresentOrderDialog(dialog, async () =>
        {
            var note = dialog.SelectedIds.FirstOrDefault()?.Replace('-', ' ') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(note)) return;
            if (!await EnsureCollectionMutationAllowedAsync(ClientOperation.EditCollectionOrder))
            {
                return;
            }

            var result = await _orderClient.AddNoteAsync(_currentOrder, line, note);
            await ApplyMotherOrderResultAsync(result);
            ShowOrder();
        });
    }

    private void ShowQuantityDialog(MotherOrderLine? line)
    {
        if (line is null) { ShowToast("Select an order line first."); return; }
        var dialog = new OrderQuantityDialog { Quantity = line.Quantity };
        PresentOrderDialog(dialog, async () => await UpdateOrderLineQuantityAsync(line, dialog.Quantity));
    }

    private void ShowDiscountDialog()
    {
        var dialog = new OrderDiscountDialog();
        PresentOrderDialog(dialog, () =>
        {
            ShowToast("Discount request is awaiting Mother validation.");
            return Task.CompletedTask;
        });
    }

    private void ShowManagerApprovalDialog()
    {
        var dialog = new ManagerApprovalDialog();
        PresentOrderDialog(dialog, () =>
        {
            ShowToast("Manager approval must be validated by Mother POS.");
            return Task.CompletedTask;
        });
    }

    private void ShowPreviousOrdersDialog()
    {
        var dialog = new PreviousOrdersDialog();
        dialog.SetOptions((_currentOrder is null ? [] : [new OrderDialogOption(_currentOrder.OrderId, _currentOrder.OrderNumber, "Current cached order") ]));
        PresentOrderDialog(dialog, () => Task.CompletedTask);
    }

    private void ShowTastingMenuDialog()
    {
        var dialog = new TastingMenuConfirmationDialog();
        dialog.SetOptions([new("starter", "Starter"), new("main", "Main course"), new("dessert", "Dessert")]);
        PresentOrderDialog(dialog, () => { ShowToast("Tasting-menu request awaits Mother validation."); return Task.CompletedTask; });
    }

    private void ShowCourseProgressDialog()
    {
        var dialog = new TastingCourseProgressDialog();
        dialog.SetProgress(0, 3);
        PresentOrderDialog(dialog, () => Task.CompletedTask);
    }

    private void PresentOrderDialog(OrderDialogBase dialog, Func<Task> onConfirmed)
    {
        if (_activeApplicationFrame is not { } frame)
        {
            ShowToast("Return to the POS shell to continue.");
            return;
        }

        dialog.Cancelled += (_, _) => frame.DialogContent = null;
        dialog.Confirmed += async (_, _) =>
        {
            frame.DialogContent = null;
            try { await onConfirmed(); }
            catch (Exception ex) { frame.ErrorTitle = "Order action failed"; frame.ErrorMessage = ex.Message; }
        };
        frame.DialogContent = dialog;
    }

    private async void ShowCashDrawer()
    {
        _posSelectedMenu = "Cash Drawer";
        Root.Children.Clear();
        _recentPrintRequests = await _cache.GetRecentPrintRequestsAsync();

        var page = new Grid
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            RowDefinitions =
            {
                new RowDefinition(150),
                new RowDefinition(GridLength.Star)
            }
        };

        page.Children.Add(RestaurantHeader("Cash Drawer"));

        var canOpen = _currentSession?.HasPermission("client.cash_drawer.open") == true;
        var drawerRequests = _recentPrintRequests
            .Where(request => request.PrintType.Contains("cash drawer", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();

        var contentGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 24,
            Padding = new Thickness(32, 36, 32, 32),
            MaximumWidthRequest = 1120,
            HorizontalOptions = LayoutOptions.Center
        };

        var openButton = new Button
        {
            Text = "Open Cash Drawer",
            FontSize = 18,
            FontFamily = "OpenSansBold",
            TextColor = Colors.White,
            BackgroundColor = canOpen ? Color.FromArgb("#10B981") : Color.FromArgb("#94A3B8"),
            CornerRadius = 8,
            HeightRequest = 64,
            IsEnabled = canOpen
        };
        openButton.Clicked += async (_, _) =>
        {
            openButton.IsEnabled = false;
            openButton.Text = "Opening...";
            await RequestPrintAsync("cash drawer open", null, false);
            ShowCashDrawer();
        };

        var refreshButton = new Button
        {
            Text = "Refresh",
            FontSize = 16,
            FontFamily = "OpenSansBold",
            TextColor = Color.FromArgb(MainText),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            CornerRadius = 8,
            HeightRequest = 54
        };
        refreshButton.Clicked += (_, _) => ShowCashDrawer();

        var controlPanel = CashDrawerPanel(new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                new Label { Text = "Drawer Control", FontSize = 22, FontFamily = "OpenSansBold", TextColor = Color.FromArgb("#1E293B") },
                CashDrawerStatusRow(canOpen),
                new Label
                {
                    Text = "Request Mother POS to open the configured receipt-printer cash drawer.",
                    FontSize = 15,
                    TextColor = Color.FromArgb(MutedText),
                    LineBreakMode = LineBreakMode.WordWrap
                },
                openButton,
                refreshButton
            }
        });

        var activityStack = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Label { Text = "Recent Activity", FontSize = 22, FontFamily = "OpenSansBold", TextColor = Color.FromArgb("#1E293B") }
            }
        };

        if (drawerRequests.Count == 0)
        {
            activityStack.Children.Add(new Label
            {
                Text = "No cash drawer requests yet.",
                FontSize = 15,
                TextColor = Color.FromArgb("#94A3B8"),
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        else
        {
            foreach (var request in drawerRequests)
            {
                activityStack.Children.Add(CashDrawerActivityRow(request));
            }
        }

        contentGrid.Children.Add(controlPanel);
        contentGrid.Children.Add(CashDrawerPanel(activityStack));
        SetColumn(contentGrid.Children[1], 1);

        var scroll = new ScrollView { Content = contentGrid };
        page.Children.Add(scroll);
        SetRow(scroll, 1);
        Root.Children.Add(page);
    }

    private View CashDrawerStatusRow(bool canOpen)
    {
        var latestDrawer = _recentPrintRequests.FirstOrDefault(request => request.PrintType.Contains("cash drawer", StringComparison.OrdinalIgnoreCase));
        var statusText = !canOpen
            ? "No Permission"
            : latestDrawer?.Status ?? "Ready";
        var statusColor = !canOpen ? Danger : latestDrawer is null ? "#10B981" : PrintStatusColor(latestDrawer.Status);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(150)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = "Mother POS drawer", FontSize = 15, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb(MainText) },
                        new Label { Text = _connectionStatus, FontSize = 13, TextColor = Color.FromArgb(MutedText) }
                    }
                },
                StatusPill(statusText, statusColor)
            }
        };
        SetColumn(grid.Children[1], 1);
        return grid;
    }

    private View CashDrawerActivityRow(PrintRequestState request)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(126)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = FormatCashDrawerTime(request.UpdatedUtc), FontSize = 15, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(MainText) },
                        new Label { Text = request.Message, FontSize = 12, TextColor = Color.FromArgb(MutedText), LineBreakMode = LineBreakMode.TailTruncation }
                    }
                },
                StatusPill(request.Status, PrintStatusColor(request.Status))
            }
        };
        SetColumn(row.Children[1], 1);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(14, 12),
            Content = row
        };
    }

    private static View CashDrawerPanel(View content)
    {
        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(24),
            MinimumHeightRequest = 360,
            Content = content
        };
    }

    private static string FormatCashDrawerTime(string? value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("HH:mm:ss · dd MMM")
            : "Pending";
    }

    private async Task<PrintRequestState?> RequestPrintAsync(string printType, string? orderId, bool redraw = true)
    {
        if (printType == "cash drawer open" && (_currentSession == null ||
            (!_currentSession.HasPermission("client.cash_drawer.open") &&
             !_currentSession.HasPermission(OrderWeb.Contracts.Capabilities.PosCapabilityKeys.OpenCashDrawer))))
        {
            ShowToast("This user cannot open the cash drawer from Client POS.");
            return null;
        }

        if (printType != "cash drawer open" && (_currentSession == null ||
            (!_currentSession.HasPermission(OrderWeb.Contracts.Capabilities.PosCapabilityKeys.PrintReceipts) &&
             !_currentSession.HasPermission(OrderWeb.Contracts.Capabilities.PosCapabilityKeys.CreateOrders) &&
             !_currentSession.HasPermission("client.order.print"))))
        {
            ShowToast("This user cannot send print requests from Client POS.");
            return null;
        }

        if (printType != "cash drawer open" &&
            _currentOrder is not null &&
            IsCustomerHubOrderType(_currentOrder.OrderType) &&
            !await EnsureCollectionMutationAllowedAsync(ClientOperation.PrintCollectionOrder))
        {
            return null;
        }

        _connectionStatus = "Syncing";
        var request = await _printClient.RequestPrintAsync(printType, orderId, _currentSession);
        await _cache.SavePrintRequestAsync(request);

        _recentPrintRequests = await _cache.GetRecentPrintRequestsAsync();
        _connectionStatus = "Connected";
        ShowToast($"{printType}: {request.Status} — {request.Message}");
        if (redraw)
        {
            ShowOrder();
        }

        return request;
    }

    private View PrintStatusPanel()
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Children.Add(new Label { Text = "Print Requests", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText) });

        var requests = _recentPrintRequests.Take(3).ToList();
        if (requests.Count == 0)
        {
            stack.Children.Add(new Label { Text = "Mother print status will appear here.", FontSize = 12, TextColor = Color.FromArgb("#94A3B8") });
        }

        foreach (var request in requests)
        {
            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(110) },
                Children =
                {
                    new Label { Text = request.PrintType, FontSize = 13, TextColor = Color.FromArgb(SecondaryText), VerticalTextAlignment = TextAlignment.Center },
                    StatusPill(request.Status, PrintStatusColor(request.Status))
                }
            };
            SetColumn(row.Children[1], 1);
            stack.Children.Add(row);
        }

        return new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb(LightSurface),
            Padding = 12,
            Content = stack
        };
    }

    private static string PrintStatusColor(string status)
    {
        return status switch
        {
            "printed" => "#10B981",
            "printing" => "#3B82F6",
            "queued" => "#F59E0B",
            "printer offline" or "failed" => "#EF4444",
            _ => "#64748B"
        };
    }

    private void ShowSharedCustomerFlow()
    {
        var capabilities = ClientCapabilityResolver.ForRole(_currentSession?.Role, _currentSession?.Permissions);
        if (!capabilities.Contains(PosCapabilityKeys.ViewCustomers) && !capabilities.Contains(PosCapabilityKeys.ManageCustomers))
        {
            ShowToast("Mother POS has not granted customer access to this terminal.");
            return;
        }

        _posSelectedMenu = "Customers";
        var view = new CustomerFlowView();
        view.SearchRequested += async (_, term) => await SearchSharedCustomersAsync(view, term);
        view.SubmissionRequested += async (_, submission) => await SubmitSharedCustomerFlowAsync(view, submission);
        Root.Children.Clear();
        Root.Children.Add(SharedAppFrame("Customers", view, "customers"));
    }

    private async Task SearchSharedCustomersAsync(CustomerFlowView view, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            view.SetStatus("Enter a name, phone number, address, or postcode.");
            view.SetResults([]);
            return;
        }

        view.SetLoading(true);
        var request = new OrderWeb.Client.Models.CustomerSearchRequest(view.OrderType, term, term, term);
        var motherResults = await _customerClient.SearchCustomersAsync(request);
        // A Client stores only customers assigned to an active order. The cache
        // is a limited fallback, never a copied restaurant customer directory.
        var results = motherResults.Count > 0
            ? motherResults
            : await _cache.SearchCachedCustomersAsync(new OrderWeb.Client.Models.CustomerSearchRequest(view.OrderType, term, term, term));
        var presentation = results
            .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
            .Select(group => group.First())
            .Take(10)
            .Select(customer => new CustomerPresentation(customer.MotherId, customer.Name, customer.Phone, customer.Email, customer.Address, customer.Postcode, "Mother-authorized result"))
            .ToList();
        view.SetResults(presentation);
        view.SetStatus(presentation.Count == 0 ? "No matching customer found. Enter details to continue." : $"{presentation.Count} customer result(s) found.");
    }

    private async Task SubmitSharedCustomerFlowAsync(CustomerFlowView view, CustomerFlowSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Customer.Name) || string.IsNullOrWhiteSpace(submission.Customer.Phone))
        {
            view.SetStatus("Customer name and phone are required.");
            return;
        }

        var online = await new ClientOfflinePolicy(_cache).IsMotherOnlineAsync();
        var saveOp = IsCustomerHubOrderType(submission.OrderType)
            ? ClientOperation.SaveCollectionOrder
            : ClientOperation.SubmitFinalOrder;
        var decision = new ClientOfflinePolicy(_cache).Evaluate(saveOp, online);
        if (!decision.Allowed)
        {
            view.SetStatus(decision.Message);
            return;
        }

        view.SetLoading(true, "Saving customer with Mother POS…");
        try
        {
            var cached = new CachedCustomer(0, submission.Customer.Id, submission.Customer.Name, submission.Customer.Phone,
                submission.Customer.Email, submission.Address ?? string.Empty, submission.Postcode, 0);
            var draft = new CustomerOrderDraft(submission.OrderType, cached,
                submission.OrderType == "Collection" ? submission.PickupOrDeliveryTime : null,
                submission.OrderType == "Delivery" ? submission.PickupOrDeliveryTime : null,
                submission.Notes, submission.Address, submission.Postcode, null, 0m);
            var saved = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomerForActiveOrderAsync(saved, isDelivery: submission.OrderType == "Delivery");

            _pendingCustomerOrderId ??= Guid.NewGuid().ToString("N");
            var result = await _orderClient.CreateCustomerOrderAsync(
                draft with { Customer = saved },
                _currentSession,
                _pendingCustomerOrderId);
            await ApplyMotherOrderResultAsync(result);
            _pendingCustomerOrderId = null;
            view.SetStatus("Mother confirmed the customer and opened the order.");
            ShowOrder();
        }
        catch (Exception ex)
        {
            view.SetStatus($"Mother could not save this customer: {ex.Message}");
        }
    }

    private void ShowCustomerForm(string title)
    {
        if (title.StartsWith("Delivery", StringComparison.OrdinalIgnoreCase))
        {
            _posSelectedMenu = "Delivery";
            _ = ClientSideNavigation.PushFromSideAsync(Navigation, new Pages.Orders.DeliveryOrderPage());
            return;
        }

        _posSelectedMenu = "Collection";
        _ = ClientSideNavigation.PushFromSideAsync(Navigation, new Pages.Orders.CollectionOrderPage());
    }

    // Retained only as a rollback implementation while the shared payment route
    // is verified. Normal navigation calls ShowPayment below.
    private void ShowLegacyPayment()
    {
        Root.Children.Clear();
        var total = _currentOrder?.Total ?? 0m;
        var selectedMethod = "Cash";
        var tenderedEntry = new Entry
        {
            Placeholder = "0.00",
            Text = total.ToString("F2"),
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.Transparent,
            FontFamily = "OpenSansRegular",
            FontSize = 15,
            HeightRequest = 50
        };
        var changeLabel = new Label
        {
            Text = Money(0),
            FontFamily = "OpenSansBold",
            FontSize = 18,
            TextColor = Color.FromArgb(Success),
            VerticalTextAlignment = TextAlignment.Center
        };
        var methodLabel = new Label
        {
            Text = "Method: Cash",
            FontFamily = "OpenSansRegular",
            FontSize = 14,
            TextColor = Color.FromArgb(MutedText)
        };
        var receiptCheck = new CheckBox { IsChecked = true, Color = Color.FromArgb(PrimaryAction) };
        var printStatus = new PrintStatusView();
        var methodButtons = new List<Button>();

        void UpdateChange()
        {
            var tendered = decimal.TryParse(tenderedEntry.Text, out var parsed) ? parsed : 0m;
            changeLabel.Text = Money(Math.Max(0m, tendered - total));
        }

        Button PaymentMethodButton(string method)
        {
            var button = new Button
            {
                Text = method,
                FontFamily = "OpenSansSemibold",
                FontSize = 17,
                CornerRadius = 8,
                HeightRequest = 60
            };
            button.Clicked += (_, _) =>
            {
                selectedMethod = method;
                methodLabel.Text = $"Method: {method}";
                foreach (var item in methodButtons)
                {
                    item.BackgroundColor = Color.FromArgb(item.Text == method ? PrimaryAction : "#F1F5F9");
                    item.TextColor = item.Text == method ? Color.FromArgb(PageBackground) : Color.FromArgb("#334155");
                }
                tenderedEntry.Text = total.ToString("F2");
                UpdateChange();
            };
            methodButtons.Add(button);
            return button;
        }

        var cashButton = PaymentMethodButton("Cash");
        var cardButton = PaymentMethodButton("Card");
        var giftButton = PaymentMethodButton("Gift Card");
        var splitButton = PaymentMethodButton("Split");
        cashButton.BackgroundColor = Color.FromArgb(PrimaryAction);
        cashButton.TextColor = Color.FromArgb(PageBackground);
        foreach (var button in methodButtons.Where(button => button != cashButton))
        {
            button.BackgroundColor = Color.FromArgb("#F1F5F9");
            button.TextColor = Color.FromArgb("#334155");
        }

        tenderedEntry.TextChanged += (_, _) => UpdateChange();

        var methodGrid = new Grid
        {
            RowDefinitions = { new RowDefinition(60), new RowDefinition(60) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowSpacing = 12,
            ColumnSpacing = 12,
            Children = { cashButton, cardButton, giftButton, splitButton }
        };
        SetColumn(cardButton, 1);
        SetRow(giftButton, 1);
        SetRow(splitButton, 1);
        SetColumn(splitButton, 1);

        var tenderGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 14,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = "Amount Paid", FontFamily = "OpenSansRegular", FontSize = 14, TextColor = Color.FromArgb(SecondaryText) },
                        new Border
                        {
                            BackgroundColor = Color.FromArgb(PageBackground),
                            Stroke = Color.FromArgb("#CBD5E1"),
                            StrokeShape = new RoundRectangle { CornerRadius = 8 },
                            Padding = new Thickness(12, 0),
                            Content = tenderedEntry
                        }
                    }
                },
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = "Change Due", FontFamily = "OpenSansRegular", FontSize = 14, TextColor = Color.FromArgb(SecondaryText) },
                        new Border
                        {
                            BackgroundColor = Color.FromArgb(PageBackground),
                            Stroke = Color.FromArgb(BorderLight),
                            StrokeShape = new RoundRectangle { CornerRadius = 8 },
                            Padding = new Thickness(12, 0),
                            HeightRequest = 52,
                            Content = changeLabel
                        }
                    }
                }
            }
        };
        SetColumn(tenderGrid.Children[1], 1);

        var paymentPanel = new Border
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 22,
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 4,
                        Children =
                        {
                            new Label { Text = "Total Due", FontFamily = "OpenSansRegular", FontSize = 14, TextColor = Color.FromArgb(MutedText) },
                            new Label { Text = Money(total), FontFamily = "OpenSansBold", FontSize = 36, TextColor = Color.FromArgb(MainText) }
                        }
                    },
                    methodGrid,
                    tenderGrid,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            receiptCheck,
                            new Label { Text = "Print receipt", FontFamily = "OpenSansRegular", FontSize = 14, TextColor = Color.FromArgb(SecondaryText), VerticalTextAlignment = TextAlignment.Center }
                        }
                    },
                    PrimaryButton("Confirm Payment", PrimaryAction, async (_, _) =>
                    {
                        printStatus.SetStatus(receiptCheck.IsChecked ? "queued" : "sent", receiptCheck.IsChecked ? "Queued by Mother" : "Sent to kitchen");
                        await CompletePaymentAsync(selectedMethod.ToLowerInvariant());
                    }),
                    OutlineButton("Back to Order", (_, _) => ShowOrder())
                }
            }
        };

        var printPanel = new Border
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 18,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Mother Printing", FontFamily = "OpenSansSemibold", FontSize = 20, TextColor = Color.FromArgb(MainText) },
                    printStatus,
                    methodLabel
                }
            }
        };

        var content = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(360) },
            ColumnSpacing = 22,
            MaximumWidthRequest = 980,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                paymentPanel,
                printPanel
            }
        };
        SetColumn(printPanel, 1);
        Root.Children.Add(AppFrame("Payment", content, false));
    }

    private void ShowPayment() => ShowSharedPayment();

    private async void ShowSharedPayment()
    {
        if (_currentOrder is null)
        {
            ShowToast("Payment requires a Mother-confirmed order.");
            return;
        }

        if (IsCustomerHubOrderType(_currentOrder.OrderType))
        {
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            if (!online)
            {
                ShowToast("Taking payment for this order requires Mother POS.");
                return;
            }
        }

        if (_currentSession?.HasPermission(OrderWeb.Contracts.Capabilities.PosCapabilityKeys.TakePayments) != true)
        {
            ShowToast("Your role cannot take payments on this Client.");
            return;
        }

        var paymentService = new ClientPaymentService(_cache);
        var viewModel = new PaymentViewModel { AmountDue = _currentOrder.Total };
        var paymentView = new PaymentView { ViewModel = viewModel };
        paymentView.SubmissionRequested += async (_, submission) =>
        {
            if (_currentOrder is null)
            {
                viewModel.ApplyAuthoritativeResult(false, "Payment requires a Mother-confirmed order.", false);
                return;
            }

            var result = await paymentService.TakePaymentAsync(
                _currentOrder.OrderId,
                submission.Method,
                submission.Amount,
                submission.RequestId,
                expectedOrderRevision: _currentOrder.Version,
                correlationId: submission.CorrelationId);
            viewModel.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
            if (result.Approved)
            {
                try
                {
                    var refreshed = await _orderClient.OpenOrderForEditAsync(_currentOrder.OrderId);
                    await ApplyMotherOrderResultAsync(refreshed);
                }
                catch
                {
                    // Payment already succeeded; refresh is best-effort.
                }

                if (submission.PrintReceipt)
                {
                    await RequestPrintAsync("customer receipt", _currentOrder.OrderId, false);
                }
            }
        };
        viewModel.StatusCheckRequested += async (_, requestId) =>
        {
            var result = await paymentService.GetPaymentStatusAsync(requestId);
            viewModel.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
        };

        Root.Children.Clear();
        Root.Children.Add(SharedAppFrame("Payment", paymentView, "payments"));
    }

    private async Task CompletePaymentAsync(string method)
    {
        var policy = new ClientOfflinePolicy(_cache);
        var operation = string.Equals(method, "card", StringComparison.OrdinalIgnoreCase)
            ? ClientOperation.CardPayment
            : ClientOperation.SubmitFinalOrder;
        var decision = policy.Evaluate(operation, await policy.IsMotherOnlineAsync());
        if (!decision.Allowed)
        {
            ShowToast(decision.Message);
            return;
        }

        // This legacy dashboard has no authoritative order/payment ID. It may
        // not queue or display a local payment as successful.
        ShowToast("Payment requires Mother confirmation. No payment has been taken.");
    }

    private async Task QueueClientActionAsync(string actionType, string message)
    {
        if (!string.Equals(actionType, "unsent_draft", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("This action requires Mother POS confirmation and was not queued locally.");
            return;
        }

        await _cache.QueuePendingActionAsync(actionType, new
        {
            selectedTable = _currentOrder?.TableNumber ?? _selectedCachedTable?.TableNumber,
            guests = _guests,
            orderId = _currentOrder?.OrderId,
            lines = CurrentOrderPayloadLines(),
            queuedAtUtc = DateTimeOffset.UtcNow
        });
        _cacheStatus = await _cache.GetStatusAsync();
        ShowToast(message);
    }

    private IReadOnlyList<object> CurrentOrderPayloadLines()
    {
        return _currentOrder != null
            ? _currentOrder.Lines.Select(line => (object)line).ToList()
            : Array.Empty<object>();
    }

    private void ShowLiveOrders() => ShowLiveOrders(_liveOrderFilter);

    private async void ShowLiveOrders(string selectedFilter)
    {
        _posSelectedMenu = "Live Order";
        _isViewingOrderScreen = false;
        selectedFilter = NormalizeLiveOrderType(selectedFilter);
        _liveOrderFilter = selectedFilter;
        Root.Children.Clear();

        var motherOrders = await _orderClient.GetOpenOrdersAsync();
        if (motherOrders != null)
        {
            await _cache.ReplaceOperationalOrdersAsync(motherOrders);
        }

        var openOrders = await _cache.GetOpenOrderStatesAsync();
        var cards = BuildLiveOrderCards(openOrders)
            .Where(card => selectedFilter == "All" || card.Type == selectedFilter)
            .ToList();

        var page = new Grid
        {
            BackgroundColor = Color.FromArgb(LightSurface),
            RowDefinitions =
            {
                new RowDefinition(150),
                new RowDefinition(98),
                new RowDefinition(GridLength.Star)
            }
        };

        page.Children.Add(RestaurantHeader("Live Order"));

        var filters = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                LiveOrderFilterButton("All", selectedFilter),
                LiveOrderFilterButton("Collection", selectedFilter),
                LiveOrderFilterButton("Delivery", selectedFilter),
                LiveOrderFilterButton("Table", selectedFilter)
            }
        };

        var filterBar = new Border
        {
            BackgroundColor = Color.FromArgb(PageBackground),
            Stroke = Color.FromArgb(BorderLight),
            StrokeThickness = 1,
            Content = filters
        };
        page.Children.Add(filterBar);
        SetRow(filterBar, 1);

        if (cards.Count == 0)
        {
            var emptyText = selectedFilter switch
            {
                "Collection" => "No unpaid collection orders",
                "Delivery" => "No unpaid delivery orders",
                "Table" => "No active table sessions",
                _ => "No open local orders"
            };

            var empty = new Label
            {
                Text = emptyText,
                FontSize = 16,
                TextColor = Color.FromArgb("#9CA3AF"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 220, 0, 0)
            };
            page.Children.Add(empty);
            SetRow(empty, 2);
            Root.Children.Add(page);
            return;
        }

        var orderGrid = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Start,
            JustifyContent = FlexJustify.Start,
            Padding = new Thickness(24, 36, 24, 24)
        };

        foreach (var card in cards)
        {
            orderGrid.Children.Add(LiveOrderCardView(card));
        }

        var scroll = new ScrollView { Content = orderGrid };
        page.Children.Add(scroll);
        SetRow(scroll, 2);
        Root.Children.Add(page);
    }

    private Button LiveOrderFilterButton(string text, string selectedFilter)
    {
        var selected = NormalizeLiveOrderType(text) == selectedFilter;
        var button = new Button
        {
            Text = text,
            FontSize = 15,
            FontFamily = "OpenSansBold",
            TextColor = selected ? Colors.White : Color.FromArgb(MutedText),
            BackgroundColor = selected ? Color.FromArgb("#10B981") : Color.FromArgb("#F5F5F5"),
            CornerRadius = 8,
            HeightRequest = 50,
            WidthRequest = text == "All" ? 84 : 132,
            Padding = new Thickness(0),
            BorderWidth = 0
        };
        button.Clicked += (_, _) => ShowLiveOrders(text);
        return button;
    }

    private IReadOnlyList<LiveOrderCardModel> BuildLiveOrderCards(
        IReadOnlyList<MotherOrderState> openOrders)
    {
        var cards = new List<LiveOrderCardModel>();

        foreach (var order in openOrders.Where(order => !string.Equals(order.Status, "Closed", StringComparison.OrdinalIgnoreCase)))
        {
            var type = NormalizeLiveOrderType(order.OrderType);
            var tableDisplay = !string.IsNullOrWhiteSpace(order.TableNumber) ? $"Table {order.TableNumber}" : "Table";
            var subtitle = type == "Table" ? tableDisplay : order.Status;
            var accent = type == "Table" ? Danger : "#10B981";

            cards.Add(new LiveOrderCardModel(
                Type: type,
                Title: type,
                OrderNumber: FormatLiveOrderNumber(order.OrderNumber, order.OrderId),
                Subtitle: subtitle,
                Total: order.Total,
                TimeText: FormatLiveOrderTime(order.UpdatedUtc),
                AccentColor: accent,
                OpenAsync: async () =>
                {
                    if (type is "Collection" or "Delivery" or "Table")
                    {
                        var decision = _offlinePolicy.Evaluate(
                            ClientOperation.OpenCollectionOrder,
                            await _offlinePolicy.IsMotherOnlineAsync());
                        if (!decision.Allowed)
                        {
                            ShowToast(decision.Message);
                            return;
                        }

                        try
                        {
                            _connectionStatus = "Syncing";
                            var opened = await _orderClient.OpenOrderForEditAsync(order.OrderId);
                            _currentOrder = opened.State;
                            _selectedCachedTable = opened.State.TableId.HasValue
                                ? _cachedFloors.SelectMany(floor => floor.Tables)
                                    .FirstOrDefault(table => table.Id == opened.State.TableId.Value)
                                : null;
                            _guests = Math.Max(_currentOrder.Guests, 1);
                            await _cache.SaveOrderStateAsync(_currentOrder);
                            await LoadOrderMenuAsync();
                            _connectionStatus = "Connected";
                            ShowToast(opened.Message);
                            ShowOrder();
                        }
                        catch (Exception ex)
                        {
                            _connectionStatus = "Connected";
                            ShowToast(ex.Message);
                        }

                        return;
                    }

                    _currentOrder = order;
                    _selectedCachedTable = order.TableId.HasValue
                        ? _cachedFloors.SelectMany(floor => floor.Tables).FirstOrDefault(table => table.Id == order.TableId.Value)
                        : null;
                    _guests = Math.Max(order.Guests, 1);
                    await _cache.SaveOrderStateAsync(order);
                    await LoadOrderMenuAsync();
                    ShowOrder();
                }));
        }

        return cards
            .OrderByDescending(card => card.Type == "Table")
            .ThenBy(card => card.TimeText)
            .ToList();
    }

    private Border LiveOrderCardView(LiveOrderCardModel card)
    {
        var accent = Color.FromArgb(card.AccentColor);
        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = accent,
            StrokeThickness = 2,
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 14, 14),
            WidthRequest = 220,
            MinimumHeightRequest = 132,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.08f
            }
        };

        border.Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = card.Title,
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1E293B"),
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = card.OrderNumber,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8"),
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = card.Subtitle,
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2,
                    Margin = new Thickness(0, 2, 0, 0)
                },
                new Label
                {
                    Text = Money(card.Total),
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = accent,
                    Margin = new Thickness(0, 6, 0, 0)
                },
                new Label
                {
                    Text = card.TimeText,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8")
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await card.OpenAsync();
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static string NormalizeLiveOrderType(string? orderType)
    {
        return (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "all" => "All",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Collection"
        };
    }

    private static string FormatLiveOrderNumber(string? orderNumber, string? fallbackId)
    {
        var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : fallbackId?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Order";
        }

        return value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
    }

    private static string FormatLiveOrderTime(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var timestamp))
        {
            return timestamp.ToLocalTime().ToString("HH:mm · dd/MM");
        }

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private sealed record LiveOrderCardModel(
        string Type,
        string Title,
        string OrderNumber,
        string Subtitle,
        decimal Total,
        string TimeText,
        string AccentColor,
        Func<Task> OpenAsync);

    private View AppFrame(string title, View content, bool showSidebar)
    {
        return SharedAppFrame(title, content, RouteForTitle(title));
    }

    private bool UsesPosShell => _currentSession?.Role is "User" or "Manager";

    private View PosPageFrame(string title, View content)
    {
        return SharedAppFrame(title, content, RouteForTitle(title));
    }

    private ApplicationShellFrame SharedAppFrame(string title, View content, string selectedRoute)
    {
        var isDashboard = title.Contains("Dashboard", StringComparison.OrdinalIgnoreCase);
        var role = _currentSession?.Role;
        var capabilities = ClientCapabilityResolver.ForRole(role, _currentSession?.Permissions);
        var features = ClientHostAccess.FeaturesForRole(role);
        var routes = ClientHostAccess.RoutesForRole(role);
        var frame = new ApplicationShellFrame
        {
            PageTitle = title,
            MainContent = content,
            RestaurantName = CurrentRestaurantName(),
            RestaurantLogo = "companymark.png",
            UserName = _currentSession?.UserName ?? "No user",
            UserRole = role ?? "User",
            TerminalName = LastTerminalName(),
            ConnectionStatus = _connectionStatus,
            SelectedRoute = selectedRoute,
            AvailableCapabilities = capabilities,
            AvailableFeatures = features,
            MenuItems = OrderWeb.SharedUI.Navigation.PosNavigationCatalog.Filter(capabilities, features, routes),
            ShowUpdateButton = string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase),
            ShowWelcomeBrand = isDashboard,
            ShowIdentity = !isDashboard,
            ShowConnection = !isDashboard,
            ShowMinimize = true
        };
        frame.NavigationRequested += (_, e) => OnClientSidebarMenuSelected(frame, e.Item.Title);
        frame.LogoutRequested += (_, _) => Logout();
        frame.MinimizeRequested += (_, _) => ClientWindowService.MinimizeMainWindow();
        frame.UpdateRequested += OnClientSidebarUpdateAllClicked;
        frame.ToastRetryRequested += (_, _) => RefreshCurrentPosPage();
        frame.RetryRequested += (_, _) => RefreshCurrentPosPage();
        _activeApplicationFrame = frame;
        return frame;
    }

    private string DashboardTitle() => "Dashboard";

    private bool IsManagerRole() =>
        string.Equals(_currentSession?.Role, "Manager", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<ApplicationNavigationItem> ClientNavigationItems() =>
    [
        new("dashboard", "Dashboard", "dashboard.png", "User", "Manager"),
        new("cashdrawer", "Cash Drawer", "giftcard.png", "Manager"),
        new("liveorder", "Live Order", "liveorder.png", "User", "Manager"),
        new("restaurant", "Restaurant", "restaurant.png", "User", "Manager"),
        new("collection", "Collection", "collection.png", "User", "Manager"),
        new("delivery", "Delivery", "delivery.png", "User", "Manager"),
        new("giftcards", "Gift Cards", "giftcards.png", "Manager"),
        new("loyalty", "Loyalty Points", "loyalty.png", "Manager"),
        new("reservation", "Reservation", "reservation.png", "User", "Manager"),
        new("orderhistory", "Order History", "orderhistory.png", "Manager")
    ];

    private static string RouteForTitle(string title) => title.Trim().ToLowerInvariant() switch
    {
        "manager dashboard" => "dashboard",
        "cash drawer" => "cashdrawer",
        "live order" => "liveorder",
        "restaurant" => "restaurant",
        "collection" => "collection",
        "delivery" => "delivery",
        "gift cards" => "giftcards",
        "loyalty points" => "loyalty",
        "reservation" => "reservation",
        "order history" => "orderhistory",
        "payment" => "payment",
        _ => "dashboard"
    };

    private View Sidebar()
    {
        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(24, 36),
            Spacing = 28,
            BackgroundColor = Color.FromArgb(PageBackground),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "OW", FontSize = 38, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(AccentBlue), HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = "Order Web", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E3A8A"), HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = "Restaurant Management", FontSize = 15, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center }
                    }
                },
                SidebarItem("Dashboard", "dashboard.png", (_, _) => ShowDashboard()),
                SidebarItem("Cash Drawer", "giftcard.png", (_, _) => OpenManagerToolPage(new CashDrawerPage())),
                SidebarItem("Live Order", "liveorder.png", (_, _) => ShowLiveOrders()),
                SidebarItem("Restaurant", "restaurant.png", (_, _) => ShowRestaurantLayout()),
                SidebarItem("Collection", "collection.png", (_, _) => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage())),
                SidebarItem("Delivery", "delivery.png", (_, _) => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage())),
                SidebarItem("Reservation", "reservation.png", (_, _) => OpenManagerToolPage(new ReservationPage())),
                new BoxView { VerticalOptions = LayoutOptions.Fill }
            }
        };
        return stack;
    }

    private View SidebarItem(string text, string icon, EventHandler click)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => click(this, EventArgs.Empty);
        var row = new HorizontalStackLayout
        {
            Spacing = 22,
            Padding = new Thickness(8, 6),
            Children =
            {
                new Image { Source = icon, WidthRequest = SidebarIconSize, HeightRequest = SidebarIconSize, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center },
                new Label { Text = text, FontSize = 16, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(SidebarText), VerticalTextAlignment = TextAlignment.Center }
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private View MenuButton()
    {
        var button = new ImageButton
        {
            Source = "mian.png",
            WidthRequest = HeaderMenuIconSize,
            HeightRequest = HeaderMenuIconSize,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit
        };
        button.Clicked += (_, _) => ShowDashboard();
        return button;
    }

    private View HeaderClock(bool compact)
    {
        _useLoginClockFormat = false;
        _dateLabel = new Label { FontSize = compact ? 17 : 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.End };
        _timeLabel = new Label { FontSize = compact ? 31 : 24, TextColor = Color.FromArgb(AccentBlue), HorizontalTextAlignment = TextAlignment.End };
        UpdateClock();
        var logout = new ImageButton
        {
            Source = "outred.png",
            WidthRequest = compact ? 62 : 40,
            HeightRequest = compact ? 62 : 40,
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(0, compact ? 16 : 10, 0, 0),
            Padding = 0,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End
        };
        logout.Clicked += (_, _) => Logout();

        return new VerticalStackLayout
        {
            Spacing = 5,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _dateLabel,
                _timeLabel,
                logout
            }
        };
    }

    private View DashboardClock()
    {
        _useLoginClockFormat = false;
        _dateLabel = new Label { FontSize = 13, FontFamily = "InterMedium", TextColor = Color.FromArgb("#374151"), HorizontalTextAlignment = TextAlignment.End };
        _timeLabel = new Label { FontSize = 18, FontFamily = "InterBold", TextColor = Color.FromArgb(AccentBlue), HorizontalTextAlignment = TextAlignment.End };
        UpdateClock();

        var logoutButton = new ImageButton
        {
            Source = "outred.png",
            BackgroundColor = Colors.Transparent,
            WidthRequest = HeaderLogoutIconSize,
            HeightRequest = HeaderLogoutIconSize,
            Aspect = Aspect.AspectFit,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        logoutButton.Clicked += (_, _) => Logout();

        var clockStack = new VerticalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _dateLabel,
                _timeLabel
            }
        };

        var headerRight = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(58)
            },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                clockStack,
                logoutButton
            }
        };
        SetColumn(logoutButton, 1);

        return headerRight;
    }

    private View LoginClock()
    {
        _useLoginClockFormat = true;
        _timeLabel = new Label { FontSize = 24, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(PrimaryAction), HorizontalTextAlignment = TextAlignment.End };
        _dateLabel = new Label { FontSize = 13, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb(SecondaryText), HorizontalTextAlignment = TextAlignment.End };
        UpdateClock();

        return new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 20, 0, 0),
            Children =
            {
                _timeLabel,
                _dateLabel
            }
        };
    }

    private Button LoginClockButton()
    {
        var button = new Button
        {
            Text = "Clock In/Out",
            BackgroundColor = Color.FromArgb(PrimaryAction),
            TextColor = Color.FromArgb(PageBackground),
            FontSize = 22,
            FontFamily = "OpenSansRegular",
            CornerRadius = 29,
            HeightRequest = 58,
            WidthRequest = 334,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromArgb(PrimaryAction)), Opacity = 0.32f, Radius = 16, Offset = new Point(0, 7) }
        };
        button.Clicked += (_, _) => ShowClockTimeModal();
        return button;
    }

    private View ConnectionStatusPill()
    {
        return new ConnectionStatusView
        {
            Status = _connectionStatus,
            HorizontalOptions = LayoutOptions.Center
        };
    }

    private void UpdateClock()
    {
        if (_timeLabel == null || _dateLabel == null)
        {
            return;
        }

        if (_useLoginClockFormat)
        {
            _dateLabel.Text = DateTime.Now.ToString("dddd, MMM d, yyyy");
            _timeLabel.Text = DateTime.Now.ToString("h:mm tt").ToLowerInvariant();
            return;
        }

        _dateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        _timeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private View Field(string label, string placeholder)
    {
        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = label, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(SecondaryText) },
                new Entry { Placeholder = placeholder, FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground) }
            }
        };
    }

    private View Field(string label, Entry entry)
    {
        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = label, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(SecondaryText) },
                entry
            }
        };
    }

    private Button PrimaryButton(string text, string color, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Color.FromArgb(PageBackground),
            FontSize = 18,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 12,
            HeightRequest = 58
        };
        button.Clicked += click;
        return button;
    }

    private Button OutlineButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(PageBackground),
            BorderColor = Color.FromArgb("#CBD5E1"),
            BorderWidth = 1,
            TextColor = Color.FromArgb(SecondaryText),
            FontSize = 18,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 12,
            HeightRequest = 58
        };
        button.Clicked += click;
        return button;
    }

    private Button PillButton(string text, string color, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb(color),
            TextColor = color == "#F8FAFC" ? Color.FromArgb(SecondaryText) : Color.FromArgb(PageBackground),
            CornerRadius = 22,
            HeightRequest = 46,
            Padding = new Thickness(22, 0)
        };
        button.Clicked += click;
        return button;
    }

    private View StatusPill(string text, string color)
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb(color),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(14, 6),
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label { Text = text, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(PageBackground), HorizontalTextAlignment = TextAlignment.Center }
        };
    }

    private View CacheStatusPanel()
    {
        var status = _cacheStatus;
        var text = status == null
            ? "SQLite cache: initializing..."
            : $"SQLite cache v{status.SchemaVersion ?? "?"}: {status.Categories} categories, {status.Products} products, {status.Tables} tables, {status.OpenOrders} open local orders, {status.PendingActions} pending actions.";

        var checkpoint = status == null
            ? "Mother is still the source of truth."
            : $"Last bootstrap: {FormatCacheTime(status.LastBootstrapTime)} | Last sync: {FormatCacheTime(status.LastSyncTime)} | Last event: {status.LastEventId ?? "-"}";

        return new Border
        {
            Stroke = Color.FromArgb(BorderLight),
            BackgroundColor = Color.FromArgb(LightSurface),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 14,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = text, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(SecondaryText) },
                    new Label { Text = checkpoint, FontSize = 12, TextColor = Color.FromArgb(MutedText) },
                    new Label { Text = "Safety: local SQLite is cache only. Final writes must go to Mother API.", FontSize = 12, TextColor = Color.FromArgb("#B45309") }
                }
            }
        };
    }

    private static string FormatCacheTime(string? value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("HH:mm:ss")
            : "-";
    }

    private void ShowToast(string message)
    {
        if (_activeApplicationFrame is { } frame)
        {
            frame.ToastTitle = CurrentRestaurantName();
            frame.ToastMessage = message;
            frame.ToastKind = message.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                              message.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
                              message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                ? StatusKind.Warning
                : StatusKind.Info;
            frame.IsToastRetryVisible = message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                                        message.Contains("offline", StringComparison.OrdinalIgnoreCase);
            frame.IsToastVisible = true;
            return;
        }

        _ = DisplayAlert(CurrentRestaurantName(), message, "OK");
    }

    private static string Money(decimal value) => $"\u00A3{value:F2}";

    private static string IconText(string key) => key switch
    {
        "table" => "┬",
        "truck" => "▰",
        "bag" => "▢",
        "live" => "◉",
        "grid" => "▦",
        "cash" => "\u00A3",
        "live-small" => "◉",
        "table-small" => "┬",
        "bag-small" => "▢",
        "truck-small" => "▰",
        "calendar" => "▣",
        "logout" => "↩",
        "restaurant-large" => "♜",
        "delivery-large" => "▱",
        "collection-large" => "▢",
        "live-large" => "◉",
        "discount" => "%",
        "void" => "!",
        "gift" => "□",
        "loyalty" => "★",
        "users" => "◫",
        _ => "□"
    };

    private static void SetColumn(IView view, int column)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Grid.ColumnProperty, column);
        }
    }

    private static void SetRow(IView view, int row)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Grid.RowProperty, row);
        }
    }
}
