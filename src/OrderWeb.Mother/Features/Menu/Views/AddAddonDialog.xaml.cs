using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddAddonDialog : ContentView
    {
        private MenuItemAddon? _editingAddon;
        private TaskCompletionSource<MenuItemAddon?>? _taskCompletionSource;

        public MenuItemAddon? ResultAddon { get; private set; }

        public AddAddonDialog()
        {
            InitializeComponent();
        }

        public Task<MenuItemAddon?> ShowAsync(MenuItemAddon? addon = null)
        {
            _editingAddon = addon;
            _taskCompletionSource = new TaskCompletionSource<MenuItemAddon?>();
            ResultAddon = null;

            if (_editingAddon != null)
            {
                // Edit mode
                DialogTitle.Text = "Edit Add-on";
                SaveButton.Text = "Update";
                NameEntry.Text = _editingAddon.Name;
                PriceEntry.Text = _editingAddon.Price.ToString("F2");
            }
            else
            {
                // Add mode
                DialogTitle.Text = "Add New Add-on";
                SaveButton.Text = "Add";
                NameEntry.Text = string.Empty;
                PriceEntry.Text = string.Empty;
            }

            this.IsVisible = true;
            NameEntry.Focus();
            return _taskCompletionSource.Task;
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NameEntry.Text))
                {
                    NotificationService.Instance.ShowWarning("Add-on name is required", "Validation Error");
                    return;
                }

                if (!decimal.TryParse(PriceEntry.Text, out decimal price) || price < 0)
                {
                    NotificationService.Instance.ShowWarning("Please enter a valid price", "Validation Error");
                    return;
                }

                if (_editingAddon != null)
                {
                    // Update existing
                    _editingAddon.Name = NameEntry.Text.Trim();
                    _editingAddon.Price = price;
                    ResultAddon = _editingAddon;
                }
                else
                {
                    // Create new
                    ResultAddon = new MenuItemAddon
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = NameEntry.Text.Trim(),
                        Price = price
                    };
                }

                this.IsVisible = false;
                _taskCompletionSource?.SetResult(ResultAddon);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to save add-on: {ex.Message}");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(null);
        }
    }
}
