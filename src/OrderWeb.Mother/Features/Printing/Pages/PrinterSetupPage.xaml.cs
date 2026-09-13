using OrderWeb.SharedUI.Controls.OrderPlace;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using Microsoft.Maui.Controls.Shapes;
using System.Globalization;

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
    private PrinterBrand _selectedBrand = PrinterBrand.Star;
    private string _selectedTechnology = "thermal";
    private NetworkPrinterType _selectedType = NetworkPrinterType.Receipt;
    private PaperWidth _selectedWidth = PaperWidth.Mm80;
    private string? _selectedPrintGroupId = null;
    private ReceiptLogoSize _receiptLogoSize = ReceiptLogoSize.Small;

    public PrinterSetupPage()
    {
        InitializeComponent();
        SizeChanged += OnPrinterPageSizeChanged;
        WirePrinterFieldKeyboards();
    }

    private void OnPrinterPageSizeChanged(object? sender, EventArgs e)
    {
        var top = TopBar.Height > 1 ? TopBar.Height : 72;
        var scrollHeight = Height - top;
        if (scrollHeight > 1)
        {
            PrinterPageScroll.HeightRequest = scrollHeight;
        }

        if (Width > 1)
        {
            PrinterContentGrid.WidthRequest = Width;
        }

        PrinterContentGrid.Padding = new Thickness(16, 12, 16, 28);
        FormPanel.WidthRequest = -1;
        FormPanel.HorizontalOptions = LayoutOptions.Fill;
        FormPanel.Padding = new Thickness(0);
    }

    private bool _printerKeyboardOpen;

    private void WirePrinterFieldKeyboards()
    {
        WirePrinterTextField(PrinterNameTap, PrinterNameEntry, PrinterNameValueLabel, "Printer name", "Kitchen Hot, Front Counter");
        WirePrinterTextField(PrintGroupTap, PrintGroupEntry, PrintGroupValueLabel, "Print group / station", "Kitchen, Bar, Grill, Dessert, Tandoor");
        WirePrinterIpField();
        WirePrinterPortField();
    }

    private void WirePrinterTextField(Button tapButton, Entry entry, Label label, string title, string placeholder)
    {
        RefreshPrinterFieldLabel(label, entry, placeholder);
        entry.TextChanged += (_, _) => RefreshPrinterFieldLabel(label, entry, placeholder);
        tapButton.Clicked += async (_, _) =>
        {
            var text = await AskPrinterTextAsync(title, placeholder, entry.Text);
            if (text != null)
            {
                entry.Text = text.Trim();
            }
        };
    }

    private void WirePrinterIpField()
    {
        RefreshPrinterFieldLabel(IpAddressValueLabel, IpAddressEntry, "192.168.1.100");
        IpAddressEntry.TextChanged += (_, _) => RefreshPrinterFieldLabel(IpAddressValueLabel, IpAddressEntry, "192.168.1.100");
        IpAddressTap.Clicked += async (_, _) =>
        {
            var text = await AskPrinterNumberAsync("IP address", "192.168.1.100", IpAddressEntry.Text, OrderWeb.SharedUI.Controls.VirtualKeyboardNumericMode.IpAddress);
            if (text != null)
            {
                IpAddressEntry.Text = text.Trim();
            }
        };
    }

    private void WirePrinterPortField()
    {
        RefreshPrinterFieldLabel(PortValueLabel, PortEntry, "9100");
        PortEntry.TextChanged += (_, _) => RefreshPrinterFieldLabel(PortValueLabel, PortEntry, "9100");
        PortTap.Clicked += async (_, _) =>
        {
            var text = await AskPrinterNumberAsync("Port", "9100", PortEntry.Text, OrderWeb.SharedUI.Controls.VirtualKeyboardNumericMode.WholeNumber, 1, 65535, 5);
            if (text != null)
            {
                PortEntry.Text = text.Trim();
            }
        };
    }

    private async Task<string?> AskPrinterTextAsync(string title, string placeholder, string? initial)
    {
        if (_printerKeyboardOpen)
        {
            return null;
        }

        _printerKeyboardOpen = true;
        try
        {
            var keyboard = new OrderWeb.SharedUI.Controls.VirtualKeyboardDialog();
            keyboard.SetPrompt(title, "Done");
            keyboard.SetTextMode(OrderWeb.SharedUI.Controls.VirtualKeyboardTextMode.Text);
            keyboard.SetPlaceholder(placeholder);
            keyboard.SetRequired(false);
            keyboard.SetInitialText(initial ?? string.Empty);
            return await keyboard.ShowAsync(this);
        }
        finally
        {
            _printerKeyboardOpen = false;
        }
    }

    private async Task<string?> AskPrinterNumberAsync(
        string title,
        string placeholder,
        string? initial,
        OrderWeb.SharedUI.Controls.VirtualKeyboardNumericMode mode,
        decimal? minimum = null,
        decimal? maximum = null,
        int maxLength = 0)
    {
        if (_printerKeyboardOpen)
        {
            return null;
        }

        _printerKeyboardOpen = true;
        try
        {
            var keyboard = new OrderWeb.SharedUI.Controls.VirtualKeyboardDialog();
            keyboard.SetPrompt(title, "Done");
            keyboard.SetNumericMode(mode, minimum, maximum);
            keyboard.SetPlaceholder(placeholder);
            keyboard.SetRequired(false);
            if (maxLength > 0)
            {
                keyboard.SetMaximumLength(maxLength);
            }

            keyboard.SetInitialText(initial ?? string.Empty);
            return await keyboard.ShowAsync(this);
        }
        finally
        {
            _printerKeyboardOpen = false;
        }
    }

    private static void RefreshPrinterFieldLabel(Label label, Entry entry, string placeholder)
    {
        var value = entry.Text?.Trim();
        var empty = string.IsNullOrWhiteSpace(value);
        label.Text = empty ? placeholder : value;
        label.TextColor = Color.FromArgb(empty ? "#94A3B8" : "#0F172A");
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
                var labels = ServiceHelper.GetService<LabelPrintDatabaseService>();
                if (labels != null)
                    await labels.EnsureBuiltInProfilesAsync();
            }

            RoutingModePicker.ItemsSource = new[] { "Dedicated Printers", "One Printer For All" };
            
            System.Diagnostics.Debug.WriteLine(" Printer services initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error initializing services: {ex.Message}");
        }
    }

    private bool _logoDialogOpen;

    private async Task LoadReceiptLogoSizeAsync()
    {
        if (_receiptLogoSettingsService == null)
        {
            return;
        }

        _receiptLogoSize = await _receiptLogoSettingsService.GetLogoSizeAsync();
    }

    private async void OnLogoUpdateClicked(object? sender, EventArgs e)
    {
        SelectToolbarButton(LogoUpdateButton);
        if (_logoDialogOpen)
        {
            return;
        }

        _logoDialogOpen = true;
        try
        {
            await ShowLogoUpdateDialogAsync();
        }
        finally
        {
            _logoDialogOpen = false;
        }
    }

    private async Task ShowLogoUpdateDialogAsync()
    {
        var businessService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        var business = await businessService.GetBusinessInfoAsync(forceRefresh: true);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = false;

        var preview = new Image
        {
            Aspect = Aspect.AspectFit,
            Margin = 8,
            IsVisible = false
        };
        var placeholder = new Label
        {
            Text = "No logo",
            FontSize = 12,
            TextColor = Color.FromArgb("#94A3B8"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var status = new Label
        {
            FontSize = 12,
            TextColor = Color.FromArgb("#64748B")
        };
        var sizeStatus = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#15803D")
        };

        Button MakeAction(string text, string textColor, string border)
        {
            return new Button
            {
                Text = text,
                BackgroundColor = Colors.White,
                TextColor = Color.FromArgb(textColor),
                BorderColor = Color.FromArgb(border),
                BorderWidth = 1,
                CornerRadius = 8,
                HeightRequest = 36,
                MinimumHeightRequest = 36,
                Padding = new Thickness(14, 0),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                Style = null,
                HorizontalOptions = LayoutOptions.Start
            };
        }

        var chooseButton = MakeAction("Choose logo", "#2563EB", "#BFDBFE");
        var removeButton = MakeAction("Remove", "#DC2626", "#FECACA");
        var smallButton = SizeButton("Small");
        var mediumButton = SizeButton("Medium");
        var largeButton = SizeButton("Large");
        var sizeButtons = new[] { smallButton, mediumButton, largeButton };

        void PaintLogo()
        {
            var path = business?.LogoPath;
            var hasLogo = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            preview.IsVisible = hasLogo;
            placeholder.IsVisible = !hasLogo;
            removeButton.IsVisible = hasLogo;
            if (hasLogo)
            {
                preview.Source = ImageSource.FromFile(path);
                status.Text = "Logo ready";
                status.TextColor = Color.FromArgb("#15803D");
            }
            else
            {
                preview.Source = null;
                status.Text = business is { Id: > 0 } ? "No logo uploaded" : "Save business information first";
                status.TextColor = Color.FromArgb("#64748B");
            }
        }

        void PaintSize()
        {
            SetLogoSizeButtonState(smallButton, _receiptLogoSize == ReceiptLogoSize.Small);
            SetLogoSizeButtonState(mediumButton, _receiptLogoSize == ReceiptLogoSize.Medium);
            SetLogoSizeButtonState(largeButton, _receiptLogoSize == ReceiptLogoSize.Large);
            sizeStatus.Text = _receiptLogoSize switch
            {
                ReceiptLogoSize.Small => "Selected: Small. About 60mm wide. Shortest header.",
                ReceiptLogoSize.Medium => "Selected: Medium. Best size for most receipts.",
                ReceiptLogoSize.Large => "Selected: Large. Full paper width, uses more paper.",
                _ => $"Selected: {_receiptLogoSize}"
            };
        }

        async Task SaveSizeAsync(ReceiptLogoSize size)
        {
            if (busy || _receiptLogoSettingsService == null)
            {
                return;
            }

            busy = true;
            foreach (var button in sizeButtons)
            {
                button.IsEnabled = false;
            }

            var saved = await _receiptLogoSettingsService.SaveLogoSizeAsync(size);
            foreach (var button in sizeButtons)
            {
                button.IsEnabled = true;
            }

            busy = false;
            if (!saved)
            {
                sizeStatus.Text = "Could not save size";
                sizeStatus.TextColor = Color.FromArgb("#DC2626");
                return;
            }

            _receiptLogoSize = size;
            sizeStatus.TextColor = Color.FromArgb("#15803D");
            PaintSize();
        }

        chooseButton.Clicked += async (_, _) =>
        {
            if (busy)
            {
                return;
            }

            if (business == null || business.Id <= 0)
            {
                status.Text = "Save business information first";
                status.TextColor = Color.FromArgb("#DC2626");
                return;
            }

            try
            {
                var file = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select restaurant logo",
                    FileTypes = FilePickerFileType.Images
                });
                if (file == null)
                {
                    return;
                }

                busy = true;
                chooseButton.IsEnabled = false;
                status.Text = "Uploading...";
                status.TextColor = Color.FromArgb("#D97706");

                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                var bytes = memory.ToArray();
                if (bytes.Length > 5 * 1024 * 1024)
                {
                    status.Text = "File too large (max 5MB)";
                    status.TextColor = Color.FromArgb("#DC2626");
                    return;
                }

                var savedPath = await businessService.SaveBusinessLogoAsync(business.Id, file.FileName, file.ContentType, bytes);
                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    status.Text = "Upload failed";
                    status.TextColor = Color.FromArgb("#DC2626");
                    return;
                }

                business.LogoPath = savedPath;
                PaintLogo();
            }
            catch (Exception ex)
            {
                status.Text = "Upload failed";
                status.TextColor = Color.FromArgb("#DC2626");
                System.Diagnostics.Debug.WriteLine($"Logo upload failed: {ex.Message}");
            }
            finally
            {
                busy = false;
                chooseButton.IsEnabled = true;
            }
        };

        var confirmRemove = false;
        removeButton.Clicked += async (_, _) =>
        {
            if (busy || business == null || business.Id <= 0)
            {
                return;
            }

            if (!confirmRemove)
            {
                confirmRemove = true;
                removeButton.Text = "Confirm remove";
                status.Text = "Tap again to remove the logo";
                status.TextColor = Color.FromArgb("#DC2626");
                return;
            }

            busy = true;
            removeButton.IsEnabled = false;
            var removed = await businessService.RemoveBusinessLogoAsync(business.Id);
            if (removed && !string.IsNullOrWhiteSpace(business.LogoPath) && File.Exists(business.LogoPath))
            {
                try
                {
                    File.Delete(business.LogoPath);
                }
                catch
                {
                    // The database logo is already cleared.
                }
            }

            busy = false;
            removeButton.IsEnabled = true;
            confirmRemove = false;
            removeButton.Text = "Remove";
            if (!removed)
            {
                status.Text = "Could not remove logo";
                status.TextColor = Color.FromArgb("#DC2626");
                return;
            }

            business.LogoPath = null;
            PaintLogo();
        };

        smallButton.Clicked += async (_, _) => await SaveSizeAsync(ReceiptLogoSize.Small);
        mediumButton.Clicked += async (_, _) => await SaveSizeAsync(ReceiptLogoSize.Medium);
        largeButton.Clicked += async (_, _) => await SaveSizeAsync(ReceiptLogoSize.Large);

        var close = new Button
        {
            Text = "Done",
            BackgroundColor = Color.FromArgb("#0F172A"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            Style = null
        };
        close.Clicked += (_, _) => done.TrySetResult(true);

        PaintLogo();
        PaintSize();

        var previewBox = new Border
        {
            WidthRequest = 88,
            HeightRequest = 88,
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new Grid
            {
                Children = { placeholder, preview }
            }
        };
        var uploadActions = new VerticalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                chooseButton,
                new Label
                {
                    Text = "PNG or JPG, up to 5MB",
                    FontSize = 11,
                    TextColor = Color.FromArgb("#94A3B8")
                },
                status,
                removeButton
            }
        };
        Grid.SetColumn(previewBox, 0);
        Grid.SetColumn(uploadActions, 1);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = new Thickness(20, 18),
            WidthRequest = 440,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label
                    {
                        Text = "Receipt logo",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A")
                    },
                    new Label
                    {
                        Text = "This logo prints at the top of customer receipts.",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#64748B"),
                        Margin = new Thickness(0, -8, 0, 0)
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitionCollection
                        {
                            new ColumnDefinition(GridLength.Auto),
                            new ColumnDefinition(GridLength.Star)
                        },
                        ColumnSpacing = 14,
                        Children = { previewBox, uploadActions }
                    },
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#F1F5F9") },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#F8FAFC"),
                        Stroke = Color.FromArgb("#E2E8F0"),
                        StrokeThickness = 1,
                        Padding = new Thickness(12, 10),
                        StrokeShape = new RoundRectangle { CornerRadius = 10 },
                        Content = new VerticalStackLayout
                        {
                            Spacing = 4,
                            Children =
                            {
                                new Label
                                {
                                    Text = "Best file",
                                    FontSize = 12,
                                    FontAttributes = FontAttributes.Bold,
                                    TextColor = Color.FromArgb("#0F172A")
                                },
                                new Label
                                {
                                    Text = "PNG, dark logo on white or transparent. About 600 x 300 px if wide, or 500 x 500 if square. Under 300 px looks soft on the printer.",
                                    FontSize = 11,
                                    TextColor = Color.FromArgb("#64748B")
                                },
                                new Label
                                {
                                    Text = "Printed size",
                                    FontSize = 12,
                                    FontAttributes = FontAttributes.Bold,
                                    TextColor = Color.FromArgb("#0F172A"),
                                    Margin = new Thickness(0, 4, 0, 0)
                                },
                                new Label
                                {
                                    Text = "Small is the shortest header. Medium is the best size for most 80mm receipts. Large uses the full paper width and more paper.",
                                    FontSize = 11,
                                    TextColor = Color.FromArgb("#64748B")
                                }
                            }
                        }
                    },
                    new Label
                    {
                        Text = "Size on the receipt",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A")
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { smallButton, mediumButton, largeButton }
                    },
                    sizeStatus,
                    close
                }
            }
        };

        var overlay = new ContentView
        {
            BackgroundColor = Color.FromArgb("#660F172A"),
            Content = card
        };
        await OrderPlaceDialogPresenter.ShowAsync(this, overlay, done);
    }

    private static Button SizeButton(string text) => new()
    {
        Text = text,
        CornerRadius = 8,
        HeightRequest = 36,
        MinimumHeightRequest = 36,
        MinimumWidthRequest = 88,
        Padding = new Thickness(14, 0),
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        Style = null
    };

    private static void SetLogoSizeButtonState(Button button, bool isSelected)
    {
        button.Style = null;
        button.BackgroundColor = Color.FromArgb(isSelected ? "#2563EB" : "#F8FAFC");
        button.TextColor = Color.FromArgb(isSelected ? "#FFFFFF" : "#334155");
        button.BorderColor = Color.FromArgb(isSelected ? "#2563EB" : "#E2E8F0");
        button.BorderWidth = 1;
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

                FillAddedPrintersList(printers);
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
        SelectToolbarButton(AddPrinterButton);
        _editingPrinter = null;
        FormTitle.Text = "Add Printer";
        SavePrinterButton.Text = "Save Printer";
        ResetForm();
        ShowPrinterForm();
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
        
        SelectTechnology(InferTechnology(printer), printer.Brand);
        SelectType(printer.PrinterType);
        SelectEpsonModel(printer.ModelCode);
        
        // Set width
        SelectWidth(printer.PaperWidth);
        
        // Set features
        CashDrawerCheckbox.IsChecked = printer.HasCashDrawer;
        CutterCheckbox.IsChecked = printer.HasCutter;
        BuzzerCheckbox.IsChecked = printer.HasBuzzer;
        TwoColorCheckbox.IsChecked = printer.SupportsTwoColor;

        if (printer.PrinterType == NetworkPrinterType.Label)
        {
            SelectLabelModel(printer.Brand, printer.ModelCode);
            ApplyLabelSize(LabelSizeIndex(printer));
        }
        
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
        
        ShowPrinterForm();
        _ = PrinterPageScroll.ScrollToAsync(0, 0, false);
    }

    private void FillAddedPrintersList(IReadOnlyList<NetworkPrinter> printers)
    {
        AddedPrintersList.Children.Clear();
        var connected = printers.Count(p => p.IsOnline);
        var offline = printers.Count - connected;
        AddedPrintersSummary.Text = printers.Count == 0
            ? "0 connected"
            : $"{connected} connected, {offline} offline";
        NoAddedPrintersLabel.IsVisible = printers.Count == 0;

        foreach (var printer in printers.OrderBy(p => p.Name))
        {
            AddedPrintersList.Children.Add(CreateAddedPrinterRow(printer));
        }
    }

    private View CreateAddedPrinterRow(NetworkPrinter printer)
    {
        var edit = new Button
        {
            Text = "Edit",
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#2563EB"),
            BorderColor = Color.FromArgb("#BFDBFE"),
            BorderWidth = 1,
            CornerRadius = 8,
            HeightRequest = 30,
            MinimumHeightRequest = 30,
            Padding = new Thickness(12, 0),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Style = null,
            VerticalOptions = LayoutOptions.Center
        };
        edit.Clicked += (_, _) => OnEditPrinterClicked(printer);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(180)),
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            Padding = new Thickness(8, 6),
            Children =
            {
                new BoxView
                {
                    WidthRequest = 8,
                    HeightRequest = 8,
                    CornerRadius = 4,
                    Color = Color.FromArgb(printer.IsOnline ? "#16A34A" : "#DC2626"),
                    VerticalOptions = LayoutOptions.Center
                },
                new VerticalStackLayout
                {
                    Spacing = 0,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = printer.Name,
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#0F172A"),
                            LineBreakMode = LineBreakMode.TailTruncation
                        },
                        new Label
                        {
                            Text = printer.PrinterType.ToString(),
                            FontSize = 11,
                            TextColor = Color.FromArgb("#94A3B8")
                        }
                    }
                },
                new Label
                {
                    Text = $"{printer.IpAddress}:{printer.Port}",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#334155"),
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = printer.IsOnline ? "Connected" : "Offline",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(printer.IsOnline ? "#15803D" : "#DC2626"),
                    VerticalOptions = LayoutOptions.Center
                },
                edit
            }
        };
        row.SetColumn(row.Children[1], 1);
        row.SetColumn(row.Children[2], 2);
        row.SetColumn(row.Children[3], 3);
        row.SetColumn(edit, 4);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = row
        };
    }

    private void OnCloseFormClicked(object? sender, EventArgs e)
    {
        HidePrinterForm();
        ClearToolbarSelection();
    }

    private void ShowPrinterForm()
    {
        PrinterListLayout.IsVisible = false;
        FormPanel.IsVisible = true;
    }

    private void HidePrinterForm()
    {
        FormPanel.IsVisible = false;
        PrinterListLayout.IsVisible = true;
        _editingPrinter = null;
    }

    private void ResetForm()
    {
        PrinterNameEntry.Text = string.Empty;
        IpAddressEntry.Text = string.Empty;
        PortEntry.Text = "9100";
        
        SelectTechnology("thermal", PrinterBrand.Epson);
        SelectType(NetworkPrinterType.Receipt);
        SelectWidth(PaperWidth.Mm80);
        
        CashDrawerCheckbox.IsChecked = false;
        CutterCheckbox.IsChecked = true;
        BuzzerCheckbox.IsChecked = false;
        TwoColorCheckbox.IsChecked = false;

        LabelProfilePicker.SelectedIndex = 0;
        ApplyLabelSize(0);
        
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

        if (EpsonModelSection.IsVisible && EpsonModelPicker.SelectedIndex < 0)
        {
            await AppAlertService.ShowAlertAsync("Model", "Select the printer model.");
            return;
        }

        try
        {
            SavePrinterButton.IsEnabled = false;

            if (await _dbService.EndpointExistsAsync(ip, port, _editingPrinter?.Id))
            {
                await AppAlertService.ShowAlertAsync("Duplicate Printer", "Another printer already uses this IP address and port.");
                return;
            }

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
            printer.Technology = _selectedTechnology;
            printer.Manufacturer = _selectedBrand.ToString().ToLowerInvariant();
            printer.ModelCode = EpsonModelSection.IsVisible
                ? ModelCodeFromLabel(EpsonModelPicker.SelectedItem?.ToString())
                : printer.ModelCode;
            if (_selectedTechnology == "thermal")
            {
                _selectedWidth = PaperWidth.Mm80;
            }
            printer.PaperWidth = _selectedWidth;
            printer.HasCashDrawer = CashDrawerCheckbox.IsChecked;
            printer.HasCutter = CutterCheckbox.IsChecked;
            printer.HasBuzzer = BuzzerCheckbox.IsChecked;
            printer.SupportsTwoColor = TwoColorCheckbox.IsChecked;
            printer.IsEnabled = _selectedType == NetworkPrinterType.Label ? LabelEnabledCheckbox.IsChecked : true;

            if (_selectedType == NetworkPrinterType.Label && _selectedBrand is PrinterBrand.Toshiba or PrinterBrand.Xprinter or PrinterBrand.Brother)
            {
                printer.LabelProfile = LabelProfilePicker.SelectedItem?.ToString();
                printer.LabelMediaProfileId = LabelPrinterProfiles.MediaProfileId(_selectedBrand, LabelProfilePicker.SelectedIndex);
                printer.MediaWidthMm = ParseDecimal(MediaWidthEntry.Text);
                printer.LabelWidthMm = ParseDecimal(LabelWidthEntry.Text);
                printer.LabelHeightMm = ParseDecimal(LabelHeightEntry.Text);
                printer.GapSizeMm = ParseDecimal(GapSizeEntry.Text);
                printer.SensorType = SensorTypePicker.SelectedIndex switch
                {
                    1 => LabelSensorType.BlackMark,
                    2 => LabelSensorType.Continuous,
                    0 => LabelSensorType.Gap,
                    _ => null
                };
                printer.PrintSpeed = ParseInt(PrintSpeedEntry.Text);
                printer.PrintDarkness = ParseInt(PrintDarknessEntry.Text);
                printer.HorizontalOffsetMm = ParseDecimal(HorizontalOffsetEntry.Text) ?? decimal.MinValue;
                printer.VerticalOffsetMm = ParseDecimal(VerticalOffsetEntry.Text) ?? decimal.MinValue;
                printer.FinishingMode = FinishingModePicker.SelectedIndex == 1 ? LabelFinishingMode.Cutter : LabelFinishingMode.TearOff;
                printer.NumberOfCopies = ParseInt(NumberOfCopiesEntry.Text) ?? 0;
                var alreadyDefault = printer.IsDefaultLabelPrinter;
                printer.IsDefaultLabelPrinter = alreadyDefault || !await HasAnotherLabelPrinterAsync(printer.Id);
                if (_selectedBrand == PrinterBrand.Xprinter)
                    LabelPrinterProfiles.ApplyXp421bLan(printer);
                else if (_selectedBrand == PrinterBrand.Brother)
                    LabelPrinterProfiles.ApplyTd4420Dn(printer);
                else
                    LabelPrinterProfiles.ApplyToshibaBfv4dGs14(printer);

                var validationError = LabelPrinterConfigurationValidator.Validate(printer, CutterInstalledCheckbox.IsChecked);
                if (validationError != null)
                {
                    await AppAlertService.ShowAlertAsync("Invalid Label Printer", validationError);
                    return;
                }
            }
            
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

            if (printer.PrinterType == NetworkPrinterType.Label && printer.IsDefaultLabelPrinter)
            {
                await _dbService.SetDefaultLabelPrinterAsync(printer.Id);
            }

            if (!string.Equals(previousPrintGroupId, printer.PrintGroupId, StringComparison.OrdinalIgnoreCase))
            {
                await ClearPrintGroupPrinterIfUnusedAsync(previousPrintGroupId, printer.Id);
            }

            HidePrinterForm();
            ClearToolbarSelection();
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

    private void ClearToolbarSelection()
    {
        foreach (var button in ToolbarButtons())
        {
            button.Style = null;
            button.BackgroundColor = Colors.White;
            button.TextColor = Color.FromArgb("#334155");
            button.BorderColor = Color.FromArgb("#E2E8F0");
            button.BorderWidth = 1;
        }
    }

    private IEnumerable<Button> ToolbarButtons()
    {
        yield return RefreshButton;
        yield return CheckStatusButton;
        yield return ManageQueueButton;
        yield return OpenDrawerButton;
        yield return LogoUpdateButton;
        yield return PrintDesignButton;
        yield return AddPrinterButton;
    }

    private void SelectToolbarButton(Button selected)
    {
        foreach (var button in ToolbarButtons())
        {
            var isSelected = ReferenceEquals(button, selected);
            button.Style = null;
            button.BackgroundColor = Color.FromArgb(isSelected ? "#2563EB" : "#FFFFFF");
            button.TextColor = isSelected ? Colors.White : Color.FromArgb("#334155");
            button.BorderColor = Color.FromArgb(isSelected ? "#2563EB" : "#E2E8F0");
            button.BorderWidth = 1;
        }

        if (!ReferenceEquals(selected, AddPrinterButton))
        {
            HidePrinterForm();
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        SelectToolbarButton(RefreshButton);
        await LoadPrintersAsync();
        await LoadRoutingSettingsAsync();
    }

    private Task ShowStatusCheckNoticeAsync(int online, int offline)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ok = new Button
        {
            Text = "OK",
            BackgroundColor = Color.FromArgb("#0F172A"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            Style = null
        };
        ok.Clicked += (_, _) => done.TrySetResult(true);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = new Thickness(22, 20),
            WidthRequest = 360,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Opacity = 0.14f,
                Radius = 18,
                Offset = new Point(0, 8)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label
                    {
                        Text = "Status Check",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitionCollection
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Star)
                        },
                        ColumnSpacing = 10,
                        Children =
                        {
                            StatusChip("Online", online, "#16A34A"),
                            StatusChip("Offline", offline, "#DC2626", column: 1)
                        }
                    },
                    ok
                }
            }
        };

        var overlay = new ContentView
        {
            BackgroundColor = Color.FromArgb("#660F172A"),
            Content = card
        };
        return OrderPlaceDialogPresenter.ShowAsync(this, overlay, done);
    }

    private static Border StatusChip(string title, int count, string color, int column = 0)
    {
        var chip = new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new HorizontalStackLayout
                    {
                        Spacing = 6,
                        HorizontalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new BoxView
                            {
                                WidthRequest = 8,
                                HeightRequest = 8,
                                CornerRadius = 4,
                                Color = Color.FromArgb(color),
                                VerticalOptions = LayoutOptions.Center
                            },
                            new Label
                            {
                                Text = title,
                                FontSize = 12,
                                TextColor = Color.FromArgb("#64748B"),
                                VerticalOptions = LayoutOptions.Center
                            }
                        }
                    },
                    new Label
                    {
                        Text = count.ToString(),
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };
        Grid.SetColumn(chip, column);
        return chip;
    }

    private async void OnCheckStatusClicked(object? sender, EventArgs e)
    {
        SelectToolbarButton(CheckStatusButton);
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
            
            await ShowStatusCheckNoticeAsync(_healthService.OnlinePrinters, _healthService.OfflinePrinters);
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Check failed: {ex.Message}");
        }
        finally
        {
            CheckStatusButton.IsEnabled = true;
            CheckStatusButton.Text = "Check status";
        }
    }

    private async void OnOpenDrawerClicked(object? sender, EventArgs e)
    {
        SelectToolbarButton(OpenDrawerButton);
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
        SelectToolbarButton(PrintDesignButton);
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
        SelectToolbarButton(ManageQueueButton);
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
            if (printer.PrinterType == NetworkPrinterType.Label && _dbService != null)
            {
                var physicallyPrinted = await DisplayAlert(
                    "Confirm Physical Label",
                    $"The test was sent to '{printer.Name}'. Did a readable label physically print on the correct media?",
                    "Yes, it printed",
                    "No");
                if (physicallyPrinted)
                {
                    await _dbService.RecordSuccessfulPhysicalTestAsync(printer.Id);
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Physical label test recorded.");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Not Confirmed", "The connection succeeded, but no successful physical test was recorded.");
                }
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Test print sent to '{printer.Name}'");
            }
        }
        else
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send test print to '{printer.Name}'");
        }
    }

    private async Task<bool> SendPrinterTestAsync(NetworkPrinter printer)
    {
        if (printer.PrinterType == NetworkPrinterType.Label
            && printer.ModuleIdentifier is "toshiba_tpcl" or "xprinter_tspl" or "brother_raster")
        {
            var labels = ServiceHelper.GetService<LabelPrintDatabaseService>();
            var queue = ServiceHelper.GetService<LabelPrintQueueService>();
            if (labels == null || queue == null || printer.Id <= 0 || string.IsNullOrWhiteSpace(printer.LabelMediaProfileId))
                return false;

            await labels.EnsureBuiltInProfilesAsync();
            var modelName = printer.ModuleIdentifier switch
            {
                "xprinter_tspl" => "Xprinter XP-421B",
                "brother_raster" => "Brother TD-4420DN",
                _ => "Toshiba B-FV4D"
            };
            var template = printer.ModuleIdentifier switch
            {
                "xprinter_tspl" => "xprinter-tspl-v1",
                "brother_raster" => "brother-raster-v1",
                _ => "toshiba-text-v1"
            };
            var job = new LabelPrintJob
            {
                PrinterId = printer.Id,
                MediaProfileId = printer.LabelMediaProfileId,
                SourceTerminal = "mother-test",
                IdempotencyKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"test|{printer.Id}|{DateTime.UtcNow:O}|{Guid.NewGuid():N}"))).ToLowerInvariant(),
                SendRevision = 1,
                JobType = LabelJobType.Test,
                Content = new LabelContentSnapshot(
                    "item",
                    "OrderWeb POS",
                    1,
                    [modelName, "TEST LABEL"],
                    $"{printer.IpAddress}:{printer.Port}",
                    DateTimeOffset.Now,
                    printer.LabelProfile),
                LabelTemplateVersion = template,
                QuantityCopies = 1,
                Status = LabelJobStatus.Pending
            };
            await labels.EnqueueAsync(job);
            await queue.ProcessQueueAsync();
            return true;
        }

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

    private void OnTechThermalTapped(object? sender, EventArgs e) => SelectTechnology("thermal");
    private void OnTechImpactTapped(object? sender, EventArgs e) => SelectTechnology("impact");
    private void OnTechLabelTapped(object? sender, EventArgs e) => SelectTechnology("label");

    private void OnBrandEpsonTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Epson);
    private void OnBrandStarTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Star);
    private void OnBrandXprinterTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Xprinter);
    private void OnBrandToshibaTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Toshiba);
    private void OnBrandBrotherTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Brother);
    private void OnBrandOtherTapped(object? sender, EventArgs e) => SelectBrand(PrinterBrand.Other);

    private void SelectTechnology(string technology, PrinterBrand? keepBrand = null)
    {
        _selectedTechnology = technology;
        PaintChoice(TechThermalBorder, TechThermalLabel, technology == "thermal");
        PaintChoice(TechImpactBorder, TechImpactLabel, technology == "impact");
        PaintChoice(TechLabelBorder, TechLabelLabel, technology == "label");

        var allowed = BrandsFor(technology);
        BrandEpsonBorder.IsVisible = allowed.Contains(PrinterBrand.Epson);
        BrandStarBorder.IsVisible = allowed.Contains(PrinterBrand.Star);
        BrandXprinterBorder.IsVisible = allowed.Contains(PrinterBrand.Xprinter);
        BrandToshibaBorder.IsVisible = allowed.Contains(PrinterBrand.Toshiba);
        BrandBrotherBorder.IsVisible = allowed.Contains(PrinterBrand.Brother);
        BrandOtherBorder.IsVisible = keepBrand == PrinterBrand.Other;

        var brand = keepBrand is PrinterBrand chosen && (allowed.Contains(chosen) || chosen == PrinterBrand.Other)
            ? chosen
            : allowed[0];
        SelectBrand(brand);

        TypeReceiptBorder.IsVisible = technology == "thermal";
        TypeOnlineBorder.IsVisible = technology == "thermal";
        TypeKitchenBorder.IsVisible = technology != "label";
        TypeBarBorder.IsVisible = technology != "label";
        TypeTakeawayBorder.IsVisible = technology != "label";
        TypeLabelBorder.IsVisible = technology == "label";
        PurposeHintLabel.Text = technology switch
        {
            "impact" => "Kitchen, bar, or takeaway only.",
            "label" => "Label printers only.",
            _ => "Select what this printer will be used for."
        };
        PurposeSection.IsVisible = technology != "label";
        PrintGroupSection.IsVisible = technology != "label";
        ModelCard.IsVisible = technology != "label";
        Grid.SetColumnSpan(BrandCard, technology == "label" ? 2 : 1);

        if (technology == "label")
        {
            SelectType(NetworkPrinterType.Label);
        }
        else if (_selectedType == NetworkPrinterType.Label
                 || (technology == "impact" && _selectedType is NetworkPrinterType.Receipt or NetworkPrinterType.Online))
        {
            SelectType(technology == "impact" ? NetworkPrinterType.Kitchen : NetworkPrinterType.Receipt);
        }

        if (technology == "thermal")
        {
            _selectedWidth = PaperWidth.Mm80;
            GeneralPaperWidthSection.IsVisible = false;
        }
    }

    private static PrinterBrand[] BrandsFor(string technology) => technology switch
    {
        "impact" => [PrinterBrand.Epson, PrinterBrand.Star],
        "label" => [PrinterBrand.Toshiba, PrinterBrand.Brother, PrinterBrand.Xprinter],
        _ => [PrinterBrand.Epson, PrinterBrand.Star, PrinterBrand.Xprinter]
    };

    private static string InferTechnology(NetworkPrinter printer)
    {
        var saved = printer.Technology?.Trim().ToLowerInvariant();
        if (saved is "thermal" or "impact" or "label")
        {
            return saved;
        }

        return printer.PrinterType == NetworkPrinterType.Label
               || printer.Brand is PrinterBrand.Toshiba or PrinterBrand.Brother
            ? "label"
            : "thermal";
    }

    private void SelectBrand(PrinterBrand brand)
    {
        _selectedBrand = brand;
        PaintChoice(BrandEpsonBorder, BrandEpsonLabel, brand == PrinterBrand.Epson);
        PaintChoice(BrandStarBorder, BrandStarLabel, brand == PrinterBrand.Star);
        RefreshEpsonModels();
        PaintChoice(BrandXprinterBorder, BrandXprinterLabel, brand == PrinterBrand.Xprinter);
        PaintChoice(BrandToshibaBorder, BrandToshibaLabel, brand == PrinterBrand.Toshiba);
        PaintChoice(BrandBrotherBorder, BrandBrotherLabel, brand == PrinterBrand.Brother);
        PaintChoice(BrandOtherBorder, BrandOtherLabel, brand == PrinterBrand.Other);
        if (_selectedTechnology == "label")
        {
            RefreshLabelForm();
        }
    }

    private static readonly string[] EpsonThermalModels =
    [
        "TM-T20",
        "TM-T20II",
        "TM-T20III",
        "TM-T20X",
        "TM-T70II",
        "TM-T88VI",
        "TM-T88VII"
    ];

    private static readonly string[] EpsonImpactModels = ["TM-U220B", "TM-U220IIB"];

    private static readonly string[] XprinterThermalModels =
    [
        "XP-T80Q (Recommended)",
        "XP-N160II (Recommended)",
        "XP-Q200",
        "XP-Q300",
        "XP-Q200II"
    ];

    private static readonly string[] StarThermalModels =
    [
        "TSP654IIE3",
        "TSP654IIE3X",
        "TSP743IIE3",
        "TSP743IIE3X",
        "TSP847IIE3",
        "TSP847IIE3X"
    ];

    private static readonly string[] StarImpactModels = ["SP742", "SP700 Series"];

    private void RefreshEpsonModels(string? keepModel = null)
    {
        var show = _selectedTechnology == "thermal" && _selectedBrand is PrinterBrand.Epson or PrinterBrand.Xprinter or PrinterBrand.Star
                   || _selectedTechnology == "impact" && _selectedBrand is PrinterBrand.Epson or PrinterBrand.Star;
        EpsonModelSection.IsVisible = show;
        if (!show)
        {
            return;
        }

        var models = (_selectedBrand, _selectedTechnology) switch
        {
            (PrinterBrand.Xprinter, _) => XprinterThermalModels,
            (PrinterBrand.Star, "impact") => StarImpactModels,
            (PrinterBrand.Star, _) => StarThermalModels,
            (_, "impact") => EpsonImpactModels,
            _ => EpsonThermalModels
        };
        ModelHintLabel.Text = (_selectedBrand, _selectedTechnology) switch
        {
            (PrinterBrand.Xprinter, _) => "XP-T80Q and XP-N160II are the recommended models. 80mm. Connect by IP on port 9100. No PC driver.",
            (PrinterBrand.Star, "thermal") => "Ethernet only. Switch the printer to ESC/POS first (TSP847: DSW1-1 and DSW1-2 OFF, and fit the 80mm guide). Then connect by IP on port 9100. No PC driver.",
            (_, "impact") => "Connect by IP on port 9100. No PC driver.",
            _ => "80mm is fixed for thermal. Mother connects by IP on port 9100. No PC driver."
        };
        var selected = ModelCodeFromLabel(keepModel ?? EpsonModelPicker.SelectedItem?.ToString());
        EpsonModelPicker.ItemsSource = models;
        var index = Array.FindIndex(models, model => string.Equals(ModelCodeFromLabel(model), selected, StringComparison.OrdinalIgnoreCase));
        EpsonModelPicker.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string? ModelCodeFromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        const string recommended = " (Recommended)";
        return label.EndsWith(recommended, StringComparison.Ordinal)
            ? label[..^recommended.Length]
            : label.Trim();
    }

    private void SelectEpsonModel(string? modelCode) => RefreshEpsonModels(modelCode);

    private static void PaintChoice(Border border, Label label, bool selected)
    {
        border.BackgroundColor = selected ? Color.FromArgb("#0F172A") : Colors.White;
        border.Stroke = Color.FromArgb(selected ? "#0F172A" : "#E2E8F0");
        border.StrokeThickness = 1;
        label.TextColor = selected ? Colors.White : Color.FromArgb("#334155");
    }

    private static void PaintPurpose(Border border, bool selected)
    {
        border.BackgroundColor = Colors.White;
        border.Stroke = Color.FromArgb(selected ? "#F59E0B" : "#E2E8F0");
        border.StrokeThickness = selected ? 1.5 : 1;
        if (border.Content is Label label)
        {
            label.TextColor = Color.FromArgb(selected ? "#D97706" : "#334155");
        }
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
        PaintPurpose(TypeReceiptBorder, type == NetworkPrinterType.Receipt);
        PaintPurpose(TypeKitchenBorder, type == NetworkPrinterType.Kitchen);
        PaintPurpose(TypeBarBorder, type == NetworkPrinterType.Bar);
        PaintPurpose(TypeOnlineBorder, type == NetworkPrinterType.Online);
        PaintPurpose(TypeTakeawayBorder, type == NetworkPrinterType.Takeaway);
        PaintPurpose(TypeLabelBorder, type == NetworkPrinterType.Label);
        
        // Auto-set buzzer for kitchen/bar/takeaway
        if (type == NetworkPrinterType.Kitchen || type == NetworkPrinterType.Bar || type == NetworkPrinterType.Takeaway)
        {
            BuzzerCheckbox.IsChecked = true;
        }

        var isLabel = type == NetworkPrinterType.Label;
        RefreshLabelForm();
        GeneralPaperWidthSection.IsVisible = !isLabel && _selectedTechnology != "thermal";
        if (_selectedTechnology == "thermal")
        {
            _selectedWidth = PaperWidth.Mm80;
        }
        GeneralFeaturesSection.IsVisible = !isLabel;
        if (isLabel && string.IsNullOrWhiteSpace(PortEntry.Text))
        {
            PortEntry.Text = "9100";
        }
    }

    private static readonly (string Label, PrinterBrand Brand)[] LabelPrinterModels =
    [
        ("Xprinter XP-421B", PrinterBrand.Xprinter),
        ("Brother TD-4420DN", PrinterBrand.Brother),
        ("Toshiba B-FV4D-GS14", PrinterBrand.Toshiba)
    ];

    private void RefreshLabelForm()
    {
        var show = _selectedType == NetworkPrinterType.Label
                   && _selectedBrand is PrinterBrand.Toshiba or PrinterBrand.Xprinter or PrinterBrand.Brother
                   && _selectedTechnology == "label";
        LabelConfigurationSection.IsVisible = show;
        if (!show)
            return;

        var profiles = _selectedBrand switch
        {
            PrinterBrand.Xprinter => new[] { "Xprinter 60 × 40 mm Container", "Xprinter 51 × 30 mm Compact", "Xprinter 80 × 50 mm Delivery" },
            PrinterBrand.Brother => new[] { "Brother 60 × 40 mm Container", "Brother 51 × 30 mm Compact", "Brother 80 × 50 mm Delivery" },
            _ => new[] { "Toshiba 60 × 40 mm Container", "Toshiba 51 × 30 mm Compact", "Toshiba 80 × 50 mm Delivery" }
        };
        SetPickerItems(LabelProfilePicker, profiles);
        ApplyLabelSize(_labelSizeIndex);
        if (string.IsNullOrWhiteSpace(PortEntry.Text))
            PortEntry.Text = "9100";
    }

    private int _labelSizeIndex;

    private void OnLabelSizeNormalTapped(object? sender, EventArgs e) => ApplyLabelSize(0);
    private void OnLabelSizeSmallTapped(object? sender, EventArgs e) => ApplyLabelSize(1);
    private void OnLabelSizeDeliveryTapped(object? sender, EventArgs e) => ApplyLabelSize(2);

    private static int LabelSizeIndex(NetworkPrinter printer)
    {
        var width = printer.LabelWidthMm ?? printer.MediaWidthMm ?? 60m;
        var height = printer.LabelHeightMm ?? 40m;
        if (width == 51m && height == 30m)
            return 1;
        if (width == 80m && height == 50m)
            return 2;
        return 0;
    }

    private void ApplyLabelSize(int index)
    {
        _labelSizeIndex = index is 1 or 2 ? index : 0;
        var (width, height) = _labelSizeIndex switch
        {
            1 => ("51", "30"),
            2 => ("80", "50"),
            _ => ("60", "40")
        };
        PaintLabelSize(LabelSizeNormalBorder, LabelSizeNormalLabel, LabelSizeNormalHint, _labelSizeIndex == 0);
        PaintLabelSize(LabelSizeSmallBorder, LabelSizeSmallLabel, LabelSizeSmallHint, _labelSizeIndex == 1);
        PaintLabelSize(LabelSizeDeliveryBorder, LabelSizeDeliveryLabel, LabelSizeDeliveryHint, _labelSizeIndex == 2);
        if (LabelProfilePicker.Items.Count > _labelSizeIndex)
            LabelProfilePicker.SelectedIndex = _labelSizeIndex;
        MediaWidthEntry.Text = width;
        LabelWidthEntry.Text = width;
        LabelHeightEntry.Text = height;
        GapSizeEntry.Text = "3";
        SensorTypePicker.SelectedIndex = 0;
        PrintSpeedEntry.Text = "4";
        PrintDarknessEntry.Text = "0";
        HorizontalOffsetEntry.Text = "0";
        VerticalOffsetEntry.Text = "0";
        FinishingModePicker.SelectedIndex = 0;
        NumberOfCopiesEntry.Text = "1";
        CutterInstalledCheckbox.IsChecked = false;
        LabelEnabledCheckbox.IsChecked = true;
    }

    private static void PaintLabelSize(Border border, Label title, Label hint, bool selected)
    {
        border.BackgroundColor = selected ? Color.FromArgb("#0F172A") : Color.FromArgb("#F8FAFC");
        border.Stroke = selected ? Colors.Transparent : Color.FromArgb("#E2E8F0");
        border.StrokeThickness = selected ? 0 : 1;
        title.TextColor = selected ? Colors.White : Color.FromArgb("#334155");
        hint.TextColor = selected ? Color.FromArgb("#E2E8F0") : Color.FromArgb("#94A3B8");
    }

    private async Task<bool> HasAnotherLabelPrinterAsync(int currentId)
    {
        if (_dbService == null)
            return false;
        var printers = await _dbService.GetPrintersByTypeAsync(NetworkPrinterType.Label);
        return printers.Any(printer => printer.IsEnabled && printer.Id != currentId);
    }

    private void SelectLabelModel(PrinterBrand brand, string? modelCode)
    {
        var match = LabelPrinterModels.FirstOrDefault(model => model.Brand == brand);
        if (match.Label is null && !string.IsNullOrWhiteSpace(modelCode))
        {
            match = modelCode.ToLowerInvariant() switch
            {
                LabelPrinterProfiles.XprinterXp421bCode => LabelPrinterModels[0],
                LabelPrinterProfiles.BrotherTd4420DnCode => LabelPrinterModels[1],
                LabelPrinterProfiles.ToshibaBfv4dGs14Code => LabelPrinterModels[2],
                _ => default
            };
        }

        if (match.Label is not null)
            SelectBrand(match.Brand);
    }

    private static void SetPickerItems(Picker picker, string[] items)
    {
        var selected = picker.SelectedItem?.ToString();
        picker.Items.Clear();
        foreach (var item in items)
            picker.Items.Add(item);
        var index = Array.FindIndex(items, item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
        picker.SelectedIndex = index >= 0 ? index : 0;
    }

    private void OnLabelProfileChanged(object? sender, EventArgs e)
    {
        var dimensions = LabelProfilePicker.SelectedIndex switch
        {
            0 => (Width: "60", Height: "40"),
            1 => (Width: "51", Height: "30"),
            2 => (Width: "80", Height: "50"),
            _ => default
        };
        if (dimensions == default) return;
        MediaWidthEntry.Text = dimensions.Width;
        LabelWidthEntry.Text = dimensions.Width;
        LabelHeightEntry.Text = dimensions.Height;
        GapSizeEntry.Text = "3";
        SensorTypePicker.SelectedIndex = 0;
    }

    private void OnSensorTypeChanged(object? sender, EventArgs e)
    {
        if (SensorTypePicker.SelectedIndex == 2)
        {
            GapSizeEntry.Text = "0";
            GapSizeEntry.IsEnabled = false;
        }
        else
        {
            GapSizeEntry.IsEnabled = true;
            if (ParseDecimal(GapSizeEntry.Text) is null or <= 0)
                GapSizeEntry.Text = "3";
        }
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var current)) return current;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) ? invariant : null;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string FormatDecimal(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

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
