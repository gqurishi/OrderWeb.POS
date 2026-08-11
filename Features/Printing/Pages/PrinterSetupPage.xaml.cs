using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using Microsoft.Maui.Controls.Shapes;

namespace POS_in_NET.Pages;

public partial class PrinterSetupPage : ContentPage
{
    private NetworkPrinterDatabaseService? _dbService;
    private NetworkPrinterService? _printerService;
    private PrinterHealthService? _healthService;
    private NetworkPrintQueueService? _queueService;
    private PrintGroupService? _printGroupService;
    private PrinterRoutingService? _routingService;
    private ReceiptLogoSettingsService? _receiptLogoSettingsService;
    private List<NetworkPrinter> _routingPrinters = new();
    private NetworkPrinter? _editingPrinter;
    private bool _isInitialized = false;
    private Timer? _refreshTimer;
    
    // Form state
    private PrinterBrand _selectedBrand = PrinterBrand.Epson;
    private NetworkPrinterType _selectedType = NetworkPrinterType.Receipt;
    private PaperWidth _selectedWidth = PaperWidth.Mm80;
    private string? _selectedPrintGroupId = null;
    private ReceiptLogoSize _receiptLogoSize = ReceiptLogoSize.Small;

    public PrinterSetupPage()
    {
        InitializeComponent();
        SizeChanged += OnPrinterPageSizeChanged;
    }

    private void OnPrinterPageSizeChanged(object? sender, EventArgs e)
    {
        var compact = Width > 0 && (Width < 1450 || Height < 850);
        PrinterContentGrid.Padding = compact ? new Thickness(20, 16) : new Thickness(32);
        PrinterContentGrid.ColumnSpacing = compact ? 20 : 32;
        PrinterListLayout.Spacing = compact ? 16 : 24;
        FormPanel.WidthRequest = compact ? 360 : 420;
        FormPanel.Padding = compact ? new Thickness(20) : new Thickness(28);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TopBar.SetPageTitle("Printers");

        try
        {
            if (!_isInitialized)
            {
                await InitializeServicesAsync();
                _isInitialized = true;
            }

            await LoadPrintersAsync();
            await LoadRoutingSettingsAsync();
            await LoadReceiptLogoSizeAsync();
            await UpdateStatusAsync();
            
            // Auto-refresh every 10 seconds
            _refreshTimer = new Timer(async _ =>
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await UpdateStatusAsync();
                });
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" OnAppearing error: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private async Task InitializeServicesAsync()
    {
        try
        {
            _dbService = ServiceHelper.GetService<NetworkPrinterDatabaseService>();
            _printerService = ServiceHelper.GetService<NetworkPrinterService>();
            _healthService = ServiceHelper.GetService<PrinterHealthService>();
            _queueService = ServiceHelper.GetService<NetworkPrintQueueService>();
            _printGroupService = ServiceHelper.GetService<PrintGroupService>() ?? new PrintGroupService();
            _routingService = ServiceHelper.GetService<PrinterRoutingService>();
            _receiptLogoSettingsService = ServiceHelper.GetService<ReceiptLogoSettingsService>()
                ?? new ReceiptLogoSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
            
            if (_dbService != null)
            {
                await _dbService.EnsureTablesExistAsync();
            }

            RoutingModePicker.ItemsSource = new[] { "Dedicated Printers", "One Printer For All" };
            
            System.Diagnostics.Debug.WriteLine(" Printer services initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error initializing services: {ex.Message}");
        }
    }

    private async Task LoadReceiptLogoSizeAsync()
    {
        if (_receiptLogoSettingsService == null)
        {
            return;
        }

        _receiptLogoSize = await _receiptLogoSettingsService.GetLogoSizeAsync();
        UpdateReceiptLogoSizeControls();
    }

    private async void OnSmallLogoSizeClicked(object? sender, EventArgs e) =>
        await SaveReceiptLogoSizeAsync(ReceiptLogoSize.Small);

    private async void OnMediumLogoSizeClicked(object? sender, EventArgs e) =>
        await SaveReceiptLogoSizeAsync(ReceiptLogoSize.Medium);

    private async void OnLargeLogoSizeClicked(object? sender, EventArgs e) =>
        await SaveReceiptLogoSizeAsync(ReceiptLogoSize.Large);

    private async Task SaveReceiptLogoSizeAsync(ReceiptLogoSize size)
    {
        if (_receiptLogoSettingsService == null)
        {
            return;
        }

        SmallLogoSizeButton.IsEnabled = false;
        MediumLogoSizeButton.IsEnabled = false;
        LargeLogoSizeButton.IsEnabled = false;
        var saved = await _receiptLogoSettingsService.SaveLogoSizeAsync(size);
        SmallLogoSizeButton.IsEnabled = true;
        MediumLogoSizeButton.IsEnabled = true;
        LargeLogoSizeButton.IsEnabled = true;

        if (!saved)
        {
            await AppAlertService.ShowAlertAsync("Logo Size", "The receipt logo size could not be saved.");
            return;
        }

        _receiptLogoSize = size;
        UpdateReceiptLogoSizeControls();
    }

    private void UpdateReceiptLogoSizeControls()
    {
        SetLogoSizeButtonState(SmallLogoSizeButton, _receiptLogoSize == ReceiptLogoSize.Small);
        SetLogoSizeButtonState(MediumLogoSizeButton, _receiptLogoSize == ReceiptLogoSize.Medium);
        SetLogoSizeButtonState(LargeLogoSizeButton, _receiptLogoSize == ReceiptLogoSize.Large);
        LogoSizeStatusLabel.Text = $"Selected: {_receiptLogoSize} (saved automatically)";
    }

    private static void SetLogoSizeButtonState(Button button, bool isSelected)
    {
        button.BackgroundColor = Color.FromArgb(isSelected ? "#0F172A" : "#E2E8F0");
        button.TextColor = Color.FromArgb(isSelected ? "#FFFFFF" : "#334155");
    }

    private async Task LoadRoutingSettingsAsync()
    {
        if (_dbService == null || _routingService == null)
        {
            return;
        }

        _routingPrinters = (await _dbService.GetAllPrintersAsync())
            .Where(printer => printer.IsEnabled && printer.PrinterType != NetworkPrinterType.Label)
            .ToList();
        AllJobsPrinterPicker.ItemsSource = _routingPrinters;

        var settings = await _routingService.GetSettingsAsync();
        RoutingModePicker.SelectedIndex = settings.UseAllJobsPrinter ? 1 : 0;
        AllJobsPrinterPicker.SelectedItem = settings.AllJobsPrinterId.HasValue
            ? _routingPrinters.FirstOrDefault(printer => printer.Id == settings.AllJobsPrinterId.Value)
            : null;
        UpdateRoutingControls();
    }

    private void UpdateRoutingControls()
    {
        var useAllJobsPrinter = RoutingModePicker.SelectedIndex == 1;
        AllJobsPrinterPicker.IsEnabled = useAllJobsPrinter;
        TestRoutingPrinterButton.IsEnabled = useAllJobsPrinter && AllJobsPrinterPicker.SelectedItem is NetworkPrinter;

        if (!useAllJobsPrinter)
        {
            RoutingStatusLabel.Text = "Dedicated printers";
            RoutingStatusLabel.TextColor = Color.FromArgb("#64748B");
            return;
        }

        if (AllJobsPrinterPicker.SelectedItem is NetworkPrinter printer)
        {
            RoutingStatusLabel.Text = $"All jobs: {printer.IpAddress}:{printer.Port}";
            RoutingStatusLabel.TextColor = printer.IsOnline
                ? Color.FromArgb("#16A34A")
                : Color.FromArgb("#D97706");
        }
        else
        {
            RoutingStatusLabel.Text = "Select a printer";
            RoutingStatusLabel.TextColor = Color.FromArgb("#DC2626");
        }
    }

    private void OnRoutingModeChanged(object? sender, EventArgs e) => UpdateRoutingControls();

    private void OnAllJobsPrinterChanged(object? sender, EventArgs e) => UpdateRoutingControls();

    private async void OnSaveRoutingClicked(object? sender, EventArgs e)
    {
        if (_routingService == null)
        {
            await AppAlertService.ShowAlertAsync("Error", "Printer routing service is unavailable.");
            return;
        }

        var useAllJobsPrinter = RoutingModePicker.SelectedIndex == 1;
        var selectedPrinter = AllJobsPrinterPicker.SelectedItem as NetworkPrinter;
        if (useAllJobsPrinter && selectedPrinter == null)
        {
            await AppAlertService.ShowAlertAsync("Required", "Select the printer that will receive all jobs.");
            return;
        }

        try
        {
            SaveRoutingButton.IsEnabled = false;
            await _routingService.SaveSettingsAsync(useAllJobsPrinter, selectedPrinter?.Id);
            UpdateRoutingControls();
            var message = useAllJobsPrinter
                ? $"All print jobs will use '{selectedPrinter!.Name}'."
                : "Dedicated printer routing is active.";
            await AppAlertService.ShowAlertAsync("Success", message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", $"Failed to save print routing: {ex.Message}");
        }
        finally
        {
            SaveRoutingButton.IsEnabled = true;
        }
    }

    private async void OnTestRoutingPrinterClicked(object? sender, EventArgs e)
    {
        if (_printerService == null || AllJobsPrinterPicker.SelectedItem is not NetworkPrinter printer)
        {
            return;
        }

        TestRoutingPrinterButton.IsEnabled = false;
        try
        {
            var success = await _printerService.SendTestPrintAsync(printer);
            await AppAlertService.ShowAlertAsync(
                success ? "Success" : "Error",
                success ? $"Test print sent to '{printer.Name}'." : $"Failed to print to '{printer.Name}'.");
        }
        finally
        {
            UpdateRoutingControls();
        }
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            var onlineCount = _healthService?.OnlinePrinters.ToString() ?? "0";
            var offlineCount = _healthService?.OfflinePrinters.ToString() ?? "0";
            var lastCheck = _healthService?.LastHealthCheck > DateTime.MinValue
                ? _healthService.LastHealthCheck.ToString("HH:mm:ss")
                : null;
            var queueCount = _queueService != null
                ? (await _queueService.GetStatsAsync()).PendingJobs.ToString()
                : null;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_healthService != null)
                {
                    OnlineCountLabel.Text = onlineCount;
                    OfflineCountLabel.Text = offlineCount;
                    if (lastCheck != null)
                    {
                        LastCheckLabel.Text = lastCheck;
                    }
                }

                if (queueCount != null)
                {
                    QueueCountLabel.Text = queueCount;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error updating status: {ex.Message}");
        }
    }

    private async Task LoadPrintersAsync()
    {
        if (_dbService == null) return;

        try
        {
            var printers = await _dbService.GetAllPrintersAsync();
            var countText = $"{printers.Count} printer{(printers.Count != 1 ? "s" : "")} configured";

            var receiptPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Receipt).ToList();
            var kitchenPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Kitchen).ToList();
            var barPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Bar).ToList();
            var labelPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Label).ToList();
            var onlinePrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Online).ToList();
            var takeawayPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Takeaway).ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PrinterCountLabel.Text = countText;
                ClearPrinterContainers();

                NoReceiptPrintersLabel.IsVisible = receiptPrinters.Count == 0;
                foreach (var printer in receiptPrinters)
                {
                    ReceiptPrintersContainer.Children.Add(CreatePrinterCard(printer));
                }

                NoKitchenPrintersLabel.IsVisible = kitchenPrinters.Count == 0;
                foreach (var printer in kitchenPrinters)
                {
                    KitchenPrintersContainer.Children.Add(CreatePrinterCard(printer));
                }

                NoBarPrintersLabel.IsVisible = barPrinters.Count == 0;
                foreach (var printer in barPrinters)
                {
                    BarPrintersContainer.Children.Add(CreatePrinterCard(printer));
                }

                NoLabelPrintersLabel.IsVisible = labelPrinters.Count == 0;
                foreach (var printer in labelPrinters)
                {
                    LabelPrintersContainer.Children.Add(CreatePrinterCard(printer));
                }

                NoOnlinePrintersLabel.IsVisible = onlinePrinters.Count == 0;
                foreach (var printer in onlinePrinters)
                {
                    OnlinePrintersContainer.Children.Add(CreatePrinterCard(printer));
                }

                NoTakeawayPrintersLabel.IsVisible = takeawayPrinters.Count == 0;
                foreach (var printer in takeawayPrinters)
                {
                    TakeawayPrintersContainer.Children.Add(CreatePrinterCard(printer));
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error loading printers: {ex.Message}");
        }
    }

    private void ClearPrinterContainers()
    {
        // Keep the "no printers" labels, remove printer cards
        var receiptChildren = ReceiptPrintersContainer.Children.Where(c => c != NoReceiptPrintersLabel).ToList();
        foreach (var child in receiptChildren) ReceiptPrintersContainer.Children.Remove(child);

        var kitchenChildren = KitchenPrintersContainer.Children.Where(c => c != NoKitchenPrintersLabel).ToList();
        foreach (var child in kitchenChildren) KitchenPrintersContainer.Children.Remove(child);

        var barChildren = BarPrintersContainer.Children.Where(c => c != NoBarPrintersLabel).ToList();
        foreach (var child in barChildren) BarPrintersContainer.Children.Remove(child);

        var labelChildren = LabelPrintersContainer.Children.Where(c => c != NoLabelPrintersLabel).ToList();
        foreach (var child in labelChildren) LabelPrintersContainer.Children.Remove(child);

        var onlineChildren = OnlinePrintersContainer.Children.Where(c => c != NoOnlinePrintersLabel).ToList();
        foreach (var child in onlineChildren) OnlinePrintersContainer.Children.Remove(child);

        var takeawayChildren = TakeawayPrintersContainer.Children.Where(c => c != NoTakeawayPrintersLabel).ToList();
        foreach (var child in takeawayChildren) TakeawayPrintersContainer.Children.Remove(child);
    }

    private View CreatePrinterCard(NetworkPrinter printer)
    {
        var card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            Padding = 20,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#0A000000")),
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.08f
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 16
        };

        // Status indicator dot
        var statusDot = new BoxView
        {
            BackgroundColor = printer.IsOnline ? Color.FromArgb("#22C55E") : Color.FromArgb("#EF4444"),
            WidthRequest = 12,
            HeightRequest = 12,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(statusDot, 0, 0);

        // Printer info
        var infoStack = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        
        var nameRow = new HorizontalStackLayout { Spacing = 10 };
        nameRow.Children.Add(new Label
        {
            Text = printer.Name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A")
        });

        // Feature badges
        if (printer.HasCashDrawer)
        {
            nameRow.Children.Add(CreateFeatureBadge("Drawer", "#22C55E"));
        }
        if (printer.HasBuzzer)
        {
            nameRow.Children.Add(CreateFeatureBadge("Buzzer", "#F59E0B"));
        }
        if (printer.SupportsTwoColor)
        {
            nameRow.Children.Add(CreateFeatureBadge("Black + Red", "#EF4444"));
        }
        if (!printer.IsEnabled)
        {
            nameRow.Children.Add(CreateFeatureBadge("Disabled", "#EF4444"));
        }
        
        infoStack.Children.Add(nameRow);
        
        infoStack.Children.Add(new Label
        {
            Text = $"{printer.IpAddress}:{printer.Port}  •  {printer.Brand}  •  {(printer.PaperWidth == PaperWidth.Mm80 ? "80mm" : "58mm")}",
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B")
        });

        grid.Add(infoStack, 1, 0);

        // Action buttons
        var buttonsStack = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };

        // Edit button
        var editBtn = CreateActionButton("Edit", "#6366F1");
        editBtn.Clicked += (s, e) => OnEditPrinterClicked(printer);
        buttonsStack.Children.Add(editBtn);

        // Test button
        var testBtn = CreateActionButton("Test", "#3B82F6");
        testBtn.Clicked += async (s, e) => await OnTestPrinterClicked(printer);
        buttonsStack.Children.Add(testBtn);

        // Delete button
        var deleteBtn = CreateActionButton("Delete", "#EF4444");
        deleteBtn.Clicked += async (s, e) => await OnDeletePrinterClicked(printer);
        buttonsStack.Children.Add(deleteBtn);

        grid.Add(buttonsStack, 2, 0);

        card.Content = grid;
        return card;
    }

    private Border CreateFeatureBadge(string text, string colorHex)
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb(colorHex).WithAlpha(0.1f),
            Padding = new Thickness(8, 4),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = new Label
            {
                Text = text,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(colorHex)
            }
        };
    }

    private Button CreateActionButton(string text, string colorHex)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(colorHex),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(14, 8),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold
        };
    }

    #region Form Actions

    private void OnAddPrinterClicked(object? sender, EventArgs e)
    {
        _editingPrinter = null;
        FormTitle.Text = "Add Printer";
        SavePrinterButton.Text = "Save Printer";
        ResetForm();
        FormPanel.IsVisible = true;
    }

    private void OnEditPrinterClicked(NetworkPrinter printer)
    {
        _editingPrinter = printer;
        FormTitle.Text = "Edit Printer";
        SavePrinterButton.Text = "Update Printer";
        
        // Populate form
        PrinterNameEntry.Text = printer.Name;
        IpAddressEntry.Text = printer.IpAddress;
        PortEntry.Text = printer.Port.ToString();
        
        // Set brand
        SelectBrand(printer.Brand);
        
        // Set type
        SelectType(printer.PrinterType);
        
        // Set width
        SelectWidth(printer.PaperWidth);
        
        // Set features
        CashDrawerCheckbox.IsChecked = printer.HasCashDrawer;
        CutterCheckbox.IsChecked = printer.HasCutter;
        BuzzerCheckbox.IsChecked = printer.HasBuzzer;
        TwoColorCheckbox.IsChecked = printer.SupportsTwoColor;
        
        // Set print group - load the group name if exists
        if (!string.IsNullOrEmpty(printer.PrintGroupId))
        {
            LoadPrintGroupNameAsync(printer.PrintGroupId);
        }
        else
        {
            PrintGroupEntry.Text = string.Empty;
        }
        
        ConnectionStatusLabel.Text = printer.IsOnline ? "Connected" : "Offline";
        ConnectionStatusLabel.TextColor = printer.IsOnline ? Color.FromArgb("#22C55E") : Color.FromArgb("#EF4444");
        
        FormPanel.IsVisible = true;
    }

    private void OnCloseFormClicked(object? sender, EventArgs e)
    {
        FormPanel.IsVisible = false;
        _editingPrinter = null;
    }

    private void ResetForm()
    {
        PrinterNameEntry.Text = string.Empty;
        IpAddressEntry.Text = string.Empty;
        PortEntry.Text = "9100";
        
        SelectBrand(PrinterBrand.Epson);
        SelectType(NetworkPrinterType.Receipt);
        SelectWidth(PaperWidth.Mm80);
        
        CashDrawerCheckbox.IsChecked = false;
        CutterCheckbox.IsChecked = true;
        BuzzerCheckbox.IsChecked = false;
        TwoColorCheckbox.IsChecked = false;
        
        PrintGroupEntry.Text = string.Empty;
        
        ConnectionStatusLabel.Text = "Not tested";
        ConnectionStatusLabel.TextColor = Color.FromArgb("#94A3B8");
    }

    private async void OnTestConnectionClicked(object? sender, EventArgs e)
    {
        if (_printerService == null) return;

        var ip = IpAddressEntry.Text?.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter an IP address");
            return;
        }

        if (!IsPrivateIpv4Address(ip))
        {
            await AppAlertService.ShowAlertAsync(
                "Private IP Required",
                "Enter the printer's private static IP or DHCP reservation (10.x.x.x, 172.16-31.x.x, or 192.168.x.x). Public printer addresses are blocked.");
            return;
        }

        if (!int.TryParse(PortEntry.Text, out int port) || port is < 1 or > 65535)
        {
            await AppAlertService.ShowAlertAsync("Invalid Port", "Enter a TCP port from 1 to 65535 (normally 9100).");
            return;
        }

        TestConnectionButton.IsEnabled = false;
        ConnectionStatusLabel.Text = "Testing...";
        ConnectionStatusLabel.TextColor = Color.FromArgb("#64748B");

        try
        {
            var result = await _printerService.TestConnectionAsync(ip, port);
            
            if (result.Success)
            {
                ConnectionStatusLabel.Text = $"Connected ({result.ResponseTimeMs}ms)";
                ConnectionStatusLabel.TextColor = Color.FromArgb("#22C55E");
            }
            else
            {
                ConnectionStatusLabel.Text = result.Message;
                ConnectionStatusLabel.TextColor = Color.FromArgb("#EF4444");
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusLabel.Text = $"Error: {ex.Message}";
            ConnectionStatusLabel.TextColor = Color.FromArgb("#EF4444");
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private async void OnSavePrinterClicked(object? sender, EventArgs e)
    {
        if (_dbService == null) return;

        var name = PrinterNameEntry.Text?.Trim();
        var ip = IpAddressEntry.Text?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a printer name");
            return;
        }

        if (string.IsNullOrEmpty(ip))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter an IP address");
            return;
        }

        if (!IsPrivateIpv4Address(ip))
        {
            await AppAlertService.ShowAlertAsync(
                "Private IP Required",
                "Enter the printer's private static IP or DHCP reservation (10.x.x.x, 172.16-31.x.x, or 192.168.x.x). Public printer addresses are blocked.");
            return;
        }

        if (!int.TryParse(PortEntry.Text, out int port) || port is < 1 or > 65535)
        {
            await AppAlertService.ShowAlertAsync("Invalid Port", "Enter a TCP port from 1 to 65535 (normally 9100).");
            return;
        }

        try
        {
            SavePrinterButton.IsEnabled = false;

            if (_editingPrinter != null && _selectedType == NetworkPrinterType.Label && _routingService != null)
            {
                var routingSettings = await _routingService.GetSettingsAsync();
                if (routingSettings.UseAllJobsPrinter && routingSettings.AllJobsPrinterId == _editingPrinter.Id)
                {
                    await AppAlertService.ShowAlertAsync(
                        "Error",
                        "Switch to Dedicated Printers or select another all-jobs printer before changing this printer to Label.");
                    return;
                }
            }

            var previousPrintGroupId = _editingPrinter?.PrintGroupId;
            var printer = _editingPrinter ?? new NetworkPrinter();
            printer.Name = name;
            printer.IpAddress = ip;
            printer.Port = port;
            printer.Brand = _selectedBrand;
            printer.PrinterType = _selectedType;
            printer.PaperWidth = _selectedWidth;
            printer.HasCashDrawer = CashDrawerCheckbox.IsChecked;
            printer.HasCutter = CutterCheckbox.IsChecked;
            printer.HasBuzzer = BuzzerCheckbox.IsChecked;
            printer.SupportsTwoColor = TwoColorCheckbox.IsChecked;
            printer.IsEnabled = true;
            
            // Get or create print group from the entered name
            var printGroupName = PrintGroupEntry.Text?.Trim();
            if (!string.IsNullOrEmpty(printGroupName))
            {
                printer.PrintGroupId = await GetOrCreatePrintGroupIdAsync(printGroupName, printer);
            }
            else
            {
                printer.PrintGroupId = null;
            }
            
            // Set color based on type
            printer.ColorCode = _selectedType switch
            {
                NetworkPrinterType.Receipt => "#10B981",
                NetworkPrinterType.Kitchen => "#EF4444",
                NetworkPrinterType.Bar => "#8B5CF6",
                NetworkPrinterType.Label => "#22C55E",
                NetworkPrinterType.Online => "#3B82F6",
                NetworkPrinterType.Takeaway => "#F97316",
                _ => "#6366F1"
            };

            if (_editingPrinter != null)
            {
                await _dbService.UpdatePrinterAsync(printer);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Printer '{name}' updated!");
            }
            else
            {
                printer.Id = await _dbService.AddPrinterAsync(printer);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Printer '{name}' added!");
            }

            if (!string.Equals(previousPrintGroupId, printer.PrintGroupId, StringComparison.OrdinalIgnoreCase))
            {
                await ClearPrintGroupPrinterIfUnusedAsync(previousPrintGroupId, printer.Id);
            }

            FormPanel.IsVisible = false;
            _editingPrinter = null;
            await LoadPrintersAsync();
            await LoadRoutingSettingsAsync();
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save printer: {ex.Message}");
        }
        finally
        {
            SavePrinterButton.IsEnabled = true;
        }
    }

    private async Task<string> GetOrCreatePrintGroupIdAsync(string groupName, NetworkPrinter printer)
    {
        if (_printGroupService == null) return string.Empty;

        try
        {
            // Get all existing print groups
            var allGroups = await _printGroupService.GetAllPrintGroupsAsync();
            
            // Check if group with this name already exists (case-insensitive)
            var existingGroup = allGroups.FirstOrDefault(g => 
                g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            
            if (existingGroup != null)
            {
                existingGroup.PrinterIp = printer.IpAddress;
                existingGroup.PrinterPort = printer.Port;
                existingGroup.PrinterType = GetPrintGroupType(printer.PrinterType);
                existingGroup.IsActive = true;
                existingGroup.ColorCode = GetColorForGroupName(existingGroup.Name);

                await _printGroupService.UpdatePrintGroupAsync(existingGroup);
                return existingGroup.Id;
            }
            
            // Create new print group
            var newGroup = new PrintGroup
            {
                Id = Guid.NewGuid().ToString(),
                Name = groupName,
                PrinterIp = printer.IpAddress,
                PrinterPort = printer.Port,
                PrinterType = GetPrintGroupType(printer.PrinterType),
                ColorCode = GetColorForGroupName(groupName),
                IsActive = true,
                DisplayOrder = allGroups.Count
            };
            
            await _printGroupService.CreatePrintGroupAsync(newGroup);
            System.Diagnostics.Debug.WriteLine($" Created new print group: {groupName}");
            
            return newGroup.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error creating print group: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task ClearPrintGroupPrinterIfUnusedAsync(string? printGroupId, int currentPrinterId)
    {
        if (string.IsNullOrWhiteSpace(printGroupId) || _printGroupService == null || _dbService == null)
        {
            return;
        }

        try
        {
            var printers = await _dbService.GetAllPrintersAsync();
            var stillAssigned = printers.Any(printer =>
                printer.Id != currentPrinterId &&
                string.Equals(printer.PrintGroupId, printGroupId, StringComparison.OrdinalIgnoreCase) &&
                printer.IsEnabled);

            if (stillAssigned)
            {
                return;
            }

            var group = await _printGroupService.GetPrintGroupByIdAsync(printGroupId);
            if (group == null)
            {
                return;
            }

            group.PrinterIp = null;
            group.PrinterPort = 9100;
            await _printGroupService.UpdatePrintGroupAsync(group);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error clearing print group printer: {ex.Message}");
        }
    }

    private async void LoadPrintGroupNameAsync(string printGroupId)
    {
        if (_printGroupService == null) return;

        try
        {
            var allGroups = await _printGroupService.GetAllPrintGroupsAsync();
            var group = allGroups.FirstOrDefault(g => g.Id == printGroupId);
            
            if (group != null)
            {
                PrintGroupEntry.Text = group.Name;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error loading print group: {ex.Message}");
        }
    }

    private string GetColorForGroupName(string groupName)
    {
        // Assign colors based on common group names
        var nameLower = groupName.ToLower();
        
        if (nameLower.Contains("kitchen")) return "#EF4444"; // Red
        if (nameLower.Contains("bar")) return "#8B5CF6"; // Purple
        if (nameLower.Contains("grill")) return "#F97316"; // Orange
        if (nameLower.Contains("takeaway") || nameLower.Contains("delivery")) return "#3B82F6"; // Blue
        if (nameLower.Contains("label") || nameLower.Contains("printer")) return "#22C55E"; // Green
        if (nameLower.Contains("dessert")) return "#EC4899"; // Pink
        if (nameLower.Contains("starter")) return "#F59E0B"; // Amber
        
        // Default color
        return "#6366F1"; // Indigo
    }

    private static string GetPrintGroupType(NetworkPrinterType printerType)
    {
        return printerType switch
        {
            NetworkPrinterType.Bar => "bar",
            NetworkPrinterType.Receipt or NetworkPrinterType.Online => "receipt",
            NetworkPrinterType.Label => "label",
            _ => "kitchen"
        };
    }

    #endregion

    #region Printer Actions

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadPrintersAsync();
        await LoadRoutingSettingsAsync();
    }

    private async void OnTestAllClicked(object? sender, EventArgs e)
    {
        if (_dbService == null || _printerService == null) return;

        var printers = await _dbService.GetAllPrintersAsync();
        if (printers.Count == 0)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("No Printers", "No printers configured to test.");
            return;
        }

        int success = 0, failed = 0;

        foreach (var printer in printers.Where(p => p.IsEnabled))
        {
            var result = await SendPrinterTestAsync(printer);
            if (result) success++;
            else failed++;
        }

        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test Complete", $"{success} successful, {failed} failed");
        await LoadPrintersAsync();
    }

    private async void OnCheckStatusClicked(object? sender, EventArgs e)
    {
        if (_healthService == null)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Health service not available.");
            return;
        }

        CheckStatusButton.IsEnabled = false;
        CheckStatusButton.Text = "Checking...";

        try
        {
            // Force health check
            await _healthService.ForceHealthCheckAsync();
            
            // Update UI
            await UpdateStatusAsync();
            await LoadPrintersAsync();
            
            await DisplayAlert("Status Check", 
                $"{_healthService.OnlinePrinters} online, {_healthService.OfflinePrinters} offline", 
                "OK");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Check failed: {ex.Message}");
        }
        finally
        {
            CheckStatusButton.IsEnabled = true;
            CheckStatusButton.Text = "Check Status";
        }
    }

    private async void OnOpenDrawerClicked(object? sender, EventArgs e)
    {
        if (_dbService == null || _printerService == null) return;

        NetworkPrinter? drawerPrinter = null;
        if (_routingService != null)
        {
            var settings = await _routingService.GetSettingsAsync();
            if (settings.UseAllJobsPrinter)
            {
                var allJobsPrinter = await _routingService.GetAllJobsPrinterAsync();
                drawerPrinter = allJobsPrinter is { HasCashDrawer: true } ? allJobsPrinter : null;
            }
        }

        if (drawerPrinter == null && (_routingService == null || !(await _routingService.GetSettingsAsync()).UseAllJobsPrinter))
        {
            var receiptPrinters = await _dbService.GetPrintersByTypeAsync(NetworkPrinterType.Receipt);
            drawerPrinter = receiptPrinters.FirstOrDefault(p => p.HasCashDrawer && p.IsEnabled);
        }

        if (drawerPrinter == null)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("No Cash Drawer", "No receipt printer with cash drawer configured.");
            return;
        }

        var result = await _printerService.OpenCashDrawerAsync(drawerPrinter);
        
        if (result)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Cash drawer opened on '{drawerPrinter.Name}'");
        }
        else
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open cash drawer on '{drawerPrinter.Name}'");
        }
    }

    private async void OnPrintDesignClicked(object? sender, EventArgs e)
    {
        await NavigationCoordinator.Shared.PushTemporaryPageAsync(new PrintTemplatesPage(), source: sender as VisualElement);
    }

    private static bool IsPrivateIpv4Address(string value)
    {
        if (!System.Net.IPAddress.TryParse(value, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private async void OnManageQueueClicked(object? sender, EventArgs e)
    {
        if (_queueService == null || _dbService == null)
        {
            await AppAlertService.ShowAlertAsync("Error", "Print queue service is unavailable.");
            return;
        }

        var currentUser = AuthenticationService.Instance.CurrentUser;
        if (currentUser == null || currentUser.Role is not (UserRole.Manager or UserRole.Admin))
        {
            await AppAlertService.ShowAlertAsync("Denied", "Manager or admin access is required to manage the print queue.");
            return;
        }

        var printers = await _dbService.GetAllPrintersAsync();
        var dialog = new PrintQueueManagementDialog(_queueService, printers);
        var selection = await dialog.ShowAsync();
        if (selection.Action == PrintQueueManagementAction.Close)
        {
            return;
        }

        if (selection.Action == PrintQueueManagementAction.RetryFailed)
        {
            var retried = await _queueService.RetryAllFailedJobsAsync(selection.PrinterId);
            await AppAlertService.ShowAlertAsync("Complete", $"{retried} failed print job(s) queued for retry.");
            await UpdateStatusAsync();
            return;
        }

        var cutoff = selection.Action == PrintQueueManagementAction.CancelPreviousDays
            ? DateTime.Today.AddMilliseconds(-1)
            : DateTime.Now;
        var candidateCount = selection.Action == PrintQueueManagementAction.CancelPreviousDays
            ? selection.Snapshot.PreviousDayJobs
            : selection.Snapshot.WaitingJobs;
        var actionName = selection.Action == PrintQueueManagementAction.CancelPreviousDays
            ? "previous-day"
            : "waiting";

        var confirm = new ModernConfirmDialog();
        confirm.SetConfirm(
            "Cancel Print Jobs",
            $"Cancel {candidateCount} {actionName} print job(s) for {selection.ScopeName}? Jobs already printing and jobs created after this confirmation started will not be cancelled.",
            "Cancel Jobs",
            "Keep Jobs",
            "!",
            "#DC2626");
        if (!await confirm.ShowAsync())
        {
            return;
        }

        var result = await _queueService.CancelWaitingJobsAsync(
            cutoff,
            selection.PrinterId,
            currentUser.Id,
            string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Username : currentUser.Name,
            $"Cancelled by {currentUser.Username} from Printer Setup ({actionName})",
            selection.PrinterId.HasValue ? "printer" : "all_printers");

        await AppAlertService.ShowAlertAsync(
            "Complete",
            $"{result.CancelledJobs} print job(s) cancelled. Completed jobs, active printing, and newer jobs were left unchanged.");
        await UpdateStatusAsync();
    }

    private async Task OnTestPrinterClicked(NetworkPrinter printer)
    {
        if (_printerService == null) return;

        var result = await SendPrinterTestAsync(printer);
        
        if (result)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Test print sent to '{printer.Name}'");
        }
        else
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send test print to '{printer.Name}'");
        }
    }

    private async Task<bool> SendPrinterTestAsync(NetworkPrinter printer)
    {
        if (printer.PrinterType == NetworkPrinterType.Label)
        {
            var labelService = new LabelPrintingService(printer.IpAddress, printer.Port, printer.IsEnabled);
            return await labelService.PrintTestLabelAsync();
        }

        if (_printerService == null)
        {
            return false;
        }

        return await _printerService.SendTestPrintAsync(printer);
    }

    private async Task OnOpenDrawerForPrinterClicked(NetworkPrinter printer)
    {
        if (_printerService == null) return;

        var result = await _printerService.OpenCashDrawerAsync(printer);
        
        if (result)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Cash drawer opened on '{printer.Name}'");
        }
        else
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open cash drawer on '{printer.Name}'");
        }
    }

    private async Task OnDeletePrinterClicked(NetworkPrinter printer)
    {
        if (_dbService == null) return;

        var confirm = await DisplayAlert("Confirm Delete", 
            $"Delete printer '{printer.Name}'?\n\nThis cannot be undone.", 
            "Delete", "Cancel");

        if (confirm)
        {
            var printGroupId = printer.PrintGroupId;
            if (_routingService != null)
            {
                await _routingService.DisableIfSelectedAsync(printer.Id);
            }
            await _dbService.DeletePrinterAsync(printer.Id);
            await ClearPrintGroupPrinterIfUnusedAsync(printGroupId, printer.Id);
            await LoadPrintersAsync();
            await LoadRoutingSettingsAsync();
        }
    }

    #endregion

    #region Brand/Type/Width Selection

    private void OnBrandEpsonTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Epson);
    private void OnBrandStarTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Star);
    private void OnBrandOtherTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Other);

    private void SelectBrand(PrinterBrand brand)
    {
        _selectedBrand = brand;
        
        // Epson
        BrandEpsonBorder.BackgroundColor = brand == PrinterBrand.Epson ? Color.FromArgb("#0F172A") : Color.FromArgb("#F1F5F9");
        if (BrandEpsonBorder.Content is Label epsonLabel)
            epsonLabel.TextColor = brand == PrinterBrand.Epson ? Colors.White : Color.FromArgb("#64748B");
        
        // Star
        BrandStarBorder.BackgroundColor = brand == PrinterBrand.Star ? Color.FromArgb("#0F172A") : Color.FromArgb("#F1F5F9");
        if (BrandStarBorder.Content is Label starLabel)
            starLabel.TextColor = brand == PrinterBrand.Star ? Colors.White : Color.FromArgb("#64748B");
        
        // Other
        BrandOtherBorder.BackgroundColor = brand == PrinterBrand.Other ? Color.FromArgb("#0F172A") : Color.FromArgb("#F1F5F9");
        if (BrandOtherBorder.Content is Label otherLabel)
            otherLabel.TextColor = brand == PrinterBrand.Other ? Colors.White : Color.FromArgb("#64748B");
    }

    private void OnTypeReceiptTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Receipt);
    private void OnTypeKitchenTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Kitchen);
    private void OnTypeBarTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Bar);
    private void OnTypeOnlineTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Online);
    private void OnTypeTakeawayTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Takeaway);
    private void OnTypeLabelTapped(object? sender, EventArgs e) => SelectType(NetworkPrinterType.Label);

    private void SelectType(NetworkPrinterType type)
    {
        _selectedType = type;
        
        // Receipt - green
        TypeReceiptBorder.BackgroundColor = type == NetworkPrinterType.Receipt ? Color.FromArgb("#22C55E") : Color.FromArgb("#F1F5F9");
        if (TypeReceiptBorder.Content is Label receiptLabel)
            receiptLabel.TextColor = type == NetworkPrinterType.Receipt ? Colors.White : Color.FromArgb("#64748B");
        
        // Kitchen - orange
        TypeKitchenBorder.BackgroundColor = type == NetworkPrinterType.Kitchen ? Color.FromArgb("#F59E0B") : Color.FromArgb("#F1F5F9");
        if (TypeKitchenBorder.Content is Label kitchenLabel)
            kitchenLabel.TextColor = type == NetworkPrinterType.Kitchen ? Colors.White : Color.FromArgb("#64748B");
        
        // Bar - purple
        TypeBarBorder.BackgroundColor = type == NetworkPrinterType.Bar ? Color.FromArgb("#8B5CF6") : Color.FromArgb("#F1F5F9");
        if (TypeBarBorder.Content is Label barLabel)
            barLabel.TextColor = type == NetworkPrinterType.Bar ? Colors.White : Color.FromArgb("#64748B");

        // Online - blue
        TypeOnlineBorder.BackgroundColor = type == NetworkPrinterType.Online ? Color.FromArgb("#3B82F6") : Color.FromArgb("#F1F5F9");
        if (TypeOnlineBorder.Content is Label onlineLabel)
            onlineLabel.TextColor = type == NetworkPrinterType.Online ? Colors.White : Color.FromArgb("#64748B");

        // Takeaway - orange
        TypeTakeawayBorder.BackgroundColor = type == NetworkPrinterType.Takeaway ? Color.FromArgb("#F97316") : Color.FromArgb("#F1F5F9");
        if (TypeTakeawayBorder.Content is Label takeawayLabel)
            takeawayLabel.TextColor = type == NetworkPrinterType.Takeaway ? Colors.White : Color.FromArgb("#64748B");

        // Label - green
        TypeLabelBorder.BackgroundColor = type == NetworkPrinterType.Label ? Color.FromArgb("#22C55E") : Color.FromArgb("#F1F5F9");
        if (TypeLabelBorder.Content is Label labelLabel)
            labelLabel.TextColor = type == NetworkPrinterType.Label ? Colors.White : Color.FromArgb("#64748B");
        
        // Auto-set buzzer for kitchen/bar/takeaway
        if (type == NetworkPrinterType.Kitchen || type == NetworkPrinterType.Bar || type == NetworkPrinterType.Takeaway)
        {
            BuzzerCheckbox.IsChecked = true;
        }
    }

    private void OnWidth80Tapped(object? sender, EventArgs e) => SelectWidth(PaperWidth.Mm80);
    private void OnWidth58Tapped(object? sender, EventArgs e) => SelectWidth(PaperWidth.Mm58);

    private void SelectWidth(PaperWidth width)
    {
        _selectedWidth = width;
        
        // 80mm
        Width80Border.BackgroundColor = width == PaperWidth.Mm80 ? Color.FromArgb("#0F172A") : Color.FromArgb("#F1F5F9");
        if (Width80Border.Content is Label w80Label)
            w80Label.TextColor = width == PaperWidth.Mm80 ? Colors.White : Color.FromArgb("#64748B");
        
        // 58mm
        Width58Border.BackgroundColor = width == PaperWidth.Mm58 ? Color.FromArgb("#0F172A") : Color.FromArgb("#F1F5F9");
        if (Width58Border.Content is Label w58Label)
            w58Label.TextColor = width == PaperWidth.Mm58 ? Colors.White : Color.FromArgb("#64748B");
    }

    #endregion
}
