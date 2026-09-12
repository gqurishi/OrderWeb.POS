using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using System;
using System.Linq;

namespace POS_in_NET.Views
{
    public class UserUpdateEventArgs : EventArgs
    {
        public User User { get; set; }
        public string? NewPin { get; set; }
    }
    
    public partial class EditUserOverlay : ContentView
    {
        public event EventHandler<UserUpdateEventArgs> UserUpdated;
        public event EventHandler<UserRole> EditRoleSelected;
        public event EventHandler OverlayClosed;
        
        private User _currentUser;
        private UserRole? _selectedRole;
        
        public EditUserOverlay()
        {
            InitializeComponent();
        }
        
        public void ShowOverlay(User user)
        {
            _currentUser = user;
            _selectedRole = user.Role;
            
            // Populate form fields
            EditNameEntry.Text = user.Name;
            EditUsernameEntry.Text = user.Username;
            EditPINEntry.Text = ""; // Always start empty for security
            UpdatePinDisplay();
            EditSelectedRoleLabel.Text = user.Role.ToString();
            
            IsVisible = true;
            System.Diagnostics.Debug.WriteLine($"Edit user overlay shown for: {user.Name}");
        }
        
        public void HideOverlay()
        {
            IsVisible = false;
            ClearForm();
            System.Diagnostics.Debug.WriteLine("Edit user overlay hidden");
        }
        
        private void ClearForm()
        {
            EditNameEntry.Text = "";
            EditUsernameEntry.Text = "";
            EditPINEntry.Text = "";
            UpdatePinDisplay();
            EditSelectedRoleLabel.Text = "Select Role";
            _currentUser = null;
            _selectedRole = null;
        }
        
        private bool _pinKeyboardOpen;

        private async void OnEditPinClicked(object sender, EventArgs e)
        {
            if (_pinKeyboardOpen)
            {
                return;
            }

            _pinKeyboardOpen = true;
            try
            {
                var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
                var pin = await keyboard.ShowDigitsAsync(
                    initialValue: EditPINEntry.Text,
                    title: "New PIN",
                    maxDigits: 4,
                    hostPage: FindHostPage());
                if (string.IsNullOrWhiteSpace(pin))
                {
                    return;
                }

                var digits = new string(pin.Where(char.IsDigit).ToArray());
                if (digits.Length != 4)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("New PIN", "Enter a 4-digit PIN.");
                    return;
                }

                EditPINEntry.Text = digits;
                UpdatePinDisplay();
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("New PIN", ex.Message);
            }
            finally
            {
                _pinKeyboardOpen = false;
            }
        }

        private void UpdatePinDisplay()
        {
            if (EditPinDisplayLabel == null)
            {
                return;
            }

            var pin = EditPINEntry.Text ?? string.Empty;
            if (pin.Length == 0)
            {
                EditPinDisplayLabel.Text = "Enter new PIN";
                EditPinDisplayLabel.TextColor = Color.FromArgb("#94A3B8");
                return;
            }

            EditPinDisplayLabel.Text = new string('•', pin.Length);
            EditPinDisplayLabel.TextColor = Color.FromArgb("#1F2937");
        }

        private ContentPage? FindHostPage()
        {
            Element? current = this;
            while (current is not null)
            {
                if (current is ContentPage page)
                {
                    return page;
                }

                current = current.Parent;
            }

            return Shell.Current?.CurrentPage as ContentPage;
        }

        private void OnEditRolePickerTapped(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Edit role picker tapped");
            EditRoleSelected?.Invoke(this, _selectedRole ?? UserRole.User);
        }
        
        public void UpdateSelectedRole(UserRole role)
        {
            _selectedRole = role;
            EditSelectedRoleLabel.Text = role.ToString();
            System.Diagnostics.Debug.WriteLine($"Edit role updated to: {role}");
        }
        
        private void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (_currentUser == null)
                {
                    System.Diagnostics.Debug.WriteLine("Error: No user to update");
                    return;
                }
                
                // Validate required fields
                if (string.IsNullOrWhiteSpace(EditNameEntry.Text))
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a name");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(EditUsernameEntry.Text))
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a username");
                    return;
                }
                
                if (_selectedRole == null)
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please select a role");
                    return;
                }
                
                // Create updated user
                var updatedUser = new User
                {
                    Id = _currentUser.Id,
                    Name = EditNameEntry.Text.Trim(),
                    Username = EditUsernameEntry.Text.Trim(),
                    Role = _selectedRole.Value,
                    CreatedAt = _currentUser.CreatedAt,
                    UpdatedAt = DateTime.Now
                };
                
                // Only update password if new PIN is provided
                var newPin = EditPINEntry.Text?.Trim();
                if (!string.IsNullOrEmpty(newPin) && (newPin.Length != 4 || !newPin.All(char.IsDigit)))
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "New PIN must be exactly 4 digits");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Saving user updates: {updatedUser.Name}, Role: {updatedUser.Role}, New PIN: {!string.IsNullOrEmpty(newPin)}");
                
                // Create a custom event args to pass both user and PIN
                var eventArgs = new UserUpdateEventArgs { User = updatedUser, NewPin = newPin };
                UserUpdated?.Invoke(this, eventArgs);
                HideOverlay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving user: {ex.Message}");
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save user: {ex.Message}");
            }
        }
        
        private void OnCancelClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Edit user cancelled");
            HideOverlay();
        }
        
        private void OnOverlayTapped(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Edit overlay background tapped - closing");
            HideOverlay();
        }
        
        private async void DisplayAlert(string title, string message, string cancel)
        {
            if (Parent is Page page)
            {
                await page.DisplayAlert(title, message, cancel);
            }
        }
    }
}
