using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Views.Layout;
using OrderWeb.Client.Views.Printing;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using OrderWeb.SharedUI.Controls;

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
    private readonly MotherBootstrapClient _bootstrapClient;
    private readonly MotherAuthClient _authClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly MotherCustomerClient _customerClient;
    private readonly MotherPrintClient _printClient = new();
    private readonly MotherOnlineOrderClient _onlineOrderClient = new();
    private readonly MotherEventClient _motherEvents;
    private readonly MotherHeartbeatClient _motherHeartbeat;
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
    private string _pin = string.Empty;
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
    private VisualElement? _posSidebarView;
    private Grid? _posSidebarOverlay;
    private Entry? _motherIpEntry;
    private Entry? _pairingCodeEntry;
    private Entry? _terminalNameEntry;
    private BootstrapRequest? _lastBootstrapRequest;
    private bool _bootstrapInProgress;
    private readonly IDispatcherTimer _clockTimer;
    private ApplicationShellFrame? _activeApplicationFrame;

    public MainPage()
    {
        InitializeComponent();
        _bootstrapClient = new MotherBootstrapClient();
        _customerClient = new MotherCustomerClient();
        _motherEvents = new MotherEventClient(_cache);
        _motherHeartbeat = new MotherHeartbeatClient(_cache, () => _currentSession);
        _motherEvents.TerminalControlReceived += OnMotherTerminalControlReceived;
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        ShowConnect();
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

        var hasPairedMother = await HasPairedMotherAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!hasPairedMother)
            {
                ShowConnect();
                return;
            }

            ShowLogin();
        });
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

    private void ShowConnect()
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        _motherIpEntry = new Entry { Placeholder = "Mother POS IP address", Text = Preferences.Get(LastMotherIpKey, string.Empty), FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground) };
        _pairingCodeEntry = new Entry { Placeholder = "6-digit code from Mother POS", Text = Preferences.Get(LastPairingCodeKey, string.Empty), FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground), Keyboard = Keyboard.Numeric };
        _terminalNameEntry = new Entry { Placeholder = "Terminal name", Text = LastTerminalName(), FontSize = 18, HeightRequest = 58, BackgroundColor = Color.FromArgb(PageBackground) };

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
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    new Label { Text = "OrderWeb Client POS", FontSize = 38, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Connect this device to the Mother POS.", FontSize = 17, TextColor = Color.FromArgb(MutedText), HorizontalTextAlignment = TextAlignment.Center },
                    Field("Mother IP Address", _motherIpEntry),
                    Field("Pairing Code", _pairingCodeEntry),
                    Field("Terminal Name", _terminalNameEntry),
                    ClientDeviceIdPanel(),
                    PrimaryButton("Connect & Bootstrap", PrimaryAction, async (_, _) => await StartBootstrapAsync()),
                    CacheStatusPanel(),
                    new Label { Text = "SQLite cache is used for offline continuity after pairing.", FontSize = 13, TextColor = Color.FromArgb("#94A3B8"), HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };

        Root.Children.Add(panel);
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
        }
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };

        var left = new Grid
        {
            BackgroundColor = Color.FromArgb("#F8F9FA"),
            Padding = new Thickness(48, 40),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 58,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "POS", FontSize = 58, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb(PrimaryAction), HorizontalTextAlignment = TextAlignment.Center },
                        new VerticalStackLayout
                        {
                            Spacing = 10,
                            Margin = new Thickness(0, 20, 0, 0),
                            HorizontalOptions = LayoutOptions.Center,
                            Children =
                            {
                                new Label { Text = "Welcome Back!", FontSize = 32, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(MainText), HorizontalTextAlignment = TextAlignment.Center },
                                new Label { Text = CurrentRestaurantName(), FontSize = 24, FontFamily = "OpenSansBold", TextColor = Color.FromArgb(MainText), Opacity = 0.72, HorizontalTextAlignment = TextAlignment.Center, MaximumWidthRequest = 300 }
                            }
                        }
                    }
                }
            }
        };

        var rightStack = new VerticalStackLayout
        {
            Spacing = 46,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 430
        };

        var pinHeader = new VerticalStackLayout
        {
            Spacing = 28,
            HorizontalOptions = LayoutOptions.Center
        };
        pinHeader.Children.Add(new Label
        {
            Text = "Enter your PIN or swipe employee card",
            FontSize = 18,
            FontFamily = "OpenSansRegular",
            TextColor = Color.FromArgb(SecondaryText),
            HorizontalTextAlignment = TextAlignment.Center
        });
        _statusLabel = new Label
        {
            IsVisible = false,
            FontSize = 16,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb(Danger),
            HorizontalTextAlignment = TextAlignment.Center
        };
        pinHeader.Children.Add(_statusLabel);
        pinHeader.Children.Add(BuildPinDots());
        rightStack.Children.Add(pinHeader);
        rightStack.Children.Add(new VerticalStackLayout
        {
            Spacing = 36,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                BuildKeypad(),
                LoginClockButton()
            }
        });

        var loginScroll = new ScrollView
        {
            Content = rightStack,
            VerticalOptions = LayoutOptions.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };

        var right = new Grid
        {
            BackgroundColor = Color.FromArgb(PageBackground),
            Padding = new Thickness(44, 34),
            Children =
            {
                loginScroll,
                LoginClock()
            }
        };

        layout.Children.Add(left);
        layout.Children.Add(right);
        SetColumn(layout.Children[1], 1);

        var minimizeButton = new Button
        {
            Text = "-",
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            CornerRadius = 0,
            BackgroundColor = Colors.Transparent,
            BorderColor = Colors.Transparent,
            BorderWidth = 0,
            TextColor = Color.FromArgb("#111827"),
            FontSize = 24,
            FontFamily = "OpenSansBold",
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 14, 18, 0),
            ZIndex = 10
        };
        minimizeButton.Clicked += (_, _) => ClientWindowService.MinimizeMainWindow();
        layout.Children.Add(minimizeButton);
        Grid.SetColumnSpan(minimizeButton, 2);

        Root.Children.Add(layout);
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
            if (session.Role == "Staff")
            {
                _pin = string.Empty;
                ShowLogin(false);
                _statusLabel!.Text = "Staff PIN is for Clock In/Out only.";
                _statusLabel.TextColor = Color.FromArgb(Danger);
                _statusLabel.IsVisible = true;
                return;
            }

            await _cache.SaveLoginSessionAsync(session);
            _currentSession = session;
            _pin = string.Empty;
            ShowDashboard();
        }
        catch (LoginException ex)
        {
            _pin = string.Empty;
            ShowLogin(false);
            _statusLabel!.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Wrong PIN" : ex.Message;
            _statusLabel.TextColor = Color.FromArgb(Danger);
            _statusLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            _pin = string.Empty;
            ShowLogin(false);
            _statusLabel!.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Wrong PIN" : ex.Message;
            _statusLabel.TextColor = Color.FromArgb(Danger);
            _statusLabel.IsVisible = true;
        }
    }

    private async void ShowClockTimeModal()
    {
        await Navigation.PushModalAsync(new ClockTimeModal(), false);
    }

    private void ShowDashboard()
    {
        if (_terminalDisabled)
        {
            ShowTerminalDisabled();
            return;
        }

        _posSelectedMenu = "Dashboard";
        Root.Children.Clear();
        Root.BackgroundColor = Color.FromArgb(PageBackground);
        if (_currentSession?.Role == "Staff")
        {
            _currentSession = null;
            ShowLogin();
            _statusLabel!.Text = "Staff PIN is for Clock In/Out only.";
            _statusLabel.TextColor = Color.FromArgb(Danger);
            _statusLabel.IsVisible = true;
            return;
        }

        if (_currentSession?.Role == "User")
        {
            Root.Children.Add(UserDashboard());
            return;
        }

        if (_currentSession?.Role == "Manager")
        {
            Root.Children.Add(ManagerDashboard());
            return;
        }

        Root.Children.Add(AppFrame("Dashboard", DashboardContent(), true));
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

        AddManagerTool(grid, "Web Orders", "weborders.png", 0, 0, ShowLiveOrders);
        AddManagerTool(grid, "Gift Cards", "giftcards.png", 0, 1, () => OpenManagerToolPage(new GiftCardPage()));
        AddManagerTool(grid, "Loyalty Points", "loyalty.png", 0, 2, () => OpenManagerToolPage(new LoyaltyPage()));
        AddManagerTool(grid, "Reservation", "reservation.png", 1, 0, () => OpenManagerToolPage(new ReservationPage()));
        AddManagerTool(grid, "Order History", "orderhistory.png", 1, 1, () => OpenManagerToolPage(new OrderHistoryPage()));
        AddManagerTool(grid, "Cash Drawer", "giftcard.png", 1, 2, () => OpenManagerToolPage(new CashDrawerPage()));
        return grid;
    }

    private async void OpenManagerToolPage(ContentPage page)
    {
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
        switch (_posSelectedMenu)
        {
            case "Restaurant":
                _ = LoadRestaurantLayoutAsync(_selectedCachedFloor?.Id);
                break;
            case "Cash Drawer":
                OpenManagerToolPage(new CashDrawerPage());
                break;
            case "Live Order":
            case "Web Orders":
                ShowLiveOrders();
                break;
            case "Collection":
                OpenManagerToolPage(new Pages.Orders.CollectionOrderPage());
                break;
            case "Delivery":
                OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage());
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
                ConnectionStatus = _connectionStatus,
                MenuItems = ClientNavigationItems(),
                HorizontalOptions = LayoutOptions.Start,
            };
        sidebar.MenuItemSelected += OnClientSidebarMenuSelected;
        sidebar.UpdateAllClicked += OnClientSidebarUpdateAllClicked;
        return sidebar;
    }

    private void OnClientSidebarMenuSelected(object? sender, string selectedMenu) =>
        NavigateClientMenu(RouteForTitle(selectedMenu), selectedMenu);

    private void OnClientNavigationRequested(string route, string title) =>
        NavigateClientMenu(route, title);

    private void NavigateClientMenu(string route, string title)
    {
        switch (route)
        {
            case "dashboard":
                RunFromPosSidebar(title, ShowDashboard);
                break;
            case "cashdrawer":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new CashDrawerPage()));
                break;
            case "liveorder":
            case "weborders":
                RunFromPosSidebar(title, ShowLiveOrders);
                break;
            case "restaurant":
                RunFromPosSidebar(title, ShowRestaurantLayout);
                break;
            case "collection":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new Pages.Orders.CollectionOrderPage()));
                break;
            case "delivery":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new Pages.Orders.DeliveryOrderPage()));
                break;
            case "giftcards":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new GiftCardPage()));
                break;
            case "loyalty":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new LoyaltyPage()));
                break;
            case "reservation":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new ReservationPage()));
                break;
            case "orderhistory":
                RunFromPosSidebar(title, () => OpenManagerToolPage(new OrderHistoryPage()));
                break;
            case "payment":
                RunFromPosSidebar(title, ShowPayment);
                break;
            case "orderentry":
                RunFromPosSidebar(title, ShowRestaurantLayout);
                break;
            case "customers":
            case "report":
            case "settings":
                RunFromPosSidebar(title, () => ShowToast($"{title} is controlled by Mother permissions and is not available on this terminal yet."));
                break;
            default:
                RunFromPosSidebar(title, ShowDashboard);
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
            return userItems;
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
                new PosSidebarMenuItem("Web Orders", "weborders.png", () => RunFromPosSidebar("Web Orders", ShowLiveOrders)),
                new PosSidebarMenuItem("Gift Cards", "giftcards.png", () => RunFromPosSidebar("Gift Cards", () => OpenManagerToolPage(new GiftCardPage()))),
                new PosSidebarMenuItem("Loyalty Points", "loyalty.png", () => RunFromPosSidebar("Loyalty Points", () => OpenManagerToolPage(new LoyaltyPage()))),
                new PosSidebarMenuItem("Reservation", "reservation.png", () => RunFromPosSidebar("Reservation", () => OpenManagerToolPage(new ReservationPage()))),
                new PosSidebarMenuItem("Order History", "orderhistory.png", () => RunFromPosSidebar("Order History", () => OpenManagerToolPage(new OrderHistoryPage())))
            };
        }

        return userItems;
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
            return staffTiles;
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
            return staffTiles.Concat(managerTiles);
        }

        var adminTiles = new[]
        {
            new DashboardTile("Menu Updates", "foodmenu.png", "client.menu.update", () => ShowToast("Client can request/menu-review updates. Master menu setup remains Mother-only.")),
            new DashboardTile("Users/Staff", "customers.png", "client.users_staff", () => ShowToast("Users and staff summary is available from Mother POS.")),
            new DashboardTile("Customers", "customers.png", "client.customers", () => ShowToast("Customer lookup is available.")),
            new DashboardTile("Business Controls", "settings.png", "client.business_controls", () => ShowToast("Business controls are available.")),
            new DashboardTile("Logout", "outred.png", "client.dashboard", Logout)
        };

        if (role == "Admin")
        {
            return adminTiles;
        }

        var superAdminTiles = adminTiles
            .Take(5)
            .Append(new DashboardTile("Business Admin", "settings.png", "client.business_admin", ShowSuperAdminClientPanel))
            .Append(new DashboardTile("Logout", "outred.png", "client.dashboard", Logout));

        return superAdminTiles;
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

    private void ShowSuperAdminClientPanel()
    {
        ShowToast("Super Admin controls stay in Mother POS.");
    }

    private void Logout()
    {
        _currentSession = null;
        _pin = string.Empty;
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
        await LoadRestaurantLayoutAsync(null);
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
        _selectedCachedTable = table;
        _guests = Math.Max(covers, 1);

        if (LegacyOrderEntryAccess.PreferLegacyRollback)
        {
            _connectionStatus = "Syncing";
            var result = await _orderClient.OpenOrCreateTableOrderAsync(table, _guests, _currentSession);
            await ApplyMotherOrderResultAsync(result);
            await LoadOrderMenuAsync();
            _connectionStatus = "Connected";
            ShowOrder();
            return;
        }

        // Step 7: SharedUI order-entry via ClientOrderService → Mother authority.
        await Navigation.PushAsync(SharedOrderEntryPage.ForTableOrLegacy(table, _guests), false);
    }

    private async Task ApplyMotherOrderResultAsync(MotherCommandResult result)
    {
        _currentOrder = result.State;
        await _cache.SaveOrderStateAsync(result.State);
        if (result.ConflictDetected)
        {
            _connectionStatus = "Syncing";
            ShowToast(result.Message);
        }
    }

    private async Task LoadOrderMenuAsync()
    {
        _cachedCategories = await _cache.GetMenuCategoriesAsync();
        _selectedCachedCategory = _cachedCategories.FirstOrDefault();
        if (_selectedCachedCategory != null)
        {
            _cachedProducts = await _cache.GetProductsByCategoryAsync(_selectedCachedCategory.Id);
        }
    }

    private void ShowOrder()
    {
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

        var selectedModifiers = product.ModifierGroups
            .SelectMany(group => group.Modifiers.Take(Math.Min(group.MaxSelect, 1)))
            .Select(modifier => modifier.Name)
            .ToList();

        if (product.ModifierGroups.Count > 0)
        {
            await Navigation.PushModalAsync(new ModifierDialog(), false);
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.AddItemAsync(_currentOrder, product, selectedModifiers);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

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
        var voidButton = ActionButton("VOID", "#EF4444", async (_, _) => await QueueClientActionAsync("void_order", "Void request sent to Mother POS."));
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

        _connectionStatus = "Syncing";
        var result = await _orderClient.RemoveItemAsync(_currentOrder, line);
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private async Task AddNoteToFirstLineAsync()
    {
        if (_currentOrder?.Lines.FirstOrDefault() is not { } line)
        {
            ShowToast("Add an item before adding notes.");
            return;
        }

        await Navigation.PushModalAsync(new NoteDialog(), false);
        _connectionStatus = "Syncing";
        var result = await _orderClient.AddNoteAsync(_currentOrder, line, "Client note");
        await ApplyMotherOrderResultAsync(result);
        _connectionStatus = "Connected";
        ShowOrder();
    }

    private async Task SendCurrentOrderToKitchenAsync()
    {
        if (_currentOrder == null)
        {
            return;
        }

        _connectionStatus = "Syncing";
        var result = await _orderClient.SendToKitchenAsync(_currentOrder);
        await ApplyMotherOrderResultAsync(result);
        await RequestPrintAsync("kitchen ticket", _currentOrder.OrderId, false);
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

    private async void ShowMoreOptions()
    {
        await Navigation.PushModalAsync(new MoreOptionsDialog(), false);
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
        if (printType == "cash drawer open" && (_currentSession == null || !_currentSession.HasPermission("client.cash_drawer.open")))
        {
            ShowToast("This user cannot open the cash drawer from Client POS.");
            return null;
        }

        if (printType != "cash drawer open" && (_currentSession == null || !_currentSession.HasPermission("client.order.print")))
        {
            ShowToast("This user cannot send print requests from Client POS.");
            return null;
        }

        _connectionStatus = "Syncing";
        var request = await _printClient.RequestPrintAsync(printType, orderId, _currentSession);
        await _cache.SavePrintRequestAsync(request);

        if (request.Status is "queued")
        {
            var printing = await _printClient.AdvanceStatusAsync(request);
            await _cache.SavePrintRequestAsync(printing);
            var printed = await _printClient.AdvanceStatusAsync(printing);
            await _cache.SavePrintRequestAsync(printed);
            request = printed;
        }
        else if (request.Status is "printer offline")
        {
            var failed = await _printClient.AdvanceStatusAsync(request);
            await _cache.SavePrintRequestAsync(failed);
            request = failed;
        }

        _recentPrintRequests = await _cache.GetRecentPrintRequestsAsync();
        _connectionStatus = "Connected";
        ShowToast($"{printType}: {request.Status}");
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

    private void ShowCustomerForm(string title)
    {
        if (title.StartsWith("Delivery", StringComparison.OrdinalIgnoreCase))
        {
            _posSelectedMenu = "Delivery";
            _ = Navigation.PushAsync(new Pages.Orders.DeliveryOrderPage(), false);
            return;
        }

        _posSelectedMenu = "Collection";
        _ = Navigation.PushAsync(new Pages.Orders.CollectionOrderPage(), false);
    }

    private void ShowPayment()
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

    private async Task CompletePaymentAsync(string method)
    {
        await _cache.QueuePendingActionAsync("close_order_payment", new
        {
            paymentMethod = method,
            total = _currentOrder?.Total ?? 0m,
            selectedTable = _currentOrder?.TableNumber ?? _selectedCachedTable?.TableNumber,
            guests = _guests,
            lines = CurrentOrderPayloadLines()
        });
        _cacheStatus = await _cache.GetStatusAsync();
        ShowToast("Payment queued for Mother POS.");
        ShowDashboard();
    }

    private async Task QueueClientActionAsync(string actionType, string message)
    {
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

    private void ShowLiveOrders() => ShowLiveOrders("All");

    private async void ShowLiveOrders(string selectedFilter)
    {
        _posSelectedMenu = "Live Order";
        Root.Children.Clear();
        selectedFilter = NormalizeLiveOrderType(selectedFilter);

        var openOrders = await _cache.GetOpenOrderStatesAsync();
        var onlineOrders = await _cache.GetOnlineOrdersAsync();
        var cards = BuildLiveOrderCards(openOrders, onlineOrders)
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
        IReadOnlyList<MotherOrderState> openOrders,
        IReadOnlyList<CachedOnlineOrder> onlineOrders)
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

        foreach (var order in onlineOrders.Where(order => !string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase)))
        {
            var type = NormalizeLiveOrderType(order.OrderType);
            cards.Add(new LiveOrderCardModel(
                Type: type,
                Title: type,
                OrderNumber: FormatLiveOrderNumber(order.OrderNumber, order.MotherId),
                Subtitle: string.IsNullOrWhiteSpace(order.CustomerName) ? order.Status : order.CustomerName,
                Total: order.Total,
                TimeText: FormatLiveOrderTime(order.DueTime),
                AccentColor: "#10B981",
                OpenAsync: () =>
                {
                    ShowToast($"{order.OrderNumber}: {order.CustomerName}");
                    return Task.CompletedTask;
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

    private View OnlineOrderActions(CachedOnlineOrder order)
    {
        return new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                MiniCommand("View", "#64748B", (_, _) => ShowToast($"{order.OrderNumber}: {order.PayloadJson}")),
                MiniCommand("Accept", "#2563EB", async (_, _) => await UpdateOnlineOrderStatusAsync(order, "Accepted")),
                MiniCommand("Preparing", "#F59E0B", async (_, _) => await UpdateOnlineOrderStatusAsync(order, "Preparing")),
                MiniCommand("Ready", "#7C3AED", async (_, _) => await UpdateOnlineOrderStatusAsync(order, "Ready")),
                MiniCommand("Complete", "#64748B", async (_, _) => await UpdateOnlineOrderStatusAsync(order, "Completed"))
            }
        };
    }

    private Button MiniCommand(string text, string color, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 12,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb(PageBackground),
            BackgroundColor = Color.FromArgb(color),
            CornerRadius = 8,
            HeightRequest = 40,
            WidthRequest = text.Length > 7 ? 92 : 74,
            Padding = 0
        };
        button.Clicked += click;
        return button;
    }

    private async Task UpdateOnlineOrderStatusAsync(CachedOnlineOrder order, string status)
    {
        if (_currentSession == null || !_currentSession.HasPermission("client.online_orders.manage"))
        {
            ShowToast("This user cannot update online order status.");
            return;
        }

        _connectionStatus = "Syncing";
        var updated = await _onlineOrderClient.UpdateStatusAsync(order, status, _currentSession);
        await _cache.SaveOnlineOrderAsync(updated);
        _connectionStatus = "Connected";
        ShowToast($"{updated.OrderNumber}: {updated.Status}");
        ShowLiveOrders();
    }

    private static string OnlineOrderStatusColor(string status)
    {
        return status switch
        {
            "New" => "#2563EB",
            "Accepted" => "#059669",
            "Preparing" => "#F59E0B",
            "Ready" => "#7C3AED",
            "Completed" or "Complete" => "#64748B",
            _ => "#64748B"
        };
    }

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
        var frame = new ApplicationShellFrame
        {
            PageTitle = title,
            MainContent = content,
            UserName = _currentSession?.UserName ?? "No user",
            UserRole = _currentSession?.Role ?? "User",
            TerminalName = LastTerminalName(),
            ConnectionStatus = _connectionStatus,
            SelectedRoute = selectedRoute,
            MenuItems = ClientNavigationItems(),
            ShowUpdateButton = string.Equals(_currentSession?.Role, "Manager", StringComparison.OrdinalIgnoreCase)
        };
        frame.NavigationRequested += (_, e) => OnClientNavigationRequested(e.Route, e.Item.Title);
        frame.LogoutRequested += (_, _) => Logout();
        frame.UpdateRequested += OnClientSidebarUpdateAllClicked;
        frame.RetryRequested += (_, _) => RefreshCurrentPosPage();
        _activeApplicationFrame = frame;
        return frame;
    }

    private IReadOnlyList<ApplicationNavigationItem> ClientNavigationItems() =>
        ClientNavigationService.BuildMenuItems(
            _currentSession,
            ClientNavigationService.IsMotherConnected(_connectionStatus));

    private static string RouteForTitle(string title) => title.Trim().ToLowerInvariant() switch
    {
        "cash drawer" => "cashdrawer",
        "live order" => "liveorder",
        "restaurant" => "restaurant",
        "collection" => "collection",
        "delivery" => "delivery",
        "web orders" => "weborders",
        "gift cards" => "giftcards",
        "loyalty points" => "loyalty",
        "reservation" => "reservation",
        "order history" => "orderhistory",
        "payment" => "payment",
        "new order" => "orderentry",
        "customers" => "customers",
        "report" => "report",
        "settings" => "settings",
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
            : $"SQLite cache v{status.SchemaVersion ?? "?"}: {status.Categories} categories, {status.Products} products, {status.Tables} tables, {status.OpenOrders} open orders, {status.OnlineOrders} online orders, {status.PendingActions} pending actions.";

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
        _ = DisplayAlert("Restaurant POS", message, "OK");
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
