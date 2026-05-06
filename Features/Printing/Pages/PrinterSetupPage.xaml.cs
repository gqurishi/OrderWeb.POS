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
    private NetworkPrinter? _editingPrinter;
    private bool _isInitialized = false;
    private Timer? _refreshTimer;
    
    // Form state
    private PrinterBrand _selectedBrand = PrinterBrand.Epson;
    private NetworkPrinterType _selectedType = NetworkPrinterType.Receipt;
    private PaperWidth _selectedWidth = PaperWidth.Mm80;
    private string? _selectedPrintGroupId = null;

    public PrinterSetupPage()
    {
        InitializeComponent();
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
            System.Diagnostics.Debug.WriteLine($"❌ OnAppearing error: {ex.Message}");
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
            _printGroupService = ServiceHelper.GetService<PrintGroupService>();
            
            if (_dbService != null)
            {
                await _dbService.EnsureTablesExistAsync();
            }
            
            System.Diagnostics.Debug.WriteLine("✅ Printer services initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error initializing services: {ex.Message}");
        }
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            // Update health status
            if (_healthService != null)
            {
                OnlineCountLabel.Text = _healthService.OnlinePrinters.ToString();
                OfflineCountLabel.Text = _healthService.OfflinePrinters.ToString();
                
                if (_healthService.LastHealthCheck > DateTime.MinValue)
                {
                    LastCheckLabel.Text = _healthService.LastHealthCheck.ToString("HH:mm:ss");
                }
            }
            
            // Update queue status
            if (_queueService != null)
            {
                var stats = await _queueService.GetStatsAsync();
                QueueCountLabel.Text = stats.PendingJobs.ToString();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error updating status: {ex.Message}");
        }
    }

    private async Task LoadPrintersAsync()
    {
        if (_dbService == null) return;

        try
        {
            var printers = await _dbService.GetAllPrintersAsync();
            
            // Update count
            PrinterCountLabel.Text = $"{printers.Count} printer{(printers.Count != 1 ? "s" : "")} configured";

            // Clear containers
            ClearPrinterContainers();

            // Group printers by type
            var receiptPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Receipt).ToList();
            var kitchenPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Kitchen).ToList();
            var barPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Bar).ToList();
            var onlinePrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Online).ToList();
            var takeawayPrinters = printers.Where(p => p.PrinterType == NetworkPrinterType.Takeaway).ToList();

            // Populate receipt printers
            NoReceiptPrintersLabel.IsVisible = receiptPrinters.Count == 0;
            foreach (var printer in receiptPrinters)
            {
                ReceiptPrintersContainer.Children.Add(CreatePrinterCard(printer));
            }

            // Populate kitchen printers
            NoKitchenPrintersLabel.IsVisible = kitchenPrinters.Count == 0;
            foreach (var printer in kitchenPrinters)
            {
                KitchenPrintersContainer.Children.Add(CreatePrinterCard(printer));
            }

            // Populate bar printers
            NoBarPrintersLabel.IsVisible = barPrinters.Count == 0;
            foreach (var printer in barPrinters)
            {
                BarPrintersContainer.Children.Add(CreatePrinterCard(printer));
            }

            // Populate online order printers
            NoOnlinePrintersLabel.IsVisible = onlinePrinters.Count == 0;
            foreach (var printer in onlinePrinters)
            {
                OnlinePrintersContainer.Children.Add(CreatePrinterCard(printer));
            }

            // Populate takeaway printers
            NoTakeawayPrintersLabel.IsVisible = takeawayPrinters.Count == 0;
            foreach (var printer in takeawayPrinters)
            {
                TakeawayPrintersContainer.Children.Add(CreatePrinterCard(printer));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error loading printers: {ex.Message}");
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
        
        // Set print group - load the group name if exists
        if (!string.IsNullOrEmpty(printer.PrintGroupId))
        {
            LoadPrintGroupNameAsync(printer.PrintGroupId);
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

        if (!int.TryParse(PortEntry.Text, out int port))
        {
            port = 9100;
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

        if (!int.TryParse(PortEntry.Text, out int port))
        {
            port = 9100;
        }

        try
        {
            SavePrinterButton.IsEnabled = false;

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
            printer.IsEnabled = true;
            
            // Get or create print group from the entered name
            var printGroupName = PrintGroupEntry.Text?.Trim();
            if (!string.IsNullOrEmpty(printGroupName))
            {
                printer.PrintGroupId = await GetOrCreatePrintGroupIdAsync(printGroupName);
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
                await _dbService.AddPrinterAsync(printer);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Printer '{name}' added!");
            }

            FormPanel.IsVisible = false;
            _editingPrinter = null;
            await LoadPrintersAsync();
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

    private async Task<string> GetOrCreatePrintGroupIdAsync(string groupName)
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
                // Return existing group ID
                return existingGroup.Id;
            }
            
            // Create new print group
            var newGroup = new PrintGroup
            {
                Id = Guid.NewGuid().ToString(),
                Name = groupName,
                ColorCode = GetColorForGroupName(groupName),
                IsActive = true,
                DisplayOrder = allGroups.Count
            };
            
            await _printGroupService.CreatePrintGroupAsync(newGroup);
            System.Diagnostics.Debug.WriteLine($"✅ Created new print group: {groupName}");
            
            return newGroup.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error creating print group: {ex.Message}");
            return string.Empty;
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
            System.Diagnostics.Debug.WriteLine($"❌ Error loading print group: {ex.Message}");
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

    #endregion

    #region Printer Actions

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadPrintersAsync();
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
            var result = await _printerService.SendTestPrintAsync(printer);
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

        var receiptPrinters = await _dbService.GetPrintersByTypeAsync(NetworkPrinterType.Receipt);
        var drawerPrinter = receiptPrinters.FirstOrDefault(p => p.HasCashDrawer && p.IsEnabled);

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

    private async Task OnTestPrinterClicked(NetworkPrinter printer)
    {
        if (_printerService == null) return;

        var result = await _printerService.SendTestPrintAsync(printer);
        
        if (result)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"Test print sent to '{printer.Name}'");
        }
        else
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send test print to '{printer.Name}'");
        }
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
            await _dbService.DeletePrinterAsync(printer.Id);
            await LoadPrintersAsync();
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
