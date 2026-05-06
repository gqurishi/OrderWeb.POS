using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class CategorySelectorDialog : ContentView
    {
        private List<MenuCategory> _allCategories = new();
        private List<MenuCategory> _filteredCategories = new();
        private TaskCompletionSource<MenuCategory?>? _taskCompletionSource;
        private bool _isSubCategoryMode;

        public CategorySelectorDialog()
        {
            InitializeComponent();
        }

        public Task<MenuCategory?> ShowAsync(List<MenuCategory> categories, bool isSubCategoryMode = false, string? noneOptionText = null)
        {
            _allCategories = categories ?? new List<MenuCategory>();
            _filteredCategories = new List<MenuCategory>(_allCategories);
            _isSubCategoryMode = isSubCategoryMode;
            _taskCompletionSource = new TaskCompletionSource<MenuCategory?>();

            // Update title
            DialogTitle.Text = isSubCategoryMode ? "Select Sub-Category" : "Select Category";
            
            // Clear search
            SearchEntry.Text = string.Empty;
            
            // Build UI
            BuildCategoriesList(noneOptionText);
            
            this.IsVisible = true;
            return _taskCompletionSource.Task;
        }

        private void BuildCategoriesList(string? noneOptionText)
        {
            CategoriesContainer.Clear();

            // Add "None" option for sub-categories
            if (_isSubCategoryMode && !string.IsNullOrEmpty(noneOptionText))
            {
                var noneButton = CreateCategoryButton(null, noneOptionText, "#EF4444", true);
                CategoriesContainer.Add(noneButton);
            }

            // Add category buttons
            foreach (var category in _filteredCategories)
            {
                var button = CreateCategoryButton(category, category.Name, "#475569", false);
                CategoriesContainer.Add(button);
            }
        }

        private Button CreateCategoryButton(MenuCategory? category, string text, string textColor, bool isSpecial)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 18,
                FontAttributes = isSpecial ? FontAttributes.Bold : FontAttributes.None,
                TextColor = Color.FromArgb(textColor),
                BackgroundColor = Color.FromArgb("#F1F5F9"),
                CornerRadius = 12,
                HeightRequest = 54,
                Margin = new Thickness(0)
            };

            button.Clicked += (s, e) =>
            {
                this.IsVisible = false;
                _taskCompletionSource?.SetResult(category);
            };

            return button;
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            var searchText = e.NewTextValue?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredCategories = new List<MenuCategory>(_allCategories);
            }
            else
            {
                _filteredCategories = _allCategories
                    .Where(c => c.Name.ToLower().Contains(searchText))
                    .ToList();
            }
            
            BuildCategoriesList(_isSubCategoryMode ? "None - Use main category only" : null);
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(null);
        }
    }
}
