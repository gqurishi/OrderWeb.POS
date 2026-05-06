using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddMenuItemDialog : ContentView
    {
        private readonly MenuItemService _itemService;
        private readonly MenuCategoryService _categoryService;
        private FoodMenuItem? _editingItem;
        private TaskCompletionSource<bool>? _taskCompletionSource;
        
        private List<MenuCategory> _categories = new();
        private List<MenuCategory> _subCategories = new();
        private MenuCategory? _selectedCategory;
        private MenuCategory? _selectedSubCategory;
        
        public ObservableCollection<MenuItemAddon> Addons { get; set; } = new();

        public AddMenuItemDialog()
        {
            InitializeComponent();
            _itemService = new MenuItemService();
            _categoryService = new MenuCategoryService();
            
            AddonsCollectionView.BindingContext = this;
            AddonsCollectionView.ItemsSource = Addons;
        }

        public Task<bool> ShowAsync(FoodMenuItem? item = null)
        {
            _editingItem = item;
            _taskCompletionSource = new TaskCompletionSource<bool>();
            _ = InitializeDialogAsync();
            return _taskCompletionSource.Task;
        }

        private async Task InitializeDialogAsync()
        {
            // Load categories first
            await LoadCategoriesAsync();

            if (_editingItem != null)
            {
                // Edit mode
                DialogTitle.Text = "Edit Menu Item";
                SaveButton.Text = "Update Item";
                NameEntry.Text = _editingItem.Name;
                PriceEntry.Text = _editingItem.Price.ToString("F2");
                
                // Set VAT toggle - UK standard VAT is 20%, so if item has 20% and not exempt, it's active
                VATActiveSwitch.IsToggled = !_editingItem.IsVatExempt && _editingItem.VatRate == 20.00m;
                UpdateVATStatusLabel();
                
                // Set Print in RED toggle
                PrintInRedSwitch.IsToggled = _editingItem.PrintInRed;

                if (!string.IsNullOrEmpty(_editingItem.CategoryId))
                {
                    _selectedCategory = _categories.FirstOrDefault(c => c.Id == _editingItem.CategoryId);
                    if (_selectedCategory != null)
                    {
                        CategoryLabel.Text = _selectedCategory.Name;
                        CategoryLabel.TextColor = Color.FromArgb("#1E293B");
                        LoadSubCategoriesForSelected();
                    }
                }
                
                Addons.Clear();
            }
            else
            {
                // Add mode
                DialogTitle.Text = "Add New Menu Item";
                SaveButton.Text = "Save Menu Item";
                NameEntry.Text = string.Empty;
                PriceEntry.Text = string.Empty;
                VATActiveSwitch.IsToggled = false;
                UpdateVATStatusLabel();
                
                _selectedCategory = null;
                _selectedSubCategory = null;
                CategoryLabel.Text = "Tap to choose";
                CategoryLabel.TextColor = Color.FromArgb("#94A3B8");
                SubCategoryLabel.Text = "Select category first";
                SubCategoryLabel.TextColor = Color.FromArgb("#CBD5E1");
                SubCategoryBorder.IsEnabled = false;
                SubCategoryBorder.Opacity = 0.7;
                SubCategoryBorder.BackgroundColor = Color.FromArgb("#F8FAFC");
                SubCategoryBorder.Stroke = Color.FromArgb("#E2E8F0");
                
                Addons.Clear();
            }
            
            this.IsVisible = true;
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var allCategories = await _categoryService.GetAllCategoriesAsync();
                // Store ALL categories for later filtering of sub-categories
                _categories = allCategories.ToList();
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to load categories: {ex.Message}");
            }
        }

        private async void OnCategoryClicked(object sender, EventArgs e)
        {
            // Only show MAIN categories (those without a parent)
            var mainCategories = _categories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
            
            if (!mainCategories.Any())
            {
                NotificationService.Instance.ShowWarning("Please create categories first in the Categories tab", "No Categories");
                return;
            }

            var selected = await CategorySelectorDialogView.ShowAsync(mainCategories, false);

            if (selected != null)
            {
                _selectedCategory = selected;
                CategoryLabel.Text = _selectedCategory.Name;
                CategoryLabel.TextColor = Color.FromArgb("#0F172A");
                
                _selectedSubCategory = null;
                LoadSubCategoriesForSelected();
            }
        }

        private void LoadSubCategoriesForSelected()
        {
            if (_selectedCategory == null) return;

            // Use already loaded categories instead of fetching again
            _subCategories = _categories.Where(c => c.ParentId == _selectedCategory.Id).ToList();

            if (_subCategories.Any())
            {
                SubCategoryLabel.Text = "Tap to choose";
                SubCategoryLabel.TextColor = Color.FromArgb("#64748B");
                SubCategoryBorder.IsEnabled = true;
                SubCategoryBorder.Opacity = 1.0;
                SubCategoryBorder.BackgroundColor = Colors.White;
                SubCategoryBorder.Stroke = Color.FromArgb("#CBD5E1");
            }
            else
            {
                SubCategoryLabel.Text = "None available";
                SubCategoryLabel.TextColor = Color.FromArgb("#CBD5E1");
                SubCategoryBorder.IsEnabled = false;
                SubCategoryBorder.Opacity = 0.7;
                SubCategoryBorder.BackgroundColor = Color.FromArgb("#F8FAFC");
                SubCategoryBorder.Stroke = Color.FromArgb("#E2E8F0");
            }
        }

        private async void OnSubCategoryClicked(object sender, EventArgs e)
        {
            if (!_subCategories.Any()) return;

            var selected = await CategorySelectorDialogView.ShowAsync(_subCategories, true, "None - Use main category only");

            if (selected == null)
            {
                // User clicked "None" - clear sub-category
                _selectedSubCategory = null;
                SubCategoryLabel.Text = "None selected";
                SubCategoryLabel.TextColor = Color.FromArgb("#64748B");
            }
            else
            {
                _selectedSubCategory = selected;
                SubCategoryLabel.Text = _selectedSubCategory.Name;
                SubCategoryLabel.TextColor = Color.FromArgb("#0F172A");
            }
        }

        private async void OnAddAddonClicked(object sender, EventArgs e)
        {
            var result = await AddAddonDialogView.ShowAsync();
            
            if (result != null)
            {
                Addons.Add(result);
                NotificationService.Instance.ShowSuccess($"Add-on '{result.Name}' added");
            }
        }

        private async void OnEditAddonClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            var addon = button?.CommandParameter as MenuItemAddon;
            if (addon == null) return;

            var result = await AddAddonDialogView.ShowAsync(addon);
            
            if (result != null)
            {
                // Refresh the collection view
                var index = Addons.IndexOf(addon);
                if (index >= 0)
                {
                    Addons.RemoveAt(index);
                    Addons.Insert(index, result);
                    NotificationService.Instance.ShowSuccess($"Add-on '{result.Name}' updated");
                }
            }
        }

        private async void OnDeleteAddonClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            var addon = button?.CommandParameter as MenuItemAddon;
            if (addon == null) return;

            var confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Confirm Delete",
                $"Are you sure you want to delete '{addon.Name}'?",
                "Delete",
                "Cancel");

            if (confirm)
            {
                Addons.Remove(addon);
                NotificationService.Instance.ShowSuccess($"Add-on '{addon.Name}' removed");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NameEntry.Text))
                {
                    NotificationService.Instance.ShowWarning("Item name is required", "Validation Error");
                    return;
                }

                if (_selectedCategory == null)
                {
                    NotificationService.Instance.ShowWarning("Please select a category", "Validation Error");
                    return;
                }

                if (!decimal.TryParse(PriceEntry.Text, out decimal price) || price < 0)
                {
                    NotificationService.Instance.ShowWarning("Please enter a valid price", "Validation Error");
                    return;
                }

                // UK standard VAT is 20% if active, 0% if not
                decimal vatRate = VATActiveSwitch.IsToggled ? 20.00m : 0.00m;
                bool isVatExempt = !VATActiveSwitch.IsToggled;

                // Determine final category ID: use sub-category if selected, otherwise use main category
                string finalCategoryId = _selectedSubCategory?.Id ?? _selectedCategory.Id;

                if (_editingItem != null)
                {
                    // Update existing item
                    _editingItem.Name = NameEntry.Text.Trim();
                    _editingItem.CategoryId = finalCategoryId;
                    _editingItem.Price = price;
                    _editingItem.VatRate = vatRate;
                    _editingItem.IsVatExempt = isVatExempt;
                    _editingItem.PrintInRed = PrintInRedSwitch.IsToggled;
                    _editingItem.UpdatedAt = DateTime.Now;

                    await _itemService.UpdateItemAsync(_editingItem);
                    NotificationService.Instance.ShowSuccess($"Menu item '{_editingItem.Name}' updated successfully");
                }
                else
                {
                    // Create new item
                    var newItem = new FoodMenuItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = NameEntry.Text.Trim(),
                        CategoryId = finalCategoryId,
                        Price = price,
                        VatRate = vatRate,
                        DisplayOrder = 0,
                        IsVatExempt = isVatExempt,
                        PrintInRed = PrintInRedSwitch.IsToggled,
                        Color = "#10B981",
                        VatType = "simple",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _itemService.CreateItemAsync(newItem);
                    NotificationService.Instance.ShowSuccess($"Menu item '{newItem.Name}' added successfully");
                }

                // Note: Add-ons are stored in memory for now
                // You'll need to implement addon storage (database or JSON) separately
                
                // Close dialog immediately for faster perceived performance
                this.IsVisible = false;
                _taskCompletionSource?.SetResult(true);
                
                // Clear form in background
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    NameEntry.Text = string.Empty;
                    PriceEntry.Text = string.Empty;
                    CategoryLabel.Text = "Tap to choose";
                    SubCategoryLabel.Text = "Select category first";
                    _selectedCategory = null;
                    _selectedSubCategory = null;
                    Addons.Clear();
                });
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to save menu item: {ex.Message}");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(false);
        }

        private void OnVATActiveToggled(object? sender, ToggledEventArgs e)
        {
            UpdateVATStatusLabel();
        }

        private void UpdateVATStatusLabel()
        {
            if (VATActiveSwitch.IsToggled)
            {
                VATStatusLabel.Text = "Active";
                VATStatusLabel.TextColor = Color.FromArgb("#10B981");
            }
            else
            {
                VATStatusLabel.Text = "Exempt";
                VATStatusLabel.TextColor = Color.FromArgb("#64748B");
            }
        }
    }
}
