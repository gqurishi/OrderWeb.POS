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

namespace POS_in_NET.Pages
{
    public partial class UnifiedSettingsPage : ContentPage
    {
        // Tab state
        private Border currentActiveTab;
        private StackLayout currentActiveContent;
        
        // Services
        private readonly BusinessSettingsService _businessService;
        private readonly DatabaseService _databaseService;
        private readonly CloudSyncService _cloudService;
        private readonly OnlineOrderApiService _orderWebService;
        private readonly AuthenticationService _authService;
        private readonly PostcodeLookupService _postcodeLookupService;
        private readonly OrderNumberService _orderNumberService;
        
        // Cloud Connect Services
        private OrderWebWebSocketService? _webSocketService;
        private OrderWebRestApiService? _restApiService;
        private CloudOrderService? _cloudOrderService;
        private CloudConfiguration? _currentCloudConfig;
        private bool _isCloudApiKeyVisible = false;
        private bool _isCloudConnecting = false;
        private System.Threading.Timer? _cloudStatusUpdateTimer;
        
        // Data models
        private BusinessInfo? _currentBusinessInfo;
        private ObservableCollection<User> _users;
        private UserRole? _selectedRole;
        private PostcodeLookupSettings _postcodeSettings;

        public UnifiedSettingsPage()
        {
            InitializeComponent();
            TopBar.SetPageTitle("Settings");
            
            // Initialize services
            _businessService = new BusinessSettingsService();
            _databaseService = new DatabaseService();
            _cloudService = new CloudSyncService(_databaseService);
            _orderWebService = new OnlineOrderApiService();
            _authService = AuthenticationService.Instance;
            _postcodeLookupService = new PostcodeLookupService(_databaseService);
            _orderNumberService = new OrderNumberService(_databaseService);
            
            // Initialize users collection
            _users = new ObservableCollection<User>();
            UsersCollectionView.ItemsSource = _users;
            
            // Initialize postcode settings
            _postcodeSettings = new PostcodeLookupSettings();
            
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
            
            // Load initial data
            _ = LoadInitialDataAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            
            // Clean up cloud status timer
            StopCloudStatusTimer();
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Loading initial data for Settings page");
                
                // Show Business Info by default
                ShowContent("BusinessInfo");
                
                // Load business info data
                await LoadBusinessInfoAsync();
                await LoadOrderNumberSettingsAsync();
                
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

        // Accordion toggle handlers for OrderWeb expandable sections
        private void OnPostcodeSectionToggled(object sender, TappedEventArgs e)
        {
            try
            {
                bool isVisible = PostcodeContent.IsVisible;
                PostcodeContent.IsVisible = !isVisible;
                PostcodeToggleIcon.Text = isVisible ? "▶" : "▼";
                System.Diagnostics.Debug.WriteLine($"Postcode section toggled: {!isVisible}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling postcode section: {ex.Message}");
            }
        }

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
                
                // Update button text colors for inactive state
                UpdateTabButtonColor(BusinessTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(UserTabBorder, Color.FromArgb("#475569"));
                UpdateTabButtonColor(OrderWebTabBorder, Color.FromArgb("#475569"));
                
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

                        _ = LoadUsersAsync();
                        
                        // Debug: Check if Name entry is properly loaded
                        System.Diagnostics.Debug.WriteLine($"NameEntry visibility: {NameEntry?.IsVisible}");
                        
                        System.Diagnostics.Debug.WriteLine("User Management content set to visible");
                        break;
                    case "OrderWeb":
                        OrderWebContent.IsVisible = true;
                        currentActiveContent = OrderWebContent;
                        
                        // Load postcode settings for OrderWeb
                        _ = LoadOrderWebPostcodeSettingsAsync();
                        
                        System.Diagnostics.Debug.WriteLine("OrderWeb content set to visible");
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
                    // Create default business info
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

                // Populate form fields
                RestaurantNameEntry.Text = _currentBusinessInfo.RestaurantName;
                PhoneEntry.Text = _currentBusinessInfo.PhoneNumber;
                EmailEntry.Text = _currentBusinessInfo.Email;
                AddressEntry.Text = _currentBusinessInfo.Address;
                CityEntry.Text = _currentBusinessInfo.City;
                CountyEntry.Text = _currentBusinessInfo.County;
                CountryEntry.Text = _currentBusinessInfo.Country;
                PostcodeEntry.Text = _currentBusinessInfo.Postcode;
                WebsiteEntry.Text = _currentBusinessInfo.Website;
                VATNumberEntry.Text = _currentBusinessInfo.VATNumber;

                // Load logo if available
                await LoadBusinessLogo();
                
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

        private async Task LoadOrderNumberSettingsAsync()
        {
            try
            {
                var settings = await _orderNumberService.GetCurrentSettingsAsync();
                OrderPrefixEntry.Text = settings.Prefix;

                var preview = await _orderNumberService.GetNextOrderNumbersPreviewAsync();
                OrderExampleLabel.Text = $"Example: {preview.Table}";
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
                    // For new business info, you might need a CreateBusinessInfoAsync method
                    success = await _businessService.UpdateBusinessInfoAsync(_currentBusinessInfo, "Current User");
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

                var logoDirectory = Path.Combine(FileSystem.AppDataDirectory, "business-logos");
                Directory.CreateDirectory(logoDirectory);

                var safeFileName = Path.GetFileNameWithoutExtension(file.FileName);
                var fileExtension = Path.GetExtension(file.FileName);
                var savedFileName = $"{safeFileName}_{DateTime.UtcNow:yyyyMMddHHmmss}{fileExtension}";
                var savedFilePath = Path.Combine(logoDirectory, savedFileName);

                await File.WriteAllBytesAsync(savedFilePath, imageBytes);

                // Update UI from the persisted local file path
                LogoImage.Source = ImageSource.FromFile(savedFilePath);
                LogoImage.IsVisible = true;
                LogoPlaceholder.IsVisible = false;
                RemoveLogoButton.IsVisible = true;

                // Persist the saved local file path; the DB save happens when the form is saved
                if (_currentBusinessInfo != null)
                {
                    _currentBusinessInfo.LogoPath = savedFilePath;
                }

                LogoStatusLabel.Text = "Logo uploaded - click Save";
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

        private void RemoveLogo()
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
                RemoveLogo();
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

                if (string.IsNullOrEmpty(pin) || pin.Length < 4)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a PIN with at least 4 digits");
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
                
                _users.Clear();
                foreach (var user in users)
                {
                    _users.Add(user);
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded {_users.Count} users from database");
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
                
                if (string.IsNullOrWhiteSpace(PINEntry.Text) || PINEntry.Text.Length < 3)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a PIN (at least 3 characters)");
                    return;
                }
                
                if (_selectedRole == null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please select a role");
                    return;
                }
                
                // Get form values
                var name = NameEntry.Text.Trim();
                var username = name.Replace(" ", "").ToLower(); // Create username from name
                var pin = PINEntry.Text.Trim();
                var role = _selectedRole.Value;
                
                // Create user using AuthenticationService
                var result = await _authService.CreateUserAsync(name, username, pin, role);
                
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
                System.Diagnostics.Debug.WriteLine("Delete User clicked");
                
                if (sender is Button button && button.CommandParameter != null)
                {
                    // The CommandParameter is bound to the entire User object
                    if (button.CommandParameter is User userToDelete)
                    {
                        bool confirm = await DisplayAlert("Confirm Delete", 
                            $"Are you sure you want to delete '{userToDelete.Name}'?\n\nThis action cannot be undone.", 
                            "Delete", "Cancel");
                        
                        if (confirm)
                        {
                            // Delete user using AuthenticationService
                            var result = await _authService.DeleteUserAsync(userToDelete.Id);
                            
                            if (result.Success)
                            {
                                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", $"User '{userToDelete.Name}' has been deleted successfully.");
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
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Unable to determine which user to delete");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting user: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete user: {ex.Message}");
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
                
                System.Diagnostics.Debug.WriteLine("✅ Cloud services initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to initialize cloud services: {ex.Message}");
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
                
                if (_currentCloudConfig != null)
                {
                    TenantSlugEntry.Text = _currentCloudConfig.TenantSlug ?? "";
                    CloudApiKeyEntry.Text = _currentCloudConfig.ApiKey ?? "";
                    
                    // Clean up REST API URL - remove tenant suffix if present
                    var restUrl = _currentCloudConfig.RestApiBaseUrl ?? "https://orderweb.net/api";
                    if (restUrl.EndsWith($"/{_currentCloudConfig.TenantSlug}"))
                    {
                        restUrl = restUrl.Substring(0, restUrl.Length - _currentCloudConfig.TenantSlug.Length - 1);
                    }
                    CloudRestApiUrlEntry.Text = restUrl;
                    
                    CloudWebSocketUrlEntry.Text = _currentCloudConfig.WebSocketUrl ?? "wss://orderweb.net:9011";
                    
                    // Enable buttons if configuration is valid
                    EnableCloudButtonsIfReady();
                    
                    System.Diagnostics.Debug.WriteLine($"[Cloud] Settings loaded for tenant: {_currentCloudConfig.TenantSlug}");
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
            EnableCloudButtonsIfReady();
        }

        private void EnableCloudButtonsIfReady()
        {
            bool hasRequiredFields = !string.IsNullOrWhiteSpace(TenantSlugEntry.Text) &&
                                   !string.IsNullOrWhiteSpace(CloudApiKeyEntry.Text) &&
                                   !string.IsNullOrWhiteSpace(CloudRestApiUrlEntry.Text);
            
            CloudConnectButton.IsEnabled = hasRequiredFields && !_isCloudConnecting;
            CloudSyncOrdersButton.IsEnabled = hasRequiredFields;
            CloudSyncHistoricalButton.IsEnabled = hasRequiredFields;
        }

        private void OnToggleCloudApiKeyClicked(object sender, EventArgs e)
        {
            _isCloudApiKeyVisible = !_isCloudApiKeyVisible;
            CloudApiKeyEntry.IsPassword = !_isCloudApiKeyVisible;
            ToggleCloudApiKeyButton.Text = _isCloudApiKeyVisible ? "Hide" : "Show";
        }

        private async void OnCloudSaveSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Cloud] Saving settings...");
                
                var config = new CloudConfiguration
                {
                    TenantSlug = TenantSlugEntry.Text?.Trim() ?? "",
                    ApiKey = CloudApiKeyEntry.Text?.Trim() ?? "",
                    RestApiBaseUrl = CloudRestApiUrlEntry.Text?.Trim() ?? "https://orderweb.net/api",
                    WebSocketUrl = CloudWebSocketUrlEntry.Text?.Trim() ?? "wss://orderweb.net:9011"
                };

                if (_databaseService != null)
                {
                    await _databaseService.SaveCloudConfigurationAsync(config);
                    _currentCloudConfig = config;
                    
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Cloud settings saved successfully!");
                    EnableCloudButtonsIfReady();
                    
                    System.Diagnostics.Debug.WriteLine("[Cloud] Settings saved successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Save failed: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save settings: {ex.Message}");
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
                
                System.Diagnostics.Debug.WriteLine("[Cloud] Connecting to OrderWeb...");
                
                // Test connection with current settings
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService != null && _currentCloudConfig != null)
                {
                    bool connected = await cloudService.TestConnectionAsync();
                    
                    if (connected)
                    {
                        // Update status to connected
                        CloudConnectionStatusFrame.BackgroundColor = Color.FromArgb("#D1FAE5");
                        CloudConnectionStatusFrame.Stroke = Color.FromArgb("#10B981");
                        CloudConnectionStatusText.Text = "Connected";
                        CloudConnectionStatusText.TextColor = Color.FromArgb("#10B981");
                        CloudConnectionStatusDescription.Text = "Receiving orders in real-time";
                        
                        CloudSystemStatusSection.IsVisible = true;
                        
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Successfully connected to OrderWeb.net!\n\nYou are now ready to receive orders.");
                        
                        System.Diagnostics.Debug.WriteLine("[Cloud] ✅ Connection successful");
                    }
                    else
                    {
                        throw new Exception("Connection test failed");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] ❌ Connection failed: {ex.Message}");
                
                // Update status to error
                CloudConnectionStatusFrame.BackgroundColor = Color.FromArgb("#FEE2E2");
                CloudConnectionStatusFrame.Stroke = Color.FromArgb("#EF4444");
                CloudConnectionStatusText.Text = "Connection Failed";
                CloudConnectionStatusText.TextColor = Color.FromArgb("#EF4444");
                CloudConnectionStatusDescription.Text = $"Error: {ex.Message}";
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Connection Failed", $"Could not connect to OrderWeb.net:\n\n{ex.Message}\n\nPlease check your settings and try again.");
            }
            finally
            {
                _isCloudConnecting = false;
                CloudConnectButton.Text = "Connect to OrderWeb.net";
                EnableCloudButtonsIfReady();
            }
        }

        private async void OnCloudSyncOrdersClicked(object sender, EventArgs e)
        {
            try
            {
                CloudSyncOrdersButton.Text = "Syncing...";
                CloudSyncOrdersButton.IsEnabled = false;
                
                System.Diagnostics.Debug.WriteLine("[Cloud] Manual sync initiated...");
                
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService != null)
                {
                    var result = await cloudService.SyncOrdersByDateAsync(DateTime.Today.AddDays(-1));
                    
                    if (result.Success)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Complete", $"Successfully synced {result.OrdersFound} orders from OrderWeb.net!");
                        System.Diagnostics.Debug.WriteLine($"[Cloud] ✅ Sync completed: {result.OrdersFound} orders");
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Sync Failed", $"Sync failed: {result.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Cloud] ❌ Sync failed: {result.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] ❌ Sync error: {ex.Message}");
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
                CloudSyncHistoricalButton.Text = "Syncing Historical...";
                CloudSyncHistoricalButton.IsEnabled = false;
                
                System.Diagnostics.Debug.WriteLine("[Cloud] Historical sync initiated...");
                
                var cloudService = ServiceHelper.GetService<CloudOrderService>();
                if (cloudService != null)
                {
                    var result = await cloudService.SyncOrdersByDateAsync(DateTime.Today.AddDays(-60));
                    
                    if (result.Success)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Historical Sync Complete", $"Successfully synced {result.OrdersFound} historical orders from the last 2 months!");
                        System.Diagnostics.Debug.WriteLine($"[Cloud] ✅ Historical sync completed: {result.OrdersFound} orders");
                    }
                    else
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Historical Sync Failed", $"Historical sync failed: {result.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Cloud] ❌ Historical sync failed: {result.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] ❌ Historical sync error: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Historical sync failed: {ex.Message}");
            }
            finally
            {
                CloudSyncHistoricalButton.Text = "📅 Sync Last 2 Months (Historical Orders)";
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
                if (_webSocketService != null)
                {
                    var status = _webSocketService.GetConnectionStatus();
                    
                    // Update connection status
                    if (status == "Connected")
                    {
                        CloudStatusConnectionIcon.Text = "🟢";
                        CloudStatusConnectionText.Text = "Live";
                        CloudStatusConnectionText.TextColor = Color.FromArgb("#10B981");
                        
                        CloudStatusBackupIcon.Text = "✅";
                        CloudStatusBackupText.Text = "Active";
                        CloudStatusBackupText.TextColor = Color.FromArgb("#10B981");
                    }
                    else
                    {
                        CloudStatusConnectionIcon.Text = "🔴";
                        CloudStatusConnectionText.Text = "Offline";
                        CloudStatusConnectionText.TextColor = Color.FromArgb("#EF4444");
                        
                        CloudStatusBackupIcon.Text = "❌";
                        CloudStatusBackupText.Text = "Inactive";
                        CloudStatusBackupText.TextColor = Color.FromArgb("#EF4444");
                    }
                    
                    // Update last check time
                    CloudStatusLastCheckText.Text = DateTime.Now.ToString("HH:mm:ss");
                }
                
                // Update device info
                CloudDeviceInfoText.Text = $"{DeviceInfo.Model} - {DeviceInfo.Platform}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Cloud] Status update error: {ex.Message}");
            }
        }

        #endregion

        #region OrderWeb Postcode Lookup Handlers

        private async Task LoadOrderWebPostcodeSettingsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[OrderWeb] Loading postcode settings...");
                
                var settings = await _postcodeLookupService.GetSettingsAsync();
                
                if (settings != null)
                {
                    OrderWebMapboxTokenEntry.Text = settings.MapboxApiToken ?? "";
                    
                    // Show usage stats if there are any
                    if (settings.TotalLookups > 0)
                    {
                        await LoadOrderWebUsageStatsAsync();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[OrderWeb] Postcode settings loaded. Total lookups: {settings.TotalLookups}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[OrderWeb] No postcode settings found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderWeb] Failed to load postcode settings: {ex.Message}");
            }
        }
        
        private void OnToggleOrderWebMapboxToken(object sender, EventArgs e)
        {
            OrderWebMapboxTokenEntry.IsPassword = !OrderWebMapboxTokenEntry.IsPassword;
            ToggleOrderWebMapboxTokenButton.Text = OrderWebMapboxTokenEntry.IsPassword ? "Show" : "Hide";
        }

        private async void OnTestOrderWebMapboxClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("===============================================");
            System.Diagnostics.Debug.WriteLine("[ORDERWEB POSTCODE] Test connection clicked!");
            System.Diagnostics.Debug.WriteLine("===============================================");
            
            if (string.IsNullOrWhiteSpace(OrderWebMapboxTokenEntry.Text))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your Mapbox API token");
                return;
            }

            try
            {
                TestOrderWebMapboxButton.IsEnabled = false;
                TestOrderWebMapboxButton.Text = "Testing...";

                var token = OrderWebMapboxTokenEntry.Text?.Trim();
                System.Diagnostics.Debug.WriteLine($"[OrderWeb PostcodeLookup] Token entered: {token?.Substring(0, Math.Min(30, token.Length))}...");

                // Create temporary settings for testing
                var testSettings = new PostcodeLookupSettings
                {
                    Provider = "Mapbox",
                    MapboxApiToken = token,
                    MapboxEnabled = true
                };

                System.Diagnostics.Debug.WriteLine("[OrderWeb PostcodeLookup] Saving test settings...");
                await _postcodeLookupService.SaveSettingsAsync(testSettings);
                
                System.Diagnostics.Debug.WriteLine("[OrderWeb PostcodeLookup] Calling TestConnectionAsync()...");
                await _postcodeLookupService.TestConnectionAsync();
                System.Diagnostics.Debug.WriteLine("[OrderWeb PostcodeLookup] TestConnectionAsync() SUCCESS");

                // Show success result
                OrderWebMapboxStatusFrame.IsVisible = true;
                OrderWebMapboxStatusFrame.BackgroundColor = Color.FromArgb("#D1FAE5");
                OrderWebMapboxStatusFrame.Stroke = Color.FromArgb("#10B981");
                OrderWebMapboxStatusText.Text = "Connected successfully!";
                OrderWebMapboxStatusText.TextColor = Color.FromArgb("#10B981");

                // Load and show usage stats
                await LoadOrderWebUsageStatsAsync();
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Mapbox connection successful!\n\nYou can now use postcode lookup in your OrderWeb delivery orders.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderWeb PostcodeLookup] ❌ Connection test FAILED: {ex.Message}");
                
                OrderWebMapboxStatusFrame.IsVisible = true;
                OrderWebMapboxStatusFrame.BackgroundColor = Color.FromArgb("#FEE2E2");
                OrderWebMapboxStatusFrame.Stroke = Color.FromArgb("#EF4444");
                OrderWebMapboxStatusText.Text = $"Error: {ex.Message}";
                OrderWebMapboxStatusText.TextColor = Color.FromArgb("#EF4444");
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Test failed: {ex.Message}");
            }
            finally
            {
                TestOrderWebMapboxButton.IsEnabled = true;
                TestOrderWebMapboxButton.Text = "Test Connection";
            }
        }

        private async void OnSaveOrderWebPostcodeClicked(object sender, EventArgs e)
        {
            // Validate Mapbox token
            if (string.IsNullOrWhiteSpace(OrderWebMapboxTokenEntry.Text))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your Mapbox API token");
                return;
            }

            try
            {
                SaveOrderWebPostcodeButton.IsEnabled = false;
                SaveOrderWebPostcodeButton.Text = "Saving...";

                var settings = new PostcodeLookupSettings
                {
                    Provider = "Mapbox",
                    MapboxApiToken = OrderWebMapboxTokenEntry.Text?.Trim() ?? "",
                    MapboxEnabled = true,
                    CustomEnabled = false
                };

                bool success = await _postcodeLookupService.SaveSettingsAsync(settings);

                if (success)
                {
                    _postcodeSettings = settings;
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "OrderWeb postcode lookup settings saved successfully!");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to save OrderWeb postcode settings");
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save OrderWeb settings: {ex.Message}");
            }
            finally
            {
                SaveOrderWebPostcodeButton.IsEnabled = true;
                SaveOrderWebPostcodeButton.Text = "Save Settings";
            }
        }

        private async Task LoadOrderWebUsageStatsAsync()
        {
            try
            {
                var settings = await _postcodeLookupService.GetSettingsAsync();
                
                if (settings != null)
                {
                    OrderWebMapboxStatsFrame.IsVisible = true;
                    OrderWebMapboxUsageLabel.Text = $"Total Lookups: {settings.TotalLookups}";
                    OrderWebMapboxLastUsedLabel.Text = settings.LastUsed != null 
                        ? $"Last Used: {settings.LastUsed:dd/MM/yyyy HH:mm}" 
                        : "Last Used: Never";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderWeb PostcodeLookup] Failed to load usage stats: {ex.Message}");
            }
        }

        private async void OnToggleOrderWebMapboxTokenClicked(object sender, EventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    if (button.Text == "Show")
                    {
                        OrderWebMapboxTokenEntry.IsPassword = false;
                        button.Text = "Hide";
                    }
                    else
                    {
                        OrderWebMapboxTokenEntry.IsPassword = true;
                        button.Text = "Show";
                    }
                }
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to toggle token visibility: {ex.Message}");
            }
        }

        #endregion
    }
}