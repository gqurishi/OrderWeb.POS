using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace POS_in_NET.Pages
{
    public partial class AddEditCategoryPage : ContentPage
    {
        private string _selectedColor = "#3B82F6";
        private MenuCategory? _categoryToEdit;
        private ObservableCollection<MenuCategory> _allCategories = new();
        private ObservableCollection<SelectableCategory> _selectableCategories;
        private bool _isSubCategory;
        private SelectableCategory? _selectedParentCategory;
        private Border? _selectedColorFrame;

        // Smart preset colors for restaurants (8 colors)
        private readonly List<string> _presetColors = new()
        {
            "#EF4444", // Red - Meat, Spicy
            "#F59E0B", // Amber - Fried, Appetizers
            "#10B981", // Green - Vegetarian, Healthy
            "#3B82F6", // Blue - Seafood, Cold Drinks
            "#8B5CF6", // Purple - Desserts, Specials
            "#EC4899", // Pink - Smoothies, Sweet
            "#06B6D4", // Cyan - Beverages, Ice Cream
            "#F97316", // Orange - Hot, Main Course
        };

        public MenuCategory? ResultCategory { get; private set; }

        public AddEditCategoryPage(ObservableCollection<MenuCategory> allCategories, MenuCategory? categoryToEdit = null, bool isSubCategory = false)
        {
            System.Diagnostics.Debug.WriteLine($"=== CONSTRUCTOR CALLED ===");
            System.Diagnostics.Debug.WriteLine($"Constructor received allCategories count: {allCategories?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"isSubCategory: {isSubCategory}");
            
            InitializeComponent();
            _allCategories = allCategories;
            _categoryToEdit = categoryToEdit;
            _isSubCategory = isSubCategory;
            _selectableCategories = new ObservableCollection<SelectableCategory>();

            InitializeDialog();
        }

        private void InitializeDialog()
        {
            // Setup preset colors (8 smart colors)
            foreach (var color in _presetColors)
            {
                var outerFrame = new Border
                {
                    BackgroundColor = Colors.White,
                    Stroke = Color.FromArgb("#E2E8F0"),
                    StrokeThickness = 1,
                    Padding = 4,
                    WidthRequest = 60,
                    HeightRequest = 60
                };

                var innerFrame = new Border
                {
                    BackgroundColor = Color.FromArgb(color),
                    Padding = 0,
                    WidthRequest = 52,
                    HeightRequest = 52
                };

                outerFrame.Content = innerFrame;

                // Use faster tap gesture with immediate response
                var tapGesture = new TapGestureRecognizer
                {
                    NumberOfTapsRequired = 1
                };
                var currentColor = color; // Capture for closure
                var currentFrame = outerFrame; // Capture for closure
                tapGesture.Tapped += (s, e) => 
                {
                    // Immediate response
                    OnColorSelected(currentColor, currentFrame);
                };
                outerFrame.GestureRecognizers.Add(tapGesture);

                ColorContainer.Children.Add(outerFrame);
            }

            // Setup parent category selection - only show top-level categories
            var topLevelCategories = _allCategories
                .Where(c => string.IsNullOrEmpty(c.ParentId))
                .ToList();
            System.Diagnostics.Debug.WriteLine($"=== PARENT CATEGORY COLLECTION SETUP ===");
            System.Diagnostics.Debug.WriteLine($"Total categories: {_allCategories.Count}");
            System.Diagnostics.Debug.WriteLine($"Top-level categories: {topLevelCategories.Count}");
            System.Diagnostics.Debug.WriteLine($"Is sub-category mode: {_isSubCategory}");
            
            foreach (var cat in topLevelCategories)
            {
                System.Diagnostics.Debug.WriteLine($"  Adding: ID={cat.Id}, Name={cat.Name}, Color={cat.Color}");
                _selectableCategories.Add(new SelectableCategory
                {
                    Id = cat.Id,
                    Name = cat.Name,
                    Color = cat.Color,
                    IsSelected = false
                });
            }
            
            ParentCategoryCollectionView.ItemsSource = _selectableCategories;
            System.Diagnostics.Debug.WriteLine($"CollectionView ItemsSource set with {_selectableCategories.Count} items");
            System.Diagnostics.Debug.WriteLine($"=== END COLLECTION SETUP ===");

            // Update dialog based on mode
            if (_isSubCategory)
            {
                DialogTitle.Text = _categoryToEdit != null ? "Edit Sub-Category" : "Add New Sub-Category";
                SaveButton.Text = _categoryToEdit != null ? "Update Sub-Category" : "Create Sub-Category";
                ParentLabel.Text = "Select Parent Category *";
                ParentHintLabel.Text = "Choose which category this sub-category belongs to";
                ParentCategorySection.IsVisible = true;
            }
            else
            {
                DialogTitle.Text = _categoryToEdit != null ? "Edit Category" : "Add New Category";
                SaveButton.Text = _categoryToEdit != null ? "Update Category" : "Create Category";
                ParentCategorySection.IsVisible = false; // Hide parent for top-level categories
            }

            // If editing, populate fields
            if (_categoryToEdit != null)
            {
                CategoryNameEntry.Text = _categoryToEdit.Name;
                _selectedColor = _categoryToEdit.Color;

                // Select the color
                var colorFrame = ColorContainer.Children
                    .OfType<Border>()
                    .FirstOrDefault(f => ((Border)f.Content!).BackgroundColor == Color.FromArgb(_selectedColor));
                if (colorFrame != null)
                {
                    OnColorSelected(_selectedColor, colorFrame);
                }

                // Select parent if it's a sub-category
                if (!string.IsNullOrEmpty(_categoryToEdit.ParentId))
                {
                    var parentToSelect = _selectableCategories.FirstOrDefault(c => c.Id == _categoryToEdit.ParentId);
                    if (parentToSelect != null)
                    {
                        parentToSelect.IsSelected = true;
                        _selectedParentCategory = parentToSelect;
                    }
                }
            }
            else
            {
                // Select first color by default
                if (ColorContainer.Children.Count > 0)
                {
                    var firstColor = ColorContainer.Children[0] as Border;
                    if (firstColor != null)
                    {
                        OnColorSelected(_presetColors[0], firstColor);
                    }
                }
            }
        }

        private void OnColorSelected(string color, Border selectedFrame)
        {
            _selectedColor = color;
            _selectedColorFrame = selectedFrame;
            
            System.Diagnostics.Debug.WriteLine($" Color selected: {color}");

            // Use MainThread to ensure instant UI update
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Reset all color frames quickly
                foreach (var child in ColorContainer.Children.OfType<Border>())
                {
                    child.Stroke = Color.FromArgb("#E2E8F0");
                    child.StrokeThickness = 1;
                    child.Padding = 4;
                }

                // Highlight selected with thicker border immediately
                selectedFrame.Stroke = Color.FromArgb(color);
                selectedFrame.StrokeThickness = 3;
                selectedFrame.Padding = 0;
                
                // Clear custom hex input since preset is selected
                CustomColorEntry.Text = string.Empty;
                ColorPreviewFrame.IsVisible = false;
                ColorErrorLabel.IsVisible = false;
            });
        }

        private void OnCustomColorTextChanged(object? sender, TextChangedEventArgs e)
        {
            var input = e.NewTextValue?.Trim().ToUpper() ?? string.Empty;
            
            // Remove any # if user typed it
            input = input.Replace("#", "");
            
            // Remove any invalid characters
            input = new string(input.Where(c => "0123456789ABCDEF".Contains(c)).ToArray());
            
            // Limit to 6 characters
            if (input.Length > 6)
            {
                input = input.Substring(0, 6);
            }
            
            // Update entry without triggering event again
            if (CustomColorEntry.Text != input)
            {
                CustomColorEntry.Text = input;
                return;
            }
            
            // Validate and show preview
            if (string.IsNullOrEmpty(input))
            {
                ColorPreviewFrame.IsVisible = false;
                ColorErrorLabel.IsVisible = false;
                return;
            }
            
            try
            {
                string hexColor;
                
                // Expand 3-digit to 6-digit (e.g., F00 -> FF0000)
                if (input.Length == 3)
                {
                    hexColor = $"#{input[0]}{input[0]}{input[1]}{input[1]}{input[2]}{input[2]}";
                }
                else if (input.Length == 6)
                {
                    hexColor = $"#{input}";
                }
                else
                {
                    // Incomplete, show preview but indicate it's partial
                    ColorPreviewFrame.IsVisible = false;
                    ColorErrorLabel.IsVisible = true;
                    ColorErrorLabel.Text = $"Enter 3 or 6 characters (currently {input.Length})";
                    return;
                }
                
                // Try to create the color
                var color = Color.FromArgb(hexColor);
                
                // Show preview
                ColorPreviewFrame.IsVisible = true;
                ColorPreviewCircle.BackgroundColor = color;
                ColorPreviewLabel.Text = $" {hexColor}";
                ColorPreviewLabel.TextColor = Color.FromArgb("#10B981");
                ColorErrorLabel.IsVisible = false;
                
                // Auto-select this color
                _selectedColor = hexColor;
                
                // Deselect all preset colors
                foreach (var child in ColorContainer.Children.OfType<Border>())
                {
                    child.Stroke = Color.FromArgb("#E2E8F0");
                    child.StrokeThickness = 1;
                    child.Padding = 4;
                }
            }
            catch
            {
                // Invalid color
                ColorPreviewFrame.IsVisible = false;
                ColorErrorLabel.IsVisible = true;
                ColorErrorLabel.Text = "Invalid hex color code";
            }
        }

        private void OnParentCategorySelected(object? sender, SelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"=== PARENT CATEGORY SELECTION CHANGED ===");
            
            if (e.CurrentSelection.Count > 0)
            {
                var selected = e.CurrentSelection[0] as SelectableCategory;
                System.Diagnostics.Debug.WriteLine($"Selected: {selected?.Name}");
                
                // Deselect all others
                foreach (var cat in _selectableCategories)
                {
                    cat.IsSelected = (cat.Id == selected?.Id);
                }
                
                _selectedParentCategory = selected;
                System.Diagnostics.Debug.WriteLine($"_selectedParentCategory set to: {_selectedParentCategory?.Name}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"No selection (cleared)");
                _selectedParentCategory = null;
            }
        }

        private async void OnSaveClicked(object? sender, EventArgs e)
        {
            var categoryName = CategoryNameEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please enter a category name.");
                return;
            }

            // Get parent category ID if selected
            string? parentCategoryId = _selectedParentCategory?.Id;

            // Validate: If this is a sub-category, parent MUST be selected
            if (_isSubCategory && string.IsNullOrEmpty(parentCategoryId))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please select a parent category for this sub-category.");
                return;
            }

            // Validate: If this is NOT a sub-category, parent must NOT be selected
            if (!_isSubCategory && !string.IsNullOrEmpty(parentCategoryId))
            {
                parentCategoryId = null; // Force it to be top-level
            }

            System.Diagnostics.Debug.WriteLine($"=== SAVING CATEGORY ===");
            System.Diagnostics.Debug.WriteLine($"Name: {categoryName}");
            System.Diagnostics.Debug.WriteLine($"Color: {_selectedColor}");
            System.Diagnostics.Debug.WriteLine($"ParentId: {parentCategoryId ?? "NULL"}");
            System.Diagnostics.Debug.WriteLine($"IsSubCategory: {_isSubCategory}");

            if (_categoryToEdit != null)
            {
                // Update existing category immediately
                _categoryToEdit.Name = categoryName;
                _categoryToEdit.Icon = ""; // Default icon
                _categoryToEdit.Color = _selectedColor;
                _categoryToEdit.ParentId = parentCategoryId;
                _categoryToEdit.UpdatedAt = DateTime.Now;
                ResultCategory = _categoryToEdit;
                
                System.Diagnostics.Debug.WriteLine($" Category updated with color: {_selectedColor}");
            }
            else
            {
                // Create new category
                ResultCategory = new MenuCategory
                {
                    Id = $"cat-{Guid.NewGuid()}",
                    Name = categoryName,
                    Description = "",
                    Icon = "", // Default icon
                    Color = _selectedColor,
                    ParentId = parentCategoryId,
                    Active = true,
                    DisplayOrder = _allCategories.Count,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                
                System.Diagnostics.Debug.WriteLine($" New category created with color: {_selectedColor}");
            }

            // Close dialog immediately - no delay
            await Navigation.PopModalAsync(true); // animated = true for smooth close
        }

        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            ResultCategory = null;
            await Navigation.PopModalAsync(true); // animated = true
        }
    }
}

