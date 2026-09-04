using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Controls;
using POS_in_NET.Views;
using System.Collections.ObjectModel;
using System.Globalization;

namespace POS_in_NET.Pages
{
    public partial class UnifiedSettingsPage : ContentPage, IQueryAttributable
    {
        // Tab state
        private Border currentActiveTab;
        private StackLayout currentActiveContent;
        
        // Services
        private readonly BusinessSettingsService _businessService;
        private readonly DatabaseService _databaseService;
        private readonly OnlineOrderApiService _orderWebService;
        private readonly AuthenticationService _authService;
        private readonly OrderNumberService _orderNumberService;
        private readonly DeliveryZoneService _deliveryZoneService;
        private readonly DatabaseBackupService _databaseBackupService;
        private readonly PostcodeLookupService _postcodeLookupService;
        private readonly TableServiceChargeSettingsService _tableServiceChargeSettingsService;
        private readonly OrderServiceAvailabilityService _orderServiceAvailabilityService;
        
        // Cloud Connect Services
        private OrderWebWebSocketService? _webSocketService;
        private OrderWebRestApiService? _restApiService;
        private CloudOrderService? _cloudOrderService;
        private OrderWebConnectionKeeperService? _connectionKeeper;
        private CloudConfiguration? _currentCloudConfig;
        private bool _isCloudApiKeyVisible = false;
        private bool _isCloudConnecting = false;
        private bool _userHasPendingCloudEdits = false;
        private System.Threading.Timer? _cloudStatusUpdateTimer;
        
        // Data models
        private BusinessInfo? _currentBusinessInfo;
        private ObservableCollection<User> _users;
        private ObservableCollection<DatabaseBackupFileInfo> _backupHistory;
        private UserRole? _selectedRole;
        private bool _hasLoadedInitialData;
        private bool _hasLoadedUsers;
        private bool _hasLoadedCloudSettings;
        private bool _hasLoadedOrderWebAddressSettings;
        private bool _hasLoadedDeliveryZones;
        private bool _hasLoadedBackupSection;
        private string _initialTab = "BusinessInfo";
        private TableServiceChargeSettings? _currentTableServiceChargeSettings;
        private bool _serviceChargeEnabled;
        private OrderServiceAvailabilitySettings _orderServices = new();
        private bool _loadingOrderServiceSwitches;

        public UnifiedSettingsPage()
        {
            InitializeComponent();
            TopBar.SetPageTitle("Settings");
            
            // Initialize services
            _businessService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _orderWebService = ServiceHelper.GetService<OnlineOrderApiService>() ?? new OnlineOrderApiService();
            _authService = AuthenticationService.Instance;
            _orderNumberService = new OrderNumberService(_databaseService);
            _deliveryZoneService = ServiceHelper.GetService<DeliveryZoneService>() ?? new DeliveryZoneService(_databaseService);
            _databaseBackupService = ServiceHelper.GetService<DatabaseBackupService>() ?? new DatabaseBackupService(_databaseService);
            _postcodeLookupService = ServiceHelper.GetService<PostcodeLookupService>() ?? new PostcodeLookupService(_databaseService);
            _tableServiceChargeSettingsService = ServiceHelper.GetService<TableServiceChargeSettingsService>()
                ?? new TableServiceChargeSettingsService(_databaseService, _authService);
            _orderServiceAvailabilityService = ServiceHelper.GetService<OrderServiceAvailabilityService>()
                ?? new OrderServiceAvailabilityService(_databaseService, _authService);
            
            // Initialize users collection
            _users = new ObservableCollection<User>();
            UsersCollectionView.ItemsSource = _users;
            _backupHistory = new ObservableCollection<DatabaseBackupFileInfo>();
            BackupHistoryCollectionView.ItemsSource = _backupHistory;
            
            // Initialize role selection overlay
            RoleSelectionOverlay.RoleSelected += OnRoleSelected;
            RoleSelectionOverlay.OverlayClosed += OnOverlayClosed;
            
            // Initialize edit user overlay
            EditUserOverlay.UserUpdated += OnUserUpdated;
            EditUserOverlay.EditRoleSelected += OnEditRoleSelected;
            EditUserOverlay.OverlayClosed += OnEditOverlayClosed;
            
            // Set initial active tab
            currentActiveTab = BusinessTabBorder;
            currentActiveContent = BusinessInfoContent;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = LoadInitialDataAsync();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("tab", out var tabValue) && tabValue is not null)
            {
                _initialTab = NormalizeSettingsTab(tabValue.ToString());
            }

            if (_hasLoadedInitialData)
            {
                ShowSettingsTab(_initialTab);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            
            StopCloudStatusTimer();

            if (_connectionKeeper != null)
            {
                _connectionKeeper.StatusChanged -= OnConnectionKeeperStatusChanged;
            }
        }

        private async Task LoadInitialDataAsync()
        {
            if (_hasLoadedInitialData)
            {
                return;
            }

            try
            {
                var performance = PosPerformanceMonitor.BeginDataLoad("Settings");
                System.Diagnostics.Debug.WriteLine("Loading initial data for Settings page");
                
                await LoadOrderServicesAsync();
                await MainThread.InvokeOnMainThreadAsync(() => ShowSettingsTab(_initialTab));

                await LoadBusinessInfoAsync();
                await LoadTableServiceChargeSettingsAsync();
                await LoadOrderNumberSettingsAsync();
                
                _hasLoadedInitialData = true;
                PosPerformanceMonitor.MarkDataVisible(performance);
                System.Diagnostics.Debug.WriteLine("Initial data loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading initial data: {ex.Message}");
            }
        }

        #region Tab Navigation
        private void OnBusinessTabClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Business tab clicked");
                UpdateTabAppearance(BusinessTabBorder);
                ShowContent("BusinessInfo");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Business tab click: {ex.Message}");
            }
        }

        private void OnUserTabClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("User tab clicked");
                UpdateTabAppearance(UserTabBorder);
                ShowContent("UserManagement");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in User tab click: {ex.Message}");
            }
        }

        private void OnOrderWebTabClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OrderWeb tab clicked");
                UpdateTabAppearance(OrderWebTabBorder);
                ShowContent("OrderWeb");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OrderWeb tab click: {ex.Message}");
            }
        }

        private void OnDeliveryZoneTabClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Delivery Zone tab clicked");
                UpdateTabAppearance(DeliveryZoneTabBorder);
                ShowContent("DeliveryZone");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Delivery Zone tab click: {ex.Message}");
            }
        }

        private void OnBackupTabClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Backup tab clicked");
                UpdateTabAppearance(BackupTabBorder);
                ShowContent("Backup");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Backup tab click: {ex.Message}");
            }
        }

        // Accordion toggle handlers for OrderWeb expandable sections
        private void OnCloudSectionToggled(object sender, TappedEventArgs e)
        {
            try
            {
                bool isVisible = CloudContent.IsVisible;
                CloudContent.IsVisible = !isVisible;
                CloudToggleIcon.Text = isVisible ? "▶" : "▼";
                System.Diagnostics.Debug.WriteLine($"Cloud section toggled: {!isVisible}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling cloud section: {ex.Message}");
            }
        }

        private void OnPostcodeSectionToggled(object sender, TappedEventArgs e)
        {
            try
            {
                var isVisible = PostcodeContent.IsVisible;
                PostcodeContent.IsVisible = !isVisible;
                PostcodeToggleIcon.Text = isVisible ? "▶" : "▼";
                if (!isVisible)
                {
                    _ = EnsureOrderWebAddressSettingsLoadedAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling postcode section: {ex.Message}");
            }
        }



        private void OnTabTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (sender is Border tappedTab)
                {
                    string tabName = tappedTab.ClassId;
                    System.Diagnostics.Debug.WriteLine($"Tab tapped: {tabName}");
                    
                    // Update tab appearance
                    UpdateTabAppearance(tappedTab);
                    
                    // Show corresponding content
                    ShowContent(tabName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in tab navigation: {ex.Message}");
            }
        }

        private void UpdateTabAppearance(Border selectedTab)
        {
            try
            {
                // Reset all tabs to inactive state
                BusinessTabBorder.BackgroundColor = Colors.Transparent;
                UserTabBorder.BackgroundColor = Colors.Transparent;
                OrderWebTabBorder.BackgroundColor = Colors.Transparent;
                DeliveryZoneTabBorder.BackgroundColor = Colors.Transparent;
                BackupTabBorder.BackgroundColor = Colors.Transparent;
                
                // Update button text colors for inactive state
                UpdateTabButtonColor(BusinessTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(UserTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(OrderWebTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(DeliveryZoneTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(BackupTabBorder, Color.FromArgb("#475569"));
                
                // Set selected tab to active state
                selectedTab.BackgroundColor = Color.FromArgb("#3B82F6");
                UpdateTabButtonColor(selectedTab, Colors.White);
                
                currentActiveTab = selectedTab;
                
                System.Diagnostics.Debug.WriteLine($"Updated tab appearance for: {selectedTab.ClassId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating tab appearance: {ex.Message}");
            }
        }
        
        private void UpdateTabButtonColor(Border tabBorder, Color textColor)
        {
            try
            {
                // Find the button within the tab border and update its text color
                if (tabBorder.Content is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Button button)
                        {
                            button.TextColor = textColor;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating tab button color: {ex.Message}");
            }
        }

        private void ShowContent(string contentType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ShowContent called with: {contentType}");
                
                // Hide all content sections
                BusinessInfoContent.IsVisible = false;
                UserInfoContent.IsVisible = false;
                OrderWebContent.IsVisible = false;
                DeliveryZoneContent.IsVisible = false;
                BackupContent.IsVisible = false;
                
                // Show selected content
                switch (contentType)
                {
                    case "BusinessInfo":
                        BusinessInfoContent.IsVisible = true;
                        currentActiveContent = BusinessInfoContent;
                        System.Diagnostics.Debug.WriteLine("Business Info content set to visible");
                        break;
                    case "UserManagement":
                        UserInfoContent.IsVisible = true;
                        currentActiveContent = UserInfoContent;

                        _ = EnsureUsersLoadedAsync();
                        
                        // Debug: Check if Name entry is properly loaded
                        System.Diagnostics.Debug.WriteLine($"NameEntry visibility: {NameEntry?.IsVisible}");
                        
                        System.Diagnostics.Debug.WriteLine("User Management content set to visible");
                        break;
                    case "OrderWeb":
                        OrderWebContent.IsVisible = true;
                        currentActiveContent = OrderWebContent;

                        InitializeCloudServices();
                        _ = EnsureCloudSettingsLoadedAsync();
                        _ = EnsureOrderWebAddressSettingsLoadedAsync();
                        
                        System.Diagnostics.Debug.WriteLine("OrderWeb content set to visible");
                        break;
                    case "DeliveryZone":
                        DeliveryZoneContent.IsVisible = true;
                        currentActiveContent = DeliveryZoneContent;
                        _ = EnsureDeliveryZonesLoadedAsync();
                        System.Diagnostics.Debug.WriteLine("Delivery Zone content set to visible");
                        break;
                    case "Backup":
                        BackupContent.IsVisible = true;
                        currentActiveContent = BackupContent;
                        _ = EnsureBackupSectionLoadedAsync();
                        System.Diagnostics.Debug.WriteLine("Backup content set to visible");
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine($"Unknown content type: {contentType}");
                        break;
                }
                
                System.Diagnostics.Debug.WriteLine($"ShowContent completed for: {contentType}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowContent: {ex.Message}");
            }
        }

        private void ShowSettingsTab(string tabName)
        {
            var normalizedTab = NormalizeSettingsTab(tabName);
            if (normalizedTab == "DeliveryZone" && !_orderServices.DeliveryEnabled)
            {
                normalizedTab = "BusinessInfo";
            }

            var selectedTab = normalizedTab switch
            {
                "UserManagement" => UserTabBorder,
                "OrderWeb" => OrderWebTabBorder,
                "DeliveryZone" => DeliveryZoneTabBorder,
                "Backup" => BackupTabBorder,
                _ => BusinessTabBorder
            };

            UpdateTabAppearance(selectedTab);
            ShowContent(normalizedTab);
        }

        private static string NormalizeSettingsTab(string? tabName)
        {
            return (tabName ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "user" or "users" or "usermanagement" => "UserManagement",
                "orderweb" or "cloud" or "cloudsettings" => "OrderWeb",
                "delivery" or "deliveryzone" => "DeliveryZone",
                "backup" => "Backup",
                _ => "BusinessInfo"
            };
        }

        private async Task EnsureUsersLoadedAsync()
        {
            if (_hasLoadedUsers)
            {
                return;
            }

            await LoadUsersAsync();
            _hasLoadedUsers = true;
        }

        private async Task EnsureCloudSettingsLoadedAsync()
        {
            if (_hasLoadedCloudSettings)
            {
                return;
            }

            await LoadCloudSettingsAsync();
            _hasLoadedCloudSettings = true;
        }

        private async Task EnsureOrderWebAddressSettingsLoadedAsync()
        {
            if (_hasLoadedOrderWebAddressSettings)
            {
                return;
            }

            await LoadOrderWebAddressSettingsAsync();
            _hasLoadedOrderWebAddressSettings = true;
        }

        private async Task EnsureDeliveryZonesLoadedAsync()
        {
            if (_hasLoadedDeliveryZones)
            {
                return;
            }

            await LoadDeliveryZonesAsync();
            _hasLoadedDeliveryZones = true;
        }

        private async Task EnsureBackupSectionLoadedAsync()
        {
            if (_hasLoadedBackupSection)
            {
                return;
            }

            await LoadBackupSectionAsync();
            _hasLoadedBackupSection = true;
        }
        #endregion

        #region Database Backup Management
        private async Task LoadBackupSectionAsync()
        {
            try
            {
                var config = TerminalConfigurationService.GetConfiguration();
                var isAdmin = _authService.CurrentUser?.Role == UserRole.Admin;
                var canUseBackup = isAdmin && TerminalRoleService.CanRunMotherJobs;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    BackupTerminalLabel.Text = $"{config.TerminalName} ({config.Mode})";
                    BackupDatabaseLabel.Text = config.DatabaseName;
                    BackupNowButton.IsEnabled = canUseBackup;
                    UploadRestoreButton.IsEnabled = canUseBackup;
                    VerifyDatabaseButton.IsEnabled = canUseBackup;
                    OpenBackupFolderButton.IsEnabled = isAdmin;

                    BackupStatusLabel.Text = canUseBackup
                        ? "Ready. Backup and restore actions are available on this Mother terminal."
                        : (!isAdmin
                            ? "Admin access is required for database backup and restore."
                            : "Backup and restore must be run from the Mother terminal.");
                    BackupStatusLabel.TextColor = canUseBackup ? Color.FromArgb("#047857") : Color.FromArgb("#B45309");
                });

                await LoadBackupHistoryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup section load error: {ex.Message}");
                BackupStatusLabel.Text = $"Could not load backup status: {ex.Message}";
                BackupStatusLabel.TextColor = Color.FromArgb("#DC2626");
            }
        }

        private async Task LoadBackupHistoryAsync()
        {
            var backups = await _databaseBackupService.GetBackupHistoryAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _backupHistory.Clear();
                foreach (var backup in backups)
                {
                    _backupHistory.Add(backup);
                }
            });
        }

        private bool CanRunBackupActions()
        {
            return _authService.CurrentUser?.Role == UserRole.Admin && TerminalRoleService.CanRunMotherJobs;
        }

        private async Task<bool> RequireBackupPermissionAsync()
        {
            if (_authService.CurrentUser?.Role != UserRole.Admin)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can use database backup and restore.");
                return false;
            }

            if (!TerminalRoleService.CanRunMotherJobs)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Mother Terminal Required", "Database backup and restore must be done on the Mother terminal.");
                return false;
            }

            return true;
        }

        private async void OnBackupNowClicked(object sender, EventArgs e)
        {
            if (!await RequireBackupPermissionAsync())
            {
                return;
            }

            try
            {
                BackupNowButton.IsEnabled = false;
                BackupNowButton.Text = "Backing up...";
                BackupStatusLabel.Text = "Creating database backup package...";
                BackupStatusLabel.TextColor = Color.FromArgb("#0369A1");

                var result = await _databaseBackupService.CreateBackupAsync("manual");
                BackupStatusLabel.Text = result.Message;
                BackupStatusLabel.TextColor = result.Success ? Color.FromArgb("#047857") : Color.FromArgb("#DC2626");

                await LoadBackupHistoryAsync();
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(result.Success ? "Backup Complete" : "Backup Failed", result.Message);
            }
            finally
            {
                BackupNowButton.Text = "Backup Now";
                BackupNowButton.IsEnabled = CanRunBackupActions();
            }
        }

        private async void OnExportBackupClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: DatabaseBackupFileInfo backup })
            {
                return;
            }

            await ShareBackupAsync(backup.FilePath);
        }

        private async Task ShareBackupAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Export Backup", "Backup file was not found.");
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export OrderWeb backup",
                File = new ShareFile(filePath)
            });
        }

        private async void OnOpenBackupFolderClicked(object sender, EventArgs e)
        {
            try
            {
                var folder = DatabaseBackupService.GetBackupFolder();
                Directory.CreateDirectory(folder);
                await Launcher.Default.OpenAsync(new OpenFileRequest("Open backup folder", new ReadOnlyFile(folder)));
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Backup Folder", $"Could not open backup folder: {ex.Message}");
            }
        }

        private async void OnRefreshBackupsClicked(object sender, EventArgs e)
        {
            await LoadBackupSectionAsync();
        }

        private async void OnVerifyDatabaseClicked(object sender, EventArgs e)
        {
            if (!await RequireBackupPermissionAsync())
            {
                return;
            }

            BackupStatusLabel.Text = "Verifying database...";
            BackupStatusLabel.TextColor = Color.FromArgb("#0369A1");
            var result = await _databaseBackupService.VerifyCurrentDatabaseAsync();
            BackupStatusLabel.Text = result.Message;
            BackupStatusLabel.TextColor = result.Success ? Color.FromArgb("#047857") : Color.FromArgb("#DC2626");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(result.Success ? "Database Verified" : "Verification Failed", result.Message);
        }

        private async void OnUploadRestoreBackupClicked(object sender, EventArgs e)
        {
            if (!await RequireBackupPermissionAsync())
            {
                return;
            }

            try
            {
                var file = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select OrderWeb backup file",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI, [".orderwebbackup"] },
                        { DevicePlatform.MacCatalyst, ["public.zip-archive"] },
                        { DevicePlatform.iOS, ["public.zip-archive"] },
                        { DevicePlatform.Android, ["application/zip"] }
                    })
                });

                if (file == null)
                {
                    return;
                }

                await RestoreBackupFileAsync(file.FullPath);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Restore Backup", $"Could not select backup: {ex.Message}");
            }
        }

        private async void OnRestoreHistoryBackupClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: DatabaseBackupFileInfo backup })
            {
                return;
            }

            if (!await RequireBackupPermissionAsync())
            {
                return;
            }

            await RestoreBackupFileAsync(backup.FilePath);
        }

        private async Task RestoreBackupFileAsync(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Restore Backup", "Backup file was not found.");
                return;
            }

            var verify = await _databaseBackupService.VerifyBackupAsync(backupPath);
            if (!verify.Success)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Restore Blocked", verify.Message);
                return;
            }

            var confirmWord = await DisplayPromptAsync(
                "Restore Database",
                "This will replace the current Mother database. Type RESTORE to continue.",
                "Continue",
                "Cancel",
                "RESTORE",
                maxLength: 7);

            if (!string.Equals(confirmWord, "RESTORE", StringComparison.Ordinal))
            {
                return;
            }

            var adminPin = await DisplayPromptAsync(
                "Admin Verification",
                "Enter an Admin PIN to restore the database.",
                "Restore",
                "Cancel",
                "Admin PIN",
                maxLength: 12,
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrWhiteSpace(adminPin))
            {
                return;
            }

            var login = await _authService.LoginAsync(adminPin.Trim(), adminPin.Trim());
            if (!login.Success || login.User?.Role != UserRole.Admin)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Admin PIN could not be verified.");
                return;
            }

            var finalConfirm = await DisplayAlert(
                "Final Confirmation",
                "A safety backup will be created first. The app may need to restart after restore.",
                "Restore Database",
                "Cancel");

            if (!finalConfirm)
            {
                return;
            }

            try
            {
                UploadRestoreButton.IsEnabled = false;
                BackupNowButton.IsEnabled = false;
                BackupStatusLabel.Text = "Restoring database. Do not close the app...";
                BackupStatusLabel.TextColor = Color.FromArgb("#B91C1C");

                await StopServicesForRestoreAsync();
                var result = await _databaseBackupService.RestoreBackupAsync(backupPath);
                BackupStatusLabel.Text = result.Message;
                BackupStatusLabel.TextColor = result.Success ? Color.FromArgb("#047857") : Color.FromArgb("#DC2626");
                await LoadBackupHistoryAsync();

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(result.Success ? "Restore Complete" : "Restore Failed", result.Message);
                if (result.Success)
                {
                    _hasLoadedInitialData = false;
                    await LoadInitialDataAsync();
                }
            }
            finally
            {
                UploadRestoreButton.IsEnabled = CanRunBackupActions();
                BackupNowButton.IsEnabled = CanRunBackupActions();
            }
        }

        private async Task StopServicesForRestoreAsync()
        {
            try
            {
                InitializeCloudServices();
                _cloudOrderService?.StopPolling();
                if (_webSocketService?.IsConnected == true)
                {
                    await _webSocketService.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Restore service stop warning: {ex.Message}");
            }
        }
        #endregion

        #region Delivery Zone Management
        private async Task LoadDeliveryZonesAsync()
        {
            try
            {
                await _deliveryZoneService.EnsureTablesAsync();
                var zones = await _deliveryZoneService.GetZonesAsync();
                var unassigned = await _deliveryZoneService.GetUnassignedPostcodesAsync();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    DeliveryZonesCollectionView.ItemsSource = zones;
                    UnassignedPostcodesCollectionView.ItemsSource = unassigned;
                    DeliveryZoneCountLabel.Text = zones.Count == 1 ? "1 zone" : $"{zones.Count} zones";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading delivery zones: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", $"Could not load delivery zones: {ex.Message}");
            }
        }

        private async void OnCreateDeliveryZoneClicked(object sender, EventArgs e)
        {
            var zoneName = DeliveryZoneNameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", "Enter a zone name.");
                return;
            }

            if (!decimal.TryParse(DeliveryZoneFeeEntry.Text?.Trim(), out var fee) || fee < 0)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", "Enter a valid delivery fee.");
                return;
            }

            try
            {
                await _deliveryZoneService.CreateZoneAsync(zoneName, fee);
                DeliveryZoneNameEntry.Text = string.Empty;
                DeliveryZoneFeeEntry.Text = string.Empty;
                await LoadDeliveryZonesAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", $"Could not create zone: {ex.Message}");
            }
        }

        private async void OnDeleteDeliveryZoneClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: DeliveryZone zone })
            {
                return;
            }

            var confirm = await DisplayAlert("Delete Zone", $"Delete {zone.Name} and all its postcodes?", "Delete", "Cancel");
            if (!confirm)
            {
                return;
            }

            try
            {
                await _deliveryZoneService.DeleteZoneAsync(zone.Id);
                await LoadDeliveryZonesAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", $"Could not delete zone: {ex.Message}");
            }
        }

        private async void OnAddPostcodeToZoneClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: DeliveryZone zone } button)
            {
                return;
            }

            var entry = FindSiblingEntry(button);
            var postcode = entry?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(postcode))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", "Enter a full postcode.");
                return;
            }

            try
            {
                await _deliveryZoneService.AddPostcodeAsync(zone.Id, postcode);
                if (entry != null)
                {
                    entry.Text = string.Empty;
                }
                await LoadDeliveryZonesAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", ex.Message);
            }
        }

        private async void OnRemoveDeliveryPostcodeClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: DeliveryZonePostcode postcode })
            {
                return;
            }

            try
            {
                await _deliveryZoneService.RemovePostcodeAsync(postcode.Id);
                await LoadDeliveryZonesAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", $"Could not remove postcode: {ex.Message}");
            }
        }

        private async void OnTestDeliveryZonePostcodeClicked(object sender, EventArgs e)
        {
            var rawPostcode = DeliveryZoneTestPostcodeEntry.Text?.Trim();
            var normalized = DeliveryZoneService.NormalizePostcode(rawPostcode);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                DeliveryZoneTestResultLabel.Text = "Enter a postcode to check.";
                DeliveryZoneTestResultLabel.TextColor = Color.FromArgb("#EF4444");
                return;
            }

            try
            {
                var match = await _deliveryZoneService.FindZoneForPostcodeAsync(normalized);
                if (match == null)
                {
                    DeliveryZoneTestResultLabel.Text = $"No delivery zone found for {normalized}.";
                    DeliveryZoneTestResultLabel.TextColor = Color.FromArgb("#B45309");
                }
                else
                {
                    DeliveryZoneTestResultLabel.Text = $"{normalized} matches {match.ZoneName}. Delivery fee £{match.DeliveryFee:F2}.";
                    DeliveryZoneTestResultLabel.TextColor = Color.FromArgb("#0F766E");
                }
            }
            catch (Exception ex)
            {
                DeliveryZoneTestResultLabel.Text = ex.Message;
                DeliveryZoneTestResultLabel.TextColor = Color.FromArgb("#EF4444");
            }
        }

        private void OnCopyUnassignedPostcodeClicked(object sender, EventArgs e)
        {
            if (sender is Button { CommandParameter: UnassignedDeliveryPostcode row })
            {
                DeliveryZoneTestPostcodeEntry.Text = row.Postcode;
                DeliveryZoneTestResultLabel.Text = $"{row.Postcode} copied. Add it to a zone postcode box when ready.";
                DeliveryZoneTestResultLabel.TextColor = Color.FromArgb("#64748B");
            }
        }

        private async void OnDeleteUnassignedPostcodeClicked(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: UnassignedDeliveryPostcode row })
            {
                return;
            }

            try
            {
                await _deliveryZoneService.DeleteUnassignedPostcodeAsync(row.Id);
                await LoadDeliveryZonesAsync();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delivery Zone", $"Could not delete postcode: {ex.Message}");
            }
        }

        private static Entry? FindSiblingEntry(Element element)
        {
            var parent = element.Parent;
            while (parent != null)
            {
                var entry = FindDescendantEntry(parent);
                if (entry != null)
                {
                    return entry;
                }

                parent = parent.Parent;
            }

            return null;
        }

        private static Entry? FindDescendantEntry(Element element)
        {
            if (element is Entry entry)
            {
                return entry;
            }

            if (element is Layout layout)
            {
                foreach (var child in layout.Children.OfType<Element>())
                {
                    var found = FindDescendantEntry(child);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            if (element is Border border && border.Content is Element borderContent)
            {
                return FindDescendantEntry(borderContent);
            }

            if (element is ContentView contentView && contentView.Content is Element content)
            {
                return FindDescendantEntry(content);
            }

            return null;
        }
        #endregion

        #region Business Info Management
        private async Task LoadBusinessInfoAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("LoadBusinessInfoAsync called");
                
                _currentBusinessInfo = await _businessService.GetBusinessInfoAsync();

                if (_currentBusinessInfo == null)
                {
                    _currentBusinessInfo = new BusinessInfo
                    {
                        RestaurantName = "",
                        PhoneNumber = "",
                        Email = "",
                        Address = "",
                        City = "",
                        County = "",
                        Country = "",
                        Postcode = "",
                        Website = "",
                        VATNumber = "",
                        TaxCode = ""
                    };
                }

                var businessInfo = _currentBusinessInfo;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    RestaurantNameEntry.Text = businessInfo.RestaurantName;
                    PhoneEntry.Text = businessInfo.PhoneNumber;
                    EmailEntry.Text = businessInfo.Email;
                    AddressEntry.Text = businessInfo.Address;
                    CityEntry.Text = businessInfo.City;
                    CountyEntry.Text = businessInfo.County;
                    CountryEntry.Text = businessInfo.Country;
                    PostcodeEntry.Text = businessInfo.Postcode;
                    WebsiteEntry.Text = businessInfo.Website;
                    VATNumberEntry.Text = businessInfo.VATNumber;

                    await LoadBusinessLogo();
                });
                
                System.Diagnostics.Debug.WriteLine("Business info loaded and UI populated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadBusinessInfoAsync error: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load business information: {ex.Message}");
            }
        }

        private async Task LoadBusinessLogo()
        {
            try
            {
                if (_currentBusinessInfo?.LogoPath != null && !string.IsNullOrEmpty(_currentBusinessInfo.LogoPath) && File.Exists(_currentBusinessInfo.LogoPath))
                {
                    LogoImage.Source = ImageSource.FromFile(_currentBusinessInfo.LogoPath);
                    LogoImage.IsVisible = true;
                    LogoPlaceholder.IsVisible = false;
                    LogoStatusLabel.Text = "Logo loaded";
                    LogoStatusLabel.TextColor = Colors.Green;
                    RemoveLogoButton.IsVisible = true;
                }
                else
                {
                    LogoStatusLabel.Text = "Ready to upload";
                    LogoStatusLabel.TextColor = Colors.Gray;
                    RemoveLogoButton.IsVisible = false;
                    LogoImage.IsVisible = false;
                    LogoPlaceholder.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadBusinessLogo error: {ex.Message}");
            }
        }

        private async Task LoadOrderServicesAsync()
        {
            var isAdmin = _authService.CurrentUser?.Role == UserRole.Admin;
            OrderServicesCard.IsVisible = isAdmin;

            try
            {
                _orderServices = await _orderServiceAvailabilityService.GetAsync(forceRefresh: true);
                _loadingOrderServiceSwitches = true;
                TableServiceSwitch.IsToggled = _orderServices.TableEnabled;
                CollectionServiceSwitch.IsToggled = _orderServices.CollectionEnabled;
                DeliveryServiceSwitch.IsToggled = _orderServices.DeliveryEnabled;
                _loadingOrderServiceSwitches = false;
                UpdateOrderServicesUi();
            }
            catch (Exception ex)
            {
                _loadingOrderServiceSwitches = false;
                SaveOrderServicesButton.IsEnabled = false;
                System.Diagnostics.Debug.WriteLine($"LoadOrderServicesAsync error: {ex.Message}");
                await AppAlertService.ShowAlertAsync("Order Services", ex.Message);
            }
        }

        private void OnOrderServiceSwitchToggled(object sender, ToggledEventArgs e)
        {
            if (!_loadingOrderServiceSwitches)
            {
                UpdateOrderServicesUi();
            }
        }

        private void UpdateOrderServicesUi()
        {
            SetOrderServiceState(TableServiceStateLabel, TableServiceSwitch.IsToggled);
            SetOrderServiceState(CollectionServiceStateLabel, CollectionServiceSwitch.IsToggled);
            SetOrderServiceState(DeliveryServiceStateLabel, DeliveryServiceSwitch.IsToggled);
            SaveOrderServicesButton.IsEnabled = _authService.CurrentUser?.Role == UserRole.Admin;
        }

        private static void SetOrderServiceState(Label label, bool enabled)
        {
            label.Text = enabled ? "Enabled" : "Disabled";
            label.TextColor = Color.FromArgb(enabled ? "#047857" : "#B91C1C");
        }

        private async void OnSaveOrderServicesClicked(object sender, EventArgs e)
        {
            if (_authService.CurrentUser?.Role != UserRole.Admin)
            {
                await AppAlertService.ShowAlertAsync("Access Denied", "Only an Administrator can change order services.");
                return;
            }

            var tableEnabled = TableServiceSwitch.IsToggled;
            var collectionEnabled = CollectionServiceSwitch.IsToggled;
            var deliveryEnabled = DeliveryServiceSwitch.IsToggled;
            if (!tableEnabled && !collectionEnabled && !deliveryEnabled)
            {
                await AppAlertService.ShowAlertAsync("Order Services", "Keep at least one order service enabled.");
                return;
            }

            var enabledNames = new List<string>();
            if (tableEnabled) enabledNames.Add("Table");
            if (collectionEnabled) enabledNames.Add("Collection");
            if (deliveryEnabled) enabledNames.Add("Delivery");

            var confirmation = new ModernConfirmDialog();
            confirmation.SetConfirm(
                "Save Order Services",
                $"Enable: {string.Join(", ", enabledNames)}?\n\nDisabled services will stop accepting new orders. Existing orders and history remain available.",
                "Save",
                "Cancel",
                string.Empty);
            if (!await confirmation.ShowAsync())
            {
                return;
            }

            SaveOrderServicesButton.IsEnabled = false;
            var originalText = SaveOrderServicesButton.Text;
            SaveOrderServicesButton.Text = "SAVING...";
            try
            {
                _orderServices = await _orderServiceAvailabilityService.SaveAsync(
                    tableEnabled,
                    collectionEnabled,
                    deliveryEnabled);

                TableServiceChargeCard.IsVisible = tableEnabled;
                DeliveryZoneTabBorder.IsVisible = deliveryEnabled;
                _ = ToastNotification.ShowAsync(
                    "Order services updated",
                    "New order options now match the selected services.",
                    NotificationType.Success,
                    1800);
            }
            catch (Exception ex)
            {
                await AppAlertService.ShowAlertAsync("Save Failed", ex.Message);
            }
            finally
            {
                SaveOrderServicesButton.Text = originalText;
                SaveOrderServicesButton.IsEnabled = true;
            }
        }

        private async Task LoadTableServiceChargeSettingsAsync()
        {
            var isAdmin = _authService.CurrentUser?.Role == UserRole.Admin;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TableServiceChargeCard.IsVisible = isAdmin && _orderServices.TableEnabled;
                DeliveryZoneTabBorder.IsVisible = _orderServices.DeliveryEnabled;
            });
            if (!isAdmin)
            {
                _currentTableServiceChargeSettings = null;
                return;
            }

            try
            {
                var settings = await _tableServiceChargeSettingsService.GetAsync();
                _currentTableServiceChargeSettings = settings.Copy();
                _serviceChargeEnabled = settings.IsEnabled;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ServiceChargePercentageEntry.Text = settings.IsEnabled
                        ? settings.Percentage.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;
                    UpdateTableServiceChargeUi();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTableServiceChargeSettingsAsync error: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TableServiceChargeCard.IsVisible = _orderServices.TableEnabled;
                    SaveServiceChargeButton.IsEnabled = false;
                });
                await AppAlertService.ShowAlertAsync("Service Charge", "The saved service-charge setting could not be loaded.");
            }
        }

        private void OnNoServiceChargeClicked(object sender, EventArgs e)
        {
            _serviceChargeEnabled = false;
            UpdateTableServiceChargeUi();
        }

        private void OnPercentageServiceChargeClicked(object sender, EventArgs e)
        {
            _serviceChargeEnabled = true;
            UpdateTableServiceChargeUi();
            ServiceChargePercentageEntry.Focus();
        }

        private void OnServiceChargePercentageTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTableServiceChargeDirtyState();
        }

        private void UpdateTableServiceChargeUi()
        {
            SetServiceChargeChoiceButton(NoServiceChargeButton, !_serviceChargeEnabled, "#334155");
            SetServiceChargeChoiceButton(PercentageServiceChargeButton, _serviceChargeEnabled, "#059669");
            ServiceChargePercentagePanel.IsVisible = _serviceChargeEnabled;

            UpdateTableServiceChargeDirtyState();
        }

        private static void SetServiceChargeChoiceButton(Button button, bool isSelected, string selectedColor)
        {
            button.BackgroundColor = Color.FromArgb(isSelected ? selectedColor : "#F1F5F9");
            button.TextColor = Color.FromArgb(isSelected ? "#FFFFFF" : "#475569");
            button.BorderColor = Color.FromArgb(isSelected ? selectedColor : "#CBD5E1");
            button.BorderWidth = 1;
        }

        private void UpdateTableServiceChargeDirtyState()
        {
            if (_currentTableServiceChargeSettings == null)
            {
                return;
            }

            var percentage = 0m;
            var inputIsValid = !_serviceChargeEnabled
                || (TryParseServiceChargePercentage(ServiceChargePercentageEntry.Text, out percentage)
                    && HasAtMostTwoDecimalPlaces(ServiceChargePercentageEntry.Text)
                    && TableServiceChargePolicy.Validate(true, percentage).IsValid);

            SaveServiceChargeButton.IsEnabled = _authService.CurrentUser?.Role == UserRole.Admin && inputIsValid;
        }

        private async void OnSaveServiceChargeClicked(object sender, EventArgs e)
        {
            if (_authService.CurrentUser?.Role != UserRole.Admin)
            {
                await AppAlertService.ShowAlertAsync("Access Denied", "Only an Administrator can change table service-charge settings.");
                return;
            }

            decimal percentage = 0m;
            if (_serviceChargeEnabled)
            {
                if (string.IsNullOrWhiteSpace(ServiceChargePercentageEntry.Text))
                {
                    await AppAlertService.ShowAlertAsync("Validation", "Enter the service-charge percentage.");
                    return;
                }

                if (!HasAtMostTwoDecimalPlaces(ServiceChargePercentageEntry.Text))
                {
                    await AppAlertService.ShowAlertAsync("Validation", "Use no more than two decimal places.");
                    return;
                }

                if (!TryParseServiceChargePercentage(ServiceChargePercentageEntry.Text, out percentage))
                {
                    await AppAlertService.ShowAlertAsync("Validation", "Enter a valid numeric percentage.");
                    return;
                }
            }

            var validation = TableServiceChargePolicy.Validate(_serviceChargeEnabled, percentage);
            if (!validation.IsValid)
            {
                await AppAlertService.ShowAlertAsync("Validation", validation.Message);
                return;
            }

            var newDescription = _serviceChargeEnabled
                ? $"{validation.NormalizedPercentage:0.##}% service charge"
                : "no service charge";
            var confirmation = new ModernConfirmDialog();
            confirmation.SetConfirm(
                "Confirm Table Service Charge",
                $"Save {newDescription} for new table orders?\n\nCollection and delivery orders are not affected. Existing open orders keep their original policy.",
                "Save Policy",
                "Cancel",
                "%",
                "#059669");

            if (!await confirmation.ShowAsync())
            {
                return;
            }

            var originalText = SaveServiceChargeButton.Text;
            SaveServiceChargeButton.IsEnabled = false;
            SaveServiceChargeButton.Text = "SAVING...";

            try
            {
                var result = await _tableServiceChargeSettingsService.SaveAsync(
                    _serviceChargeEnabled,
                    validation.NormalizedPercentage,
                    ServiceChargeClassification.Optional);

                _currentTableServiceChargeSettings = result.Settings.Copy();
                ServiceChargePercentageEntry.Text = result.Settings.IsEnabled
                    ? result.Settings.Percentage.ToString("0.##", CultureInfo.InvariantCulture)
                    : string.Empty;
                UpdateTableServiceChargeUi();

                await AppAlertService.ShowAlertAsync(
                    result.Changed ? "Saved" : "No Changes",
                    result.Changed
                        ? "Table service-charge policy saved and audited successfully."
                        : "The selected policy is already saved.");
            }
            catch (UnauthorizedAccessException ex)
            {
                await AppAlertService.ShowAlertAsync("Access Denied", ex.Message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnSaveServiceChargeClicked error: {ex.Message}");
                await AppAlertService.ShowAlertAsync("Error", "The service-charge policy could not be saved. No changes were committed.");
            }
            finally
            {
                SaveServiceChargeButton.Text = originalText;
                UpdateTableServiceChargeDirtyState();
            }
        }

        private static bool TryParseServiceChargePercentage(string? input, out decimal percentage)
        {
            var normalized = (input ?? string.Empty).Trim().TrimEnd('%').Trim();
            if (normalized.Contains(',') && !normalized.Contains('.'))
            {
                normalized = normalized.Replace(',', '.');
            }

            return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out percentage);
        }

        private static bool HasAtMostTwoDecimalPlaces(string? input)
        {
            var normalized = (input ?? string.Empty).Trim().TrimEnd('%').Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var separatorCount = normalized.Count(character => character is '.' or ',');
            if (separatorCount > 1 || normalized.Any(character => !char.IsDigit(character) && character is not '.' and not ','))
            {
                return false;
            }

            var separatorIndex = Math.Max(normalized.LastIndexOf('.'), normalized.LastIndexOf(','));
            return separatorIndex < 0 || normalized.Length - separatorIndex - 1 <= 2;
        }

        private async Task LoadOrderNumberSettingsAsync()
        {
            try
            {
                var settings = await _orderNumberService.GetCurrentSettingsAsync();
                var preview = await _orderNumberService.GetNextOrderNumbersPreviewAsync();
                var prefix = settings.Prefix;
                var exampleText = $"Example: {preview.Table}";

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    OrderPrefixEntry.Text = prefix;
                    OrderExampleLabel.Text = exampleText;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOrderNumberSettingsAsync error: {ex.Message}");
            }
        }

        private async void OnSaveBusinessClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Save Business Info clicked - basic test version");
                
                if (_currentBusinessInfo != null && _currentBusinessInfo.Id > 0)
                {
                    bool success = await _businessService.UpdateBusinessInfoAsync(_currentBusinessInfo, "Test User");
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", success ? "Business info updated successfully" : "Failed to update business info");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "No business info loaded to update");
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Error saving business info: {ex.Message}");
            }
        }

        private async void OnSaveBusinessInfoClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Save Business Info clicked");
                
                if (_currentBusinessInfo == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "No business info to save");
                    return;
                }

                // Update business info from form fields
                _currentBusinessInfo.RestaurantName = RestaurantNameEntry.Text ?? "";
                _currentBusinessInfo.PhoneNumber = PhoneEntry.Text ?? "";
                _currentBusinessInfo.Email = EmailEntry.Text ?? "";
                _currentBusinessInfo.Address = AddressEntry.Text ?? "";
                _currentBusinessInfo.City = CityEntry.Text ?? "";
                _currentBusinessInfo.County = CountyEntry.Text ?? "";
                _currentBusinessInfo.Country = CountryEntry.Text ?? "";
                _currentBusinessInfo.Postcode = PostcodeEntry.Text ?? "";
                _currentBusinessInfo.Website = WebsiteEntry.Text ?? "";
                _currentBusinessInfo.VATNumber = VATNumberEntry.Text ?? "";

                var orderPrefix = (OrderPrefixEntry.Text ?? string.Empty).Trim().ToUpper();
                if (orderPrefix.Length != 3 || !orderPrefix.All(char.IsLetter))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation", "Order code must be exactly 3 letters (A-Z).");
                    return;
                }

                // Show loading indicator
                LoadingIndicator.IsVisible = true;

                await _orderNumberService.UpdatePrefixAsync(orderPrefix);

                bool success;
                if (_currentBusinessInfo.Id > 0)
                {
                    success = await _businessService.UpdateBusinessInfoAsync(_currentBusinessInfo, "Current User");
                }
                else
                {
                    success = await _businessService.CreateBusinessInfoAsync(_currentBusinessInfo, "Current User");
                    if (success)
                    {
                        _currentBusinessInfo = await _businessService.GetBusinessInfoAsync() ?? _currentBusinessInfo;
                    }
                }

                LoadingIndicator.IsVisible = false;

                if (success)
                {
                    await LoadOrderNumberSettingsAsync();
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Business information saved successfully!");
                    
                    // Show success message
                    System.Diagnostics.Debug.WriteLine("Business information updated successfully!");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to save business information");
                }
            }
            catch (Exception ex)
            {
                LoadingIndicator.IsVisible = false;
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Error saving business info: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"OnSaveBusinessInfoClicked error: {ex.Message}");
            }
        }

        private async void OnSaveOrderCodeClicked(object sender, EventArgs e)
        {
            try
            {
                var orderPrefix = (OrderPrefixEntry.Text ?? string.Empty).Trim().ToUpper();
                OrderPrefixEntry.Text = orderPrefix;

                if (orderPrefix.Length != 3 || !orderPrefix.All(char.IsLetter))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation", "Order code must be exactly 3 letters (A-Z).");
                    return;
                }

                await _orderNumberService.UpdatePrefixAsync(orderPrefix);
                await LoadOrderNumberSettingsAsync();

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Order number code saved.");
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save order code: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"OnSaveOrderCodeClicked error: {ex.Message}");
            }
        }

        private async Task UploadSelectedLogo(FileResult file)
        {
            try
            {
                if (file == null) return;

                // Show loading
                LogoStatusLabel.Text = "Uploading...";
                LogoStatusLabel.TextColor = Colors.Orange;

                // Read file
                using var stream = await file.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                // Validate file size (5MB limit)
                const long maxSize = 5 * 1024 * 1024; // 5MB
                if (imageBytes.Length > maxSize)
                {
                    LogoStatusLabel.Text = "File too large (max 5MB)";
                    LogoStatusLabel.TextColor = Colors.Red;
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Logo file is too large. Please choose a file smaller than 5MB.");
                    return;
                }

                if (_currentBusinessInfo == null || _currentBusinessInfo.Id <= 0)
                {
                    LogoStatusLabel.Text = "Save business info first";
                    LogoStatusLabel.TextColor = Colors.Red;
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Business information must be saved before uploading a logo.");
                    return;
                }

                var savedFilePath = await _businessService.SaveBusinessLogoAsync(
                    _currentBusinessInfo.Id,
                    file.FileName,
                    file.ContentType,
                    imageBytes);

                if (string.IsNullOrWhiteSpace(savedFilePath))
                {
                    LogoStatusLabel.Text = "Upload failed";
                    LogoStatusLabel.TextColor = Colors.Red;
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Could not save logo to the shared database.");
                    return;
                }

                // Update UI from the persisted local file path
                LogoImage.Source = ImageSource.FromFile(savedFilePath);
                LogoImage.IsVisible = true;
                LogoPlaceholder.IsVisible = false;
                RemoveLogoButton.IsVisible = true;

                _currentBusinessInfo.LogoPath = savedFilePath;

                LogoStatusLabel.Text = "Logo uploaded";
                LogoStatusLabel.TextColor = Colors.Green;

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Logo uploaded successfully!");
            }
            catch (Exception ex)
            {
                LogoStatusLabel.Text = "Upload failed";
                LogoStatusLabel.TextColor = Colors.Red;
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to upload logo: {ex.Message}");
            }
        }

        private async Task RemoveLogoAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("RemoveLogo called");
                
                // Reset UI
                LogoImage.IsVisible = false;
                LogoPlaceholder.IsVisible = true;
                RemoveLogoButton.IsVisible = false;
                LogoStatusLabel.Text = "Ready to upload";
                LogoStatusLabel.TextColor = Colors.Gray;

                // Clear logo path
                if (_currentBusinessInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(_currentBusinessInfo.LogoPath) && File.Exists(_currentBusinessInfo.LogoPath))
                    {
                        File.Delete(_currentBusinessInfo.LogoPath);
                    }

                    _currentBusinessInfo.LogoPath = null;

                    if (_currentBusinessInfo.Id > 0)
                    {
                        await _businessService.RemoveBusinessLogoAsync(_currentBusinessInfo.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveLogo error: {ex.Message}");
            }
        }

        // New logo upload event handlers
        private async void OnUploadLogoClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Restaurant Logo",
                    FileTypes = FilePickerFileType.Images
                });

                if (result != null)
                {
                    await UploadSelectedLogo(result);
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to pick logo file: {ex.Message}");
            }
        }

        private void OnChangeLogoClicked(object sender, EventArgs e)
        {
            OnUploadLogoClicked(sender, e);
        }

        private async void OnRemoveLogoClicked(object sender, EventArgs e)
        {
            var result = await DisplayAlert("Confirm", "Are you sure you want to remove the current logo?", "Yes", "No");
            if (result)
            {
                await RemoveLogoAsync();
            }
        }

        private async void OnSelectLogoClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Select logo clicked");
        }
        #endregion

        #region User Management
        private void OnRolePickerTapped(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Role picker tapped - showing overlay");
                RoleSelectionOverlay.ShowOverlay();
                System.Diagnostics.Debug.WriteLine("Overlay shown successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing role picker overlay: {ex.Message}");
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to open role selection");
            }
        }

        private void OnSelectRoleClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Select role clicked - showing overlay");
                RoleSelectionOverlay.ShowOverlay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSelectRoleClicked: {ex.Message}");
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to open role selection");
            }
        }

        private async void OnAddUserClicked(object sender, EventArgs e)
        {
            try
            {
                string name = NameEntry.Text?.Trim();
                string pin = PINEntry.Text?.Trim();

                if (string.IsNullOrEmpty(name))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a user name");
                    return;
                }

                if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a 4-digit numeric PIN");
                    return;
                }

                if (_selectedRole == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please select a role");
                    return;
                }

                await AddUserAsync(name, pin, _selectedRole.Value);
                
                // Clear form
                NameEntry.Text = "";
                PINEntry.Text = "";
                SelectedRoleLabel.Text = "No role selected";
                _selectedRole = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnAddUserClicked: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to add user");
            }
        }

        private async Task AddUserAsync(string name, string pin, UserRole role)
        {
            try
            {
                var result = await _authService.CreateUserAsync(name, pin, pin, role);
                
                if (result.Success)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"User '{name}' created successfully");
                    await LoadUsersAsync(); // Refresh the users list
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to create user: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddUserAsync: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to create user");
            }
        }

        private void OnRoleSelected(object sender, UserRole selectedRole)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Role selected: {selectedRole}");
                
                // Check if edit overlay is visible - if so, update edit overlay
                if (EditUserOverlay.IsVisible)
                {
                    EditUserOverlay.UpdateSelectedRole(selectedRole);
                }
                else
                {
                    // Otherwise update create user form
                    _selectedRole = selectedRole;
                    SelectedRoleLabel.Text = selectedRole.ToString();
                }
                
                System.Diagnostics.Debug.WriteLine($"Role selection completed: {selectedRole}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling role selection: {ex.Message}");
            }
        }
        
        private void OnOverlayClosed(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Role selection overlay closed");
        }
        
        private async void OnUserUpdated(object sender, POS_in_NET.Views.UserUpdateEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Updating user: {e.User.Name}");
                
                // Update user using AuthenticationService
                var result = await _authService.UpdateUserAsync(e.User, e.NewPin);
                
                if (result.Success)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"User '{e.User.Name}' updated successfully");
                    
                    // Reload users to reflect changes
                    await LoadUsersAsync();
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", result.Message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating user: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to update user: {ex.Message}");
            }
        }
        
        private void OnEditRoleSelected(object sender, UserRole selectedRole)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Edit role picker tapped - showing overlay");
                RoleSelectionOverlay.ShowOverlay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing role selection overlay: {ex.Message}");
            }
        }
        
        private void OnEditOverlayClosed(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Edit user overlay closed");
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Loading users from database...");
                
                var users = await _authService.GetAllUsersAsync();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _users.Clear();
                    foreach (var user in users)
                    {
                        _users.Add(user);
                    }

                    UserCountLabel.Text = $"{_users.Count} {(_users.Count == 1 ? "User" : "Users")}";

                    System.Diagnostics.Debug.WriteLine($"Loaded {_users.Count} users from database");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading users: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load users: {ex.Message}");
            }
        }
        #endregion

        #region OrderWeb Management
        private async Task LoadOrderWebSettingsAsync()
        {
            // Simplified for testing
        }

        private async void OnSaveOrderWebClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Save OrderWeb settings clicked");
        }

        private async void OnTestOrderWebClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Test OrderWeb connection clicked");
        }
        #endregion

        #region Postcode Lookup Management

        private async Task LoadOrderWebAddressSettingsAsync()
        {
            try
            {
                var settings = await _postcodeLookupService.GetSettingsAsync();
                OrderWebAddressApiKeyEntry.Text = settings.OrderWebAddressApiKey ?? string.Empty;

                if (settings.TotalLookups > 0)
                {
                    OrderWebAddressStatsFrame.IsVisible = true;
                    OrderWebAddressUsageLabel.Text = $"Total Lookups: {settings.TotalLookups:N0}";
                    OrderWebAddressLastUsedLabel.Text = settings.LastUsed.HasValue
                        ? $"Last Used: {settings.LastUsed.Value:g}"
                        : "Last Used: Never";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOrderWebAddressSettingsAsync error: {ex.Message}");
            }
        }

        private void OnToggleOrderWebAddressApiKeyClicked(object sender, EventArgs e)
        {
            OrderWebAddressApiKeyEntry.IsPassword = !OrderWebAddressApiKeyEntry.IsPassword;
            ToggleOrderWebAddressApiKeyButton.Text = OrderWebAddressApiKeyEntry.IsPassword ? "Show" : "Hide";
        }

        private async void OnTestOrderWebAddressClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OrderWebAddressApiKeyEntry.Text))
            {
                await AppAlertService.ShowAlertAsync("Required", "Enter the OrderWeb address API key (owp_...).");
                return;
            }

            try
            {
                TestOrderWebAddressButton.IsEnabled = false;
                TestOrderWebAddressButton.Text = "...";
                await _postcodeLookupService.TestConnectionAsync(OrderWebAddressApiKeyEntry.Text.Trim());
                OrderWebAddressStatusFrame.IsVisible = true;
                OrderWebAddressStatusFrame.BackgroundColor = Color.FromArgb("#D1FAE5");
                OrderWebAddressStatusFrame.Stroke = Color.FromArgb("#10B981");
                OrderWebAddressStatusText.Text = "Connected successfully.";
                OrderWebAddressStatusText.TextColor = Color.FromArgb("#065F46");
                await AppAlertService.ShowAlertAsync("Success", "OrderWeb address lookup is working.");
            }
            catch (Exception ex)
            {
                OrderWebAddressStatusFrame.IsVisible = true;
                OrderWebAddressStatusFrame.BackgroundColor = Color.FromArgb("#FEE2E2");
                OrderWebAddressStatusFrame.Stroke = Color.FromArgb("#EF4444");
                OrderWebAddressStatusText.Text = ex.Message;
                OrderWebAddressStatusText.TextColor = Color.FromArgb("#991B1B");
                await AppAlertService.ShowAlertAsync("Test Failed", ex.Message);
            }
            finally
            {
                TestOrderWebAddressButton.IsEnabled = true;
                TestOrderWebAddressButton.Text = "Test";
            }
        }

        private async void OnSaveOrderWebAddressSettingsClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OrderWebAddressApiKeyEntry.Text))
            {
                await AppAlertService.ShowAlertAsync("Required", "Enter the OrderWeb address API key (owp_...).");
                return;
            }

            try
            {
                var settings = new PostcodeLookupSettings
                {
                    OrderWebAddressApiKey = OrderWebAddressApiKeyEntry.Text.Trim(),
                    OrderWebBaseUrl = OrderWebAddressLookupService.DefaultBaseUrl,
                    OrderWebAddressEnabled = true
                };

                if (await _postcodeLookupService.SaveSettingsAsync(settings))
                {
                    await AppAlertService.ShowAlertAsync("Saved", "OrderWeb address lookup settings saved.");
                    await LoadOrderWebAddressSettingsAsync();
                }
                else
                {
                    await AppAlertService.ShowAlertAsync("Error", "Could not save address lookup settings.");
                }
            }
            catch (Exception ex)
            {
                await AppAlertService.ShowAlertAsync("Error", ex.Message);
            }
        }

        // User Management Event Handlers
        private async void OnCreateUserClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Create User clicked");
                
                // Validate input fields
                if (string.IsNullOrWhiteSpace(NameEntry.Text))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a user name");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(PINEntry.Text) || PINEntry.Text.Length != 4 || !PINEntry.Text.All(char.IsDigit))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a 4-digit numeric PIN");
                    return;
                }
                
                if (_selectedRole == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please select a role");
                    return;
                }
                
                // Get form values
                var name = NameEntry.Text.Trim();
                var pin = PINEntry.Text.Trim();
                var role = _selectedRole.Value;
                
                // Create user using AuthenticationService
                var result = await _authService.CreateUserAsync(name, pin, pin, role);
                
                if (result.Success)
                {
                    // Show success message
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"User '{name}' created successfully with role '{role}'");
                    
                    // Clear form
                    NameEntry.Text = "";
                    PINEntry.Text = "";
                    _selectedRole = null;
                    SelectedRoleLabel.Text = "Select Role";
                    
                    // Reload users
                    await LoadUsersAsync();
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", result.Message);
                }
                
                System.Diagnostics.Debug.WriteLine($"User creation result: {result.Success} - {result.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating user: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to create user: {ex.Message}");
            }
        }

        private void OnEditUserClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Edit User clicked");
                
                if (sender is Button button && button.CommandParameter != null)
                {
                    // The CommandParameter is bound to the entire User object
                    if (button.CommandParameter is User userToEdit)
                    {
                        System.Diagnostics.Debug.WriteLine($"Opening edit dialog for user: {userToEdit.Name}");
                        EditUserOverlay.ShowOverlay(userToEdit);
                    }
                    else
                    {
                        _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Invalid user data");
                    }
                }
                else
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to determine which user to edit");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error editing user: {ex.Message}");
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to edit user: {ex.Message}");
            }
        }

        private async void OnDeleteUserClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Deactivate User clicked");
                
                if (sender is Button button && button.CommandParameter != null)
                {
                    // The CommandParameter is bound to the entire User object
                    if (button.CommandParameter is User userToDelete)
                    {
                        if (!userToDelete.IsActive)
                        {
                            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Already Inactive", $"User '{userToDelete.Name}' is already inactive.");
                            return;
                        }

                        bool confirm = await DisplayAlert("Confirm Deactivate",
                            $"Deactivate '{userToDelete.Name}'?\n\nThey will no longer be able to log in or clock in, but their clock history and reports will stay saved.",
                            "Deactivate", "Cancel");
                        
                        if (confirm)
                        {
                            var result = await _authService.DeactivateUserAsync(userToDelete.Id);
                            
                            if (result.Success)
                            {
                                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"User '{userToDelete.Name}' has been deactivated. Clock history is preserved.");
                                // Reload users
                                await LoadUsersAsync();
                            }
                            else
                            {
                                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", result.Message);
                            }
                        }
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Invalid user data");
                    }
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to determine which user to deactivate");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deactivating user: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to deactivate user: {ex.Message}");
            }
        }

        private async void OnPermanentDeleteUserClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Remove employee clicked");

                if (sender is not Button button || button.CommandParameter is not User userToDelete)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to determine which employee to remove");
                    return;
                }

                var displayName = string.IsNullOrWhiteSpace(userToDelete.Name)
                    ? userToDelete.Username
                    : userToDelete.Name;

                var confirm = await DisplayAlert(
                    "Remove Employee",
                    $"Remove '{displayName}' from POS access?\n\nThe employee will disappear from user management and can no longer sign in. Historical clock, discount, void and refund identity will be preserved.",
                    "Remove",
                    "Cancel");

                if (!confirm)
                {
                    return;
                }

                var finalConfirm = await DisplayAlert(
                    "Confirm Removal",
                    $"Remove '{displayName}' from the active employee list now?",
                    "Remove Now",
                    "Cancel");

                if (!finalConfirm)
                {
                    return;
                }

                var result = await _authService.DeleteUserAsync(userToDelete.Id);
                if (result.Success)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Employee Removed", result.Message);
                    await LoadUsersAsync();
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Removal Failed", result.Message);
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogFatal("Remove employee", ex);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to remove the employee. Check the production error log.");
            }
        }

        private async void OnUpdatePrefixClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Update Prefix clicked");
        }

        private async void OnToggleApiKeyClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Toggle API Key clicked");
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Connect clicked");
        }

        private async void OnSyncOrdersClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Sync Orders clicked");
        }

        private async void OnSyncHistoricalClicked(object sender, EventArgs e)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Test", "Sync Historical clicked");
        }
        #endregion

        #region Cloud Connect Handlers

        private void InitializeCloudServices()
        {
            try
            {
                _webSocketService = ServiceHelper.GetService<OrderWebWebSocketService>();
                _restApiService = ServiceHelper.GetService<OrderWebRestApiService>();
                _cloudOrderService = ServiceHelper.GetService<CloudOrderService>();
                _connectionKeeper = ServiceHelper.GetService<OrderWebConnectionKeeperService>();

                if (_connectionKeeper != null)
                {
                    _connectionKeeper.StatusChanged -= OnConnectionKeeperStatusChanged;
                    _connectionKeeper.StatusChanged += OnConnectionKeeperStatusChanged;
                    ApplyCloudStatusFromKeeper(_connectionKeeper.Status);
                }
                
                System.Diagnostics.Debug.WriteLine("Cloud services initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize cloud services: {ex.Message}");
            }
        }

        private void OnConnectionKeeperStatusChanged(OrderWebConnectionStatus status)
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyCloudStatusFromKeeper(status));
        }

        private void ApplyCloudStatusFromKeeper(OrderWebConnectionStatus status)
        {
            if (status.IsFullyOperational)
            {
                UpdateCloudConnectionStatus("Connected", "#10B981", status.StatusMessage);
            }
            else if (status.IsConfigured && !status.IsEnabled)
            {
                UpdateCloudConnectionStatus("Disabled", "#F59E0B", status.StatusMessage);
            }
            else if (status.IsConfigured && status.IsEnabled && status.IsApiHealthy && status.IsPollingActive)
            {
                UpdateCloudConnectionStatus("Partially Connected", "#F59E0B", status.StatusMessage);
            }
            else if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                UpdateCloudConnectionStatus("Connection Failed", "#EF4444", status.LastError);
            }
            else if (!status.IsConfigured)
            {
                UpdateCloudConnectionStatus("Not Connected", "#6C757D", status.StatusMessage);
            }
            else if (status.IsConfigured && status.IsEnabled && !status.IsApiHealthy)
            {
                UpdateCloudConnectionStatus("Not Connected", "#EF4444", status.StatusMessage);
            }
            else
            {
                UpdateCloudConnectionStatus("Connecting...", "#007BFF", status.StatusMessage);
            }

            CloudStatusConnectionIcon.Text = status.IsWebSocketConnected ? "" : "";
            CloudStatusConnectionText.Text = status.IsWebSocketConnected ? "Live" : "Offline";
            CloudStatusConnectionText.TextColor = Color.FromArgb(status.IsWebSocketConnected ? "#10B981" : "#EF4444");

            CloudStatusBackupIcon.Text = status.IsPollingActive ? "" : "⏸";
            CloudStatusBackupText.Text = status.IsPollingActive ? "Active" : "Stopped";
            CloudStatusBackupText.TextColor = Color.FromArgb(status.IsPollingActive ? "#10B981" : "#6B7280");

            if (status.LastHealthCheckUtc != default)
            {
                CloudStatusLastCheckText.Text = status.LastHealthCheckUtc.ToLocalTime().ToString("HH:mm:ss");
            }
        }

        private async Task LoadCloudSettingsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Cloud] Loading settings...");
                
                if (_databaseService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Cloud] Database service not available");
                    return;
                }
                
                // Load cloud configuration using DatabaseService
                _currentCloudConfig = await _databaseService.GetCloudConfigurationAsync();
                _userHasPendingCloudEdits = false;
                
                if (_currentCloudConfig != null)
                {
                    TenantSlugEntry.Text = _currentCloudConfig.TenantSlug ?? "";
                    CloudApiKeyEntry.Text = _currentCloudConfig.ApiKey ?? "";
                    
                    // Clean up REST API URL - remove tenant suffix if present
                    var restUrl = !string.IsNullOrWhiteSpace(_currentCloudConfig.RestApiBaseUrl)
                        ? _currentCloudConfig.RestApiBaseUrl
                        : _currentCloudConfig.ApiBaseUrl;
                    restUrl = string.IsNullOrWhiteSpace(restUrl) ? "https://orderweb.net/api" : restUrl;
                    if (restUrl.EndsWith($"/{_currentCloudConfig.TenantSlug}"))
                    {
                        restUrl = restUrl.Substring(0, restUrl.Length - _currentCloudConfig.TenantSlug.Length - 1);
                    }
                    CloudRestApiUrlEntry.Text = restUrl;

                    var wsUrl = string.IsNullOrWhiteSpace(_currentCloudConfig.WebSocketUrl)
                        ? "wss://orderweb.net/ws/pos"
                        : _currentCloudConfig.WebSocketUrl;
                    if (!string.IsNullOrWhiteSpace(_currentCloudConfig.TenantSlug) &&
                        wsUrl.EndsWith($"/{_currentCloudConfig.TenantSlug}", StringComparison.OrdinalIgnoreCase))
                    {
                        wsUrl = wsUrl[..^(_currentCloudConfig.TenantSlug.Length + 1)];
                    }
                    CloudWebSocketUrlEntry.Text = wsUrl;
                    ApplyCloudOnlineMasterUi(_currentCloudConfig);
                    
                    EnableCloudButtonsIfReady();

                    if (_currentCloudConfig.IsConfigured() && !_userHasPendingCloudEdits)
                    {
                        if (!_currentCloudConfig.IsEnabled)
                        {
                            UpdateCloudConnectionStatus(
                                "Disabled",
                                "#F59E0B",
                                "OrderWeb sync is disabled. Click Save Settings to enable, then Connect.");
                        }
                        else if (_connectionKeeper == null || !_connectionKeeper.Status.IsFullyOperational)
                        {
                            UpdateCloudConnectionStatus(
                                "Not Connected",
                                "#6C757D",
                                "Settings loaded. Click Connect to test and start live sync.");
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[Cloud] Settings loaded for tenant: {_currentCloudConfig.TenantSlug}");
                }
                else
                {
                    ApplyCloudOnlineMasterUi(null);
                }
                
                if (_connectionKeeper != null)
                {
                    ApplyCloudStatusFromKeeper(_connectionKeeper.Status);
                }
                
                // Start status update timer
                StartCloudStatusTimer();
                
                // Update initial status
                UpdateCloudSystemStatus();
                
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Failed to load settings: {ex.Message}");
            }
        }

        private void OnCloudFieldChanged(object sender, TextChangedEventArgs e)
        {
            _userHasPendingCloudEdits = true;
            EnableCloudButtonsIfReady();
        }

        private void OnCloudOnlineMasterToggled(object sender, ToggledEventArgs e)
        {
            _userHasPendingCloudEdits = true;
            EnableCloudButtonsIfReady();
        }

        private void ApplyCloudOnlineMasterUi(CloudConfiguration? cloudConfig)
        {
            var terminalConfig = TerminalConfigurationService.GetConfiguration();
            var isMother = terminalConfig.IsMother;
            var masterName = string.IsNullOrWhiteSpace(cloudConfig?.OnlineOrderMasterTerminalName)
                ? terminalConfig.TerminalName
                : cloudConfig!.OnlineOrderMasterTerminalName;

            CloudOnlineMasterSwitch.IsEnabled = isMother;
            CloudOnlineMasterSwitch.IsToggled = isMother && (cloudConfig?.OnlineOrderMasterEnabled ?? true);
            CloudOnlineMasterLabel.Text = isMother
                ? $"Master terminal: {masterName}. Turn on here to make this terminal receive online orders."
                : "Child terminal: local POS only. Online orders run on the mother/master terminal.";
        }

        private void EnableCloudButtonsIfReady()
        {
            bool hasRequiredFields = !string.IsNullOrWhiteSpace(TenantSlugEntry.Text) &&
                                   !string.IsNullOrWhiteSpace(CloudApiKeyEntry.Text) &&
                                   !string.IsNullOrWhiteSpace(CloudRestApiUrlEntry.Text);
            var canUseCloudControls = TerminalConfigurationService.IsConfigured &&
                                      TerminalConfigurationService.IsMotherTerminal;
            
            CloudConnectButton.IsEnabled = hasRequiredFields && canUseCloudControls && !_isCloudConnecting;
            CloudSaveSettingsButton.IsEnabled = hasRequiredFields && canUseCloudControls;
            CloudSyncOrdersButton.IsEnabled = hasRequiredFields && canUseCloudControls;
            CloudSyncHistoricalButton.IsEnabled = hasRequiredFields && canUseCloudControls;
        }

        private void OnToggleCloudApiKeyClicked(object sender, EventArgs e)
        {
            _isCloudApiKeyVisible = !_isCloudApiKeyVisible;
            CloudApiKeyEntry.IsPassword = !_isCloudApiKeyVisible;
            ToggleCloudApiKeyButton.Text = _isCloudApiKeyVisible ? "Hide" : "Show";
        }

        private CloudConfiguration BuildCloudConfigFromUi()
        {
            var tenantSlug = TenantSlugEntry.Text?.Trim() ?? "";
            var restApiUrl = CloudRestApiUrlEntry.Text?.Trim() ?? "https://orderweb.net/api";
            if (!string.IsNullOrWhiteSpace(tenantSlug) && restApiUrl.EndsWith($"/{tenantSlug}", StringComparison.OrdinalIgnoreCase))
            {
                restApiUrl = restApiUrl[..^(tenantSlug.Length + 1)];
            }

            var wsUrl = CloudWebSocketUrlEntry.Text?.Trim() ?? "wss://orderweb.net/ws/pos";
            if (!string.IsNullOrWhiteSpace(tenantSlug) && wsUrl.EndsWith($"/{tenantSlug}", StringComparison.OrdinalIgnoreCase))
            {
                wsUrl = wsUrl[..^(tenantSlug.Length + 1)];
            }

            return new CloudConfiguration
            {
                TenantSlug = tenantSlug,
                ApiKey = CloudApiKeyEntry.Text?.Trim() ?? "",
                RestApiBaseUrl = restApiUrl,
                ApiBaseUrl = restApiUrl,
                WebSocketUrl = wsUrl,
                IsEnabled = true,
                AutoPrintEnabled = _currentCloudConfig?.AutoPrintEnabled ?? true,
                NotificationsEnabled = _currentCloudConfig?.NotificationsEnabled ?? true,
                OnlineOrderMasterEnabled = CloudOnlineMasterSwitch.IsToggled,
                OnlineOrderMasterTerminalName = CloudOnlineMasterSwitch.IsToggled
                    ? TerminalConfigurationService.GetConfiguration().TerminalName
                    : (_currentCloudConfig?.OnlineOrderMasterTerminalName ?? "")
            };
        }

        private async Task<bool> SaveCloudConfigFromUiAsync()
        {
            if (_databaseService == null)
            {
                return false;
            }

            var config = BuildCloudConfigFromUi();
            if (string.IsNullOrWhiteSpace(config.TenantSlug) || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return false;
            }

            var saved = await _databaseService.SaveCloudConfigurationAsync(config);
            if (saved)
            {
                _currentCloudConfig = config;
            }

            return saved;
        }

        private void UpdateCloudConnectionStatus(string status, string colorHex, string detail)
        {
            CloudConnectionStatusText.Text = status;
            CloudConnectionStatusText.TextColor = Color.FromArgb(colorHex);
            CloudConnectionStatusDescription.Text = detail;

            if (status.Contains("Connected", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("Not Connected", StringComparison.OrdinalIgnoreCase))
            {
                CloudConnectionStatusFrame.BackgroundColor = Color.FromArgb("#D1FAE5");
                CloudConnectionStatusFrame.Stroke = Color.FromArgb("#10B981");
                CloudSystemStatusSection.IsVisible = true;
            }
            else if (status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                     status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                     status.Contains("Disconnected", StringComparison.OrdinalIgnoreCase))
            {
                CloudConnectionStatusFrame.BackgroundColor = Color.FromArgb("#FEE2E2");
                CloudConnectionStatusFrame.Stroke = Color.FromArgb("#EF4444");
            }
            else
            {
                CloudConnectionStatusFrame.BackgroundColor = Color.FromArgb("#FEF3C7");
                CloudConnectionStatusFrame.Stroke = Color.FromArgb("#F59E0B");
            }
        }

        private async void OnCloudSaveSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Cloud] Saving settings...");
                var config = BuildCloudConfigFromUi();

                if (string.IsNullOrWhiteSpace(config.TenantSlug) || string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please enter Tenant Slug and API Key.");
                    return;
                }

                if (_databaseService == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Database is not available.");
                    return;
                }

                var saved = await _databaseService.SaveCloudConfigurationAsync(config);
                if (!saved)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Could not save OrderWeb settings to the database.");
                    return;
                }

                _currentCloudConfig = config;
                _userHasPendingCloudEdits = false;
                EnableCloudButtonsIfReady();
                UpdateCloudConnectionStatus(
                    "Settings Saved",
                    "#0F766E",
                    "Saved and enabled. Click Connect to test the API and start live order sync.");

                await ToastNotification.ShowAsync(
                    "Saved",
                    "OrderWeb settings saved successfully.",
                    NotificationType.Success,
                    2500);

                System.Diagnostics.Debug.WriteLine("[Cloud] Settings saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Save failed: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save settings: {ex.Message}");
            }
        }

        private async Task ConnectCloudServicesFromUiAsync()
        {
            if (!await SaveCloudConfigFromUiAsync())
            {
                throw new InvalidOperationException("Could not save cloud settings before connecting.");
            }

            _connectionKeeper ??= ServiceHelper.GetService<OrderWebConnectionKeeperService>();
            if (_connectionKeeper == null)
            {
                throw new InvalidOperationException("OrderWeb connection keeper is not available.");
            }

            await _connectionKeeper.ApplyConfigurationAsync(_currentCloudConfig, forceBackfill: true);
            ApplyCloudStatusFromKeeper(_connectionKeeper.Status);

            if (!_connectionKeeper.Status.IsApiHealthy)
            {
                throw new InvalidOperationException(_connectionKeeper.Status.LastError ?? "OrderWeb API connection failed.");
            }
        }

        private async void OnCloudConnectClicked(object sender, EventArgs e)
        {
            if (_isCloudConnecting) return;

            try
            {
                _isCloudConnecting = true;
                CloudConnectButton.Text = "Connecting...";
                CloudConnectButton.IsEnabled = false;
                UpdateCloudConnectionStatus("Connecting...", "#007BFF", "Saving settings and testing OrderWeb connection");

                if (!await SaveCloudConfigFromUiAsync())
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                        "Validation Error",
                        "Enter Tenant Slug and API Key, then try Connect again.");
                    UpdateCloudConnectionStatus("Not Connected", "#6C757D", "Missing tenant slug or API key.");
                    return;
                }

                _userHasPendingCloudEdits = false;

                var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
                if (!onlineMasterCheck.Allowed)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Online Order Master", onlineMasterCheck.Reason);
                    UpdateCloudConnectionStatus("Local POS Only", "#6C757D", onlineMasterCheck.Reason);
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[Cloud] Connecting to OrderWeb...");
                await ConnectCloudServicesFromUiAsync();

                if (_connectionKeeper != null)
                {
                    ApplyCloudStatusFromKeeper(_connectionKeeper.Status);
                }

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Success",
                    "Connected to OrderWeb.net.\n\nREST API is active and order sync has started.");
                System.Diagnostics.Debug.WriteLine("[Cloud] Connection successful");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Connection failed: {ex.Message}");
                UpdateCloudConnectionStatus("Connection Failed", "#EF4444", ex.Message);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Connection Failed",
                    $"Could not connect to OrderWeb.net:\n\n{ex.Message}\n\nPlease verify Tenant Slug, API Key, and URLs.");
            }
            finally
            {
                _isCloudConnecting = false;
                CloudConnectButton.Text = "Connect";
                EnableCloudButtonsIfReady();
            }
        }

        private async void OnCloudSyncOrdersClicked(object sender, EventArgs e)
        {
            try
            {
                var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
                if (!onlineMasterCheck.Allowed)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Online Order Master", onlineMasterCheck.Reason);
                    return;
                }

                CloudSyncOrdersButton.Text = "Syncing...";
                CloudSyncOrdersButton.IsEnabled = false;
                
                System.Diagnostics.Debug.WriteLine("[Cloud] Manual sync initiated...");

                await SaveCloudConfigFromUiAsync();
                
                var cloudService = ServiceHelper.GetService<CloudOrderService>() ?? _cloudOrderService;
                if (cloudService != null)
                {
                    var result = await cloudService.SyncOrdersByDateAsync(DateTime.Today.AddDays(-1));
                    
                    if (result.Success)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Complete", $"Successfully synced {result.OrdersFound} orders from OrderWeb.net!");
                        System.Diagnostics.Debug.WriteLine($"[Cloud]  Sync completed: {result.OrdersFound} orders");
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", $"Sync failed: {result.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Cloud]  Sync failed: {result.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud]  Sync error: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Sync failed: {ex.Message}");
            }
            finally
            {
                CloudSyncOrdersButton.Text = "Sync Orders Now";
                CloudSyncOrdersButton.IsEnabled = true;
            }
        }

        private async void OnCloudSyncHistoricalClicked(object sender, EventArgs e)
        {
            try
            {
                var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_databaseService);
                if (!onlineMasterCheck.Allowed)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Online Order Master", onlineMasterCheck.Reason);
                    return;
                }

                CloudSyncHistoricalButton.Text = "Syncing Historical...";
                CloudSyncHistoricalButton.IsEnabled = false;
                
                System.Diagnostics.Debug.WriteLine("[Cloud] Historical sync initiated...");

                await SaveCloudConfigFromUiAsync();
                
                var cloudService = ServiceHelper.GetService<CloudOrderService>() ?? _cloudOrderService;
                if (cloudService != null)
                {
                    var result = await cloudService.SyncOrdersByDateAsync(DateTime.Today.AddDays(-60));
                    
                    if (result.Success)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Historical Sync Complete", $"Successfully synced {result.OrdersFound} historical orders from the last 2 months!");
                        System.Diagnostics.Debug.WriteLine($"[Cloud]  Historical sync completed: {result.OrdersFound} orders");
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Historical Sync Failed", $"Historical sync failed: {result.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Cloud]  Historical sync failed: {result.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud]  Historical sync error: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Historical sync failed: {ex.Message}");
            }
            finally
            {
                CloudSyncHistoricalButton.Text = "Sync Last 2 Months (Historical Orders)";
                CloudSyncHistoricalButton.IsEnabled = true;
            }
        }

        private void StartCloudStatusTimer()
        {
            StopCloudStatusTimer();
            
            _cloudStatusUpdateTimer = new System.Threading.Timer(
                _ => MainThread.BeginInvokeOnMainThread(() => UpdateCloudSystemStatus()),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3)
            );
        }

        private void StopCloudStatusTimer()
        {
            _cloudStatusUpdateTimer?.Dispose();
            _cloudStatusUpdateTimer = null;
        }

        private void UpdateCloudSystemStatus()
        {
            try
            {
                if (_isCloudConnecting)
                {
                    return;
                }

                if (_connectionKeeper != null)
                {
                    ApplyCloudStatusFromKeeper(_connectionKeeper.Status);
                }
                else if (_webSocketService != null)
                {
                    var isConnected = _webSocketService.IsConnected;
                    CloudStatusConnectionIcon.Text = isConnected ? "" : "";
                    CloudStatusConnectionText.Text = isConnected ? "Live" : "Offline";
                    CloudStatusConnectionText.TextColor = Color.FromArgb(isConnected ? "#10B981" : "#EF4444");
                }

                CloudDeviceInfoText.Text = $"{DeviceInfo.Model} - {DeviceInfo.Platform}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Status update error: {ex.Message}");
            }
        }

        #endregion

    }
}
