using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddCategoryDialog : ContentView
    {
        private readonly MenuCategoryService _categoryService;
        private MenuCategory? _editingCategory;
        private TaskCompletionSource<bool>? _taskCompletionSource;

        public AddCategoryDialog()
        {
            InitializeComponent();
            _categoryService = new MenuCategoryService();
        }

        public Task<bool> ShowAsync(MenuCategory? category = null)
        {
            _editingCategory = category;
            _taskCompletionSource = new TaskCompletionSource<bool>();

            if (_editingCategory != null)
            {
                // Edit mode
                DialogTitle.Text = "Edit Category";
                SaveButton.Text = "Update";
                NameEntry.Text = _editingCategory.Name;
                ActiveSwitch.IsToggled = _editingCategory.Active;
            }
            else
            {
                // Add mode
                DialogTitle.Text = "Add New Category";
                SaveButton.Text = "Save";
                NameEntry.Text = string.Empty;
                ActiveSwitch.IsToggled = true;
            }

            this.IsVisible = true;
            return _taskCompletionSource.Task;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NameEntry.Text))
                {
                    NotificationService.Instance.ShowWarning("Category name is required", "Validation Error");
                    return;
                }

                if (_editingCategory != null)
                {
                    // Update existing category
                    _editingCategory.Name = NameEntry.Text.Trim();
                    _editingCategory.Active = ActiveSwitch.IsToggled;
                    _editingCategory.UpdatedAt = DateTime.Now;

                    await _categoryService.UpdateCategoryAsync(_editingCategory);
                    NotificationService.Instance.ShowSuccess($"Category '{_editingCategory.Name}' updated successfully");
                }
                else
                {
                    // Create new category
                    var newCategory = new MenuCategory
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = NameEntry.Text.Trim(),
                        DisplayOrder = 0,
                        Active = ActiveSwitch.IsToggled,
                        ParentId = null, // Top-level category
                        Color = "#6366F1",
                        Icon = "📁",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _categoryService.CreateCategoryAsync(newCategory);
                    NotificationService.Instance.ShowSuccess($"Category '{newCategory.Name}' added successfully");
                }

                this.IsVisible = false;
                _taskCompletionSource?.SetResult(true);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to save category: {ex.Message}");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(false);
        }
    }
}
