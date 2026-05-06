using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddSubCategoryDialog : ContentView
    {
        private readonly MenuCategoryService _categoryService;
        private MenuCategory? _editingSubCategory;
        private TaskCompletionSource<bool>? _taskCompletionSource;
        private List<MenuCategory> _parentCategories = new();
        private List<MenuCategory> _filteredCategories = new();
        private MenuCategory? _selectedParentCategory;

        public AddSubCategoryDialog()
        {
            InitializeComponent();
            _categoryService = new MenuCategoryService();
        }

        public Task<bool> ShowAsync(MenuCategory? subCategory = null)
        {
            _editingSubCategory = subCategory;
            _taskCompletionSource = new TaskCompletionSource<bool>();

            // Load parent categories
            _ = InitializeDialogAsync();

            return _taskCompletionSource.Task;
        }

        private async Task InitializeDialogAsync()
        {
            // Load parent categories
            await LoadParentCategoriesAsync();

            if (_editingSubCategory != null)
            {
                // Edit mode
                DialogTitle.Text = "Edit Sub-Category";
                SaveButton.Text = "Update";
                NameEntry.Text = _editingSubCategory.Name;
                ActiveSwitch.IsToggled = _editingSubCategory.Active;

                // Select parent category
                if (!string.IsNullOrEmpty(_editingSubCategory.ParentId))
                {
                    _selectedParentCategory = _parentCategories.FirstOrDefault(c => c.Id == _editingSubCategory.ParentId);
                    if (_selectedParentCategory != null)
                    {
                        _selectedParentCategory.IsSelected = true;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            ParentCategoryCollectionView.SelectedItem = _selectedParentCategory;
                            SelectedCategoryFrame.IsVisible = true;
                            SelectedCategoryLabel.Text = $"Selected: {_selectedParentCategory.Name}";
                        });
                    }
                }
            }
            else
            {
                // Add mode
                DialogTitle.Text = "Add New Sub-Category";
                SaveButton.Text = "Save";
                NameEntry.Text = string.Empty;
                ActiveSwitch.IsToggled = true;
                CategorySearchEntry.Text = string.Empty;
                SelectedCategoryFrame.IsVisible = false;
                _selectedParentCategory = null;
                
                // Clear all selections
                foreach (var category in _parentCategories)
                {
                    category.IsSelected = false;
                }
            }

            this.IsVisible = true;
        }

        private async Task LoadParentCategoriesAsync()
        {
            try
            {
                _parentCategories = await _categoryService.GetTopLevelCategoriesAsync();
                _filteredCategories = new List<MenuCategory>(_parentCategories);
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ParentCategoryCollectionView.ItemsSource = _filteredCategories;
                    System.Diagnostics.Debug.WriteLine($"✅ Loaded {_parentCategories.Count} parent categories");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading parent categories: {ex.Message}");
            }
        }

        private void OnCategorySearchChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = e.NewTextValue?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredCategories = new List<MenuCategory>(_parentCategories);
            }
            else
            {
                _filteredCategories = _parentCategories
                    .Where(c => c.Name.ToLower().Contains(searchText))
                    .ToList();
            }
            
            ParentCategoryCollectionView.ItemsSource = _filteredCategories;
        }

        private void OnParentCategorySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is MenuCategory selectedCategory)
            {
                // Clear previous selection
                foreach (var category in _filteredCategories)
                {
                    category.IsSelected = false;
                }
                
                // Set new selection
                selectedCategory.IsSelected = true;
                _selectedParentCategory = selectedCategory;
                SelectedCategoryFrame.IsVisible = true;
                SelectedCategoryLabel.Text = $"Selected: {selectedCategory.Name}";
                System.Diagnostics.Debug.WriteLine($"✓ Selected parent category: {selectedCategory.Name}");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NameEntry.Text))
                {
                    NotificationService.Instance.ShowWarning("Sub-category name is required", "Validation Error");
                    return;
                }

                if (_selectedParentCategory == null)
                {
                    NotificationService.Instance.ShowWarning("Please select a parent category", "Validation Error");
                    return;
                }

                if (_editingSubCategory != null)
                {
                    // Update existing sub-category
                    _editingSubCategory.Name = NameEntry.Text.Trim();
                    _editingSubCategory.ParentId = _selectedParentCategory.Id;
                    _editingSubCategory.Active = ActiveSwitch.IsToggled;
                    _editingSubCategory.UpdatedAt = DateTime.Now;

                    await _categoryService.UpdateCategoryAsync(_editingSubCategory);
                    NotificationService.Instance.ShowSuccess($"Sub-category '{_editingSubCategory.Name}' updated successfully");
                }
                else
                {
                    // Create new sub-category
                    var newSubCategory = new MenuCategory
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = NameEntry.Text.Trim(),
                        ParentId = _selectedParentCategory.Id, // Set parent category
                        DisplayOrder = 0,
                        Active = ActiveSwitch.IsToggled,
                        Color = "#8B5CF6",
                        Icon = "📂",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _categoryService.CreateCategoryAsync(newSubCategory);
                    NotificationService.Instance.ShowSuccess($"Sub-category '{newSubCategory.Name}' added successfully");
                }

                this.IsVisible = false;
                _taskCompletionSource?.SetResult(true);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to save sub-category: {ex.Message}");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(false);
        }
    }
}
