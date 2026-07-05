using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class FoodMenuManagement : ContentPage
    {
        private readonly MenuItemService _menuItemService;
        private readonly MenuCategoryService _categoryService;
        private readonly MealDealService _mealDealService;
        private readonly TastingMenuService _tastingMenuService;
        
        private List<MenuCategory> _allCategories = new();
        private List<MenuCategory> _topCategories = new();
        private List<MenuCategory> _subCategories = new();
        private List<FoodMenuItem> _allItems = new();
        private List<MealDeal> _allMealDeals = new();
        private List<TastingMenu> _allTastingMenus = new();
        
        private MenuCategory? _editingCategory;
        private MenuCategory? _selectedParentCategory;
        private string _selectedColor = "#3B82F6";
        private string _currentTab = "Items";
        private string _searchText = "";
        private bool _hasInitialLoad;
        private bool _isSavingSubCategory;
        private TaskCompletionSource<bool>? _deleteConfirmTaskSource;
        
        // Drag and drop state
        private MenuCategory? _draggingCategory;
        private View? _draggingView;
        private int _dragStartIndex = -1;
        
        // Preset colors for categories
        private readonly string[] _presetColors = new[]
        {
            "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
            "#EC4899", "#06B6D4", "#84CC16", "#F97316", "#6366F1",
            "#14B8A6", "#A855F7", "#E11D48", "#0EA5E9", "#22C55E"
        };

        public FoodMenuManagement()
        {
            InitializeComponent();
            
            _menuItemService = new MenuItemService();
            _categoryService = new MenuCategoryService();
            _mealDealService = new MealDealService();
            _tastingMenuService = new TastingMenuService();
            
            if (TopBar != null)
            {
                TopBar.SetPageTitle("Food Menu");
            }
            
            BuildColorPicker();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!_hasInitialLoad)
            {
                await LoadAllDataAsync();
                SelectTab("Items");
                _hasInitialLoad = true;
                return;
            }

            await RefreshMenuDataAsync();
            SelectTab(_currentTab);
        }

        #region Data Loading

        private async Task RefreshMenuDataAsync()
        {
            try
            {
                await LoadCategoriesAsync();
                _allItems = await _menuItemService.GetAllItemsAsync();
                _allMealDeals = await _mealDealService.GetAllDealsAsync();
                _allTastingMenus = await _tastingMenuService.GetAllAsync();
                RefreshCurrentTab();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing menu data: {ex.Message}");
                await ToastNotification.ShowAsync("Error", $"Failed to refresh menu data: {ex.Message}", NotificationType.Error);
            }
        }

        private async Task LoadCategoriesAsync()
        {
            _allCategories = await _categoryService.GetAllCategoriesAsync();
            _topCategories = _allCategories
                .Where(c => string.IsNullOrEmpty(c.ParentId))
                .OrderBy(c => c.DisplayOrder)
                .ToList();
            _subCategories = _allCategories
                .Where(c => !string.IsNullOrEmpty(c.ParentId))
                .OrderBy(c => c.DisplayOrder)
                .ToList();
            RefreshCurrentTab();
        }

        private async Task LoadAllDataAsync()
        {
            await RefreshMenuDataAsync();
        }

        private void RefreshCurrentTab()
        {
            if (CategoriesContent.IsVisible)
                BuildCategoriesList();
            else if (SubCategoriesContent.IsVisible)
                BuildSubCategoriesList();
            else if (ItemsContent.IsVisible)
                BuildItemsList();
            else if (MealDealsContent.IsVisible)
                BuildMealDealsList();
            else if (TastingMenusContent.IsVisible)
                BuildTastingMenusList();
        }

        #endregion

        #region Tab Navigation

        private void SelectTab(string tabName)
        {
            _currentTab = tabName;
            
            // Reset all tabs
            ItemsTabBorder.BackgroundColor = Colors.Transparent;
            ItemsTabLabel.TextColor = Color.FromArgb("#64748B");
            ItemsTabLabel.FontAttributes = FontAttributes.None;
            
            SubCategoriesTabBorder.BackgroundColor = Colors.Transparent;
            SubCategoriesTabLabel.TextColor = Color.FromArgb("#64748B");
            SubCategoriesTabLabel.FontAttributes = FontAttributes.None;
            
            CategoriesTabBorder.BackgroundColor = Colors.Transparent;
            CategoriesTabLabel.TextColor = Color.FromArgb("#64748B");
            CategoriesTabLabel.FontAttributes = FontAttributes.None;
            
            MealDealsTabBorder.BackgroundColor = Colors.Transparent;
            MealDealsTabLabel.TextColor = Color.FromArgb("#64748B");
            MealDealsTabLabel.FontAttributes = FontAttributes.None;

            TastingMenusTabBorder.BackgroundColor = Colors.Transparent;
            TastingMenusTabLabel.TextColor = Color.FromArgb("#64748B");
            TastingMenusTabLabel.FontAttributes = FontAttributes.None;
            
            // Hide all content
            ItemsContent.IsVisible = false;
            SubCategoriesContent.IsVisible = false;
            CategoriesContent.IsVisible = false;
            MealDealsContent.IsVisible = false;
            TastingMenusContent.IsVisible = false;
            
            // Activate selected tab
            switch (tabName)
            {
                case "Items":
                    ItemsTabBorder.BackgroundColor = Color.FromArgb("#3B82F6");
                    ItemsTabLabel.TextColor = Colors.White;
                    ItemsTabLabel.FontAttributes = FontAttributes.Bold;
                    ItemsContent.IsVisible = true;
                    BuildItemsList();
                    break;
                    
                case "SubCategories":
                    SubCategoriesTabBorder.BackgroundColor = Color.FromArgb("#8B5CF6");
                    SubCategoriesTabLabel.TextColor = Colors.White;
                    SubCategoriesTabLabel.FontAttributes = FontAttributes.Bold;
                    SubCategoriesContent.IsVisible = true;
                    BuildSubCategoriesList();
                    break;
                    
                case "Categories":
                    CategoriesTabBorder.BackgroundColor = Color.FromArgb("#10B981");
                    CategoriesTabLabel.TextColor = Colors.White;
                    CategoriesTabLabel.FontAttributes = FontAttributes.Bold;
                    CategoriesContent.IsVisible = true;
                    BuildCategoriesList();
                    break;
                    
                case "MealDeals":
                    MealDealsTabBorder.BackgroundColor = Color.FromArgb("#F59E0B");
                    MealDealsTabLabel.TextColor = Colors.White;
                    MealDealsTabLabel.FontAttributes = FontAttributes.Bold;
                    MealDealsContent.IsVisible = true;
                    BuildMealDealsList();
                    break;

                case "TastingMenus":
                    TastingMenusTabBorder.BackgroundColor = Color.FromArgb("#0EA5E9");
                    TastingMenusTabLabel.TextColor = Colors.White;
                    TastingMenusTabLabel.FontAttributes = FontAttributes.Bold;
                    TastingMenusContent.IsVisible = true;
                    BuildTastingMenusList();
                    break;
            }
        }

        private void OnItemsTabClicked(object sender, EventArgs e) => SelectTab("Items");
        private void OnSubCategoriesTabClicked(object sender, EventArgs e) => SelectTab("SubCategories");
        private void OnCategoriesTabClicked(object sender, EventArgs e) => SelectTab("Categories");
        private void OnMealDealsTabClicked(object sender, EventArgs e) => SelectTab("MealDeals");
        private void OnTastingMenusTabClicked(object sender, EventArgs e) => SelectTab("TastingMenus");
        
        #endregion
        
        #region Search
        
        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = e.NewTextValue?.ToLower() ?? "";
            RefreshCurrentTab();
        }
        
        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            SearchEntry.Text = "";
            _searchText = "";
            RefreshCurrentTab();
        }

        #endregion

        #region Categories Tab

        private void BuildCategoriesList()
        {
            CategoriesListContainer.Children.Clear();
            
            var filteredCategories = _topCategories.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                filteredCategories = filteredCategories.Where(c => 
                    c.Name.ToLower().Contains(_searchText) ||
                    (c.Description?.ToLower().Contains(_searchText) ?? false));
            }
            
            foreach (var category in filteredCategories)
            {
                var itemCount = _allItems.Count(i => i.CategoryId == category.Id);
                var row = CreateCategoryRow(category, itemCount);
                CategoriesListContainer.Children.Add(row);
            }
            
            if (!filteredCategories.Any())
            {
                var message = string.IsNullOrEmpty(_searchText) 
                    ? "No categories yet" 
                    : $"No categories matching \"{_searchText}\"";
                CategoriesListContainer.Children.Add(CreateEmptyState(message, "Add your first category to organize menu items"));
            }
        }

        private View CreateCategoryRow(MenuCategory category, int itemCount)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = 50 },   // Drag handle
                    new ColumnDefinition { Width = GridLength.Star }, // Name with color
                    new ColumnDefinition { Width = 100 },  // Items count
                    new ColumnDefinition { Width = 100 },  // Status
                    new ColumnDefinition { Width = 100 },  // Actions
                },
                ColumnSpacing = 10,
                Padding = new Thickness(24, 16),
                BackgroundColor = Colors.White
            };
            
            // Store category reference in the grid
            grid.BindingContext = category;
            
            // Arrow buttons for reordering
            int currentIndex = _topCategories.IndexOf(category);
            bool isFirst = currentIndex == 0;
            bool isLast = currentIndex == _topCategories.Count - 1;
            
            var arrowStack = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            
            // Up arrow button
            var upBtn = new Border
            {
                BackgroundColor = isFirst ? Color.FromArgb("#F1F5F9") : Color.FromArgb("#E2E8F0"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                WidthRequest = 32,
                HeightRequest = 24,
                Opacity = isFirst ? 0.5 : 1,
                Content = new Label
                {
                    Text = "↑",
                    FontSize = 16,
                    TextColor = isFirst ? Color.FromArgb("#94A3B8") : Color.FromArgb("#475569"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            
            if (!isFirst)
            {
                var upTap = new TapGestureRecognizer();
                upTap.Tapped += async (s, e) => await MoveCategoryUp(category);
                upBtn.GestureRecognizers.Add(upTap);
            }
            
            // Down arrow button
            var downBtn = new Border
            {
                BackgroundColor = isLast ? Color.FromArgb("#F1F5F9") : Color.FromArgb("#E2E8F0"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                WidthRequest = 32,
                HeightRequest = 24,
                Opacity = isLast ? 0.5 : 1,
                Content = new Label
                {
                    Text = "↓",
                    FontSize = 16,
                    TextColor = isLast ? Color.FromArgb("#94A3B8") : Color.FromArgb("#475569"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            
            if (!isLast)
            {
                var downTap = new TapGestureRecognizer();
                downTap.Tapped += async (s, e) => await MoveCategoryDown(category);
                downBtn.GestureRecognizers.Add(downTap);
            }
            
            arrowStack.Add(upBtn);
            arrowStack.Add(downBtn);
            
            var dragHandleBorder = arrowStack;
            
            // Add drag gesture to the entire grid for advanced users
            var dragGesture = new DragGestureRecognizer();
            dragGesture.DragStarting += (s, e) => OnCategoryDragStarting(category, grid, e);
            dragGesture.DropCompleted += (s, e) => OnCategoryDragCompleted(grid);
            grid.GestureRecognizers.Add(dragGesture);
            
            // Make the whole row a drop target
            var dropGesture = new DropGestureRecognizer();
            dropGesture.DragOver += (s, e) => OnCategoryDragOver(category, grid, e);
            dropGesture.DragLeave += (s, e) => OnCategoryDragLeave(grid);
            dropGesture.Drop += (s, e) => OnCategoryDrop(category, grid, e);
            grid.GestureRecognizers.Add(dropGesture);
            
            var dragHandle = dragHandleBorder;
            
            grid.Add(dragHandle, 0);
            
            // Category name with color indicator
            var nameStack = new HorizontalStackLayout
            {
                Spacing = 12,
                VerticalOptions = LayoutOptions.Center
            };
            
            var colorBorder = new Border
            {
                BackgroundColor = Color.FromArgb(category.Color ?? "#3B82F6"),
                WidthRequest = 24,
                HeightRequest = 24,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                StrokeThickness = 0,
                VerticalOptions = LayoutOptions.Center
            };
            nameStack.Add(colorBorder);
            
            nameStack.Add(new Label
            {
                Text = category.Name,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                VerticalOptions = LayoutOptions.Center
            });
            grid.Add(nameStack, 1);
            
            // Items count badge
            var countBadge = new Border
            {
                BackgroundColor = Color.FromArgb("#EFF6FF"),
                Stroke = Color.FromArgb("#3B82F6"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 4),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = $"{itemCount}",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#3B82F6"),
                    FontAttributes = FontAttributes.Bold
                }
            };
            grid.Add(countBadge, 2);
            
            // Status badge
            var statusColor = category.Active ? "#10B981" : "#EF4444";
            var statusText = category.Active ? "Active" : "Inactive";
            var statusBadge = new Border
            {
                BackgroundColor = Color.FromArgb(category.Active ? "#D1FAE5" : "#FEE2E2"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 6),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = statusText,
                    FontSize = 12,
                    TextColor = Color.FromArgb(statusColor),
                    FontAttributes = FontAttributes.Bold
                }
            };
            
            // Make status badge tappable to toggle
            var statusTap = new TapGestureRecognizer();
            statusTap.Tapped += async (s, e) => await ToggleCategoryStatus(category);
            statusBadge.GestureRecognizers.Add(statusTap);
            grid.Add(statusBadge, 3);
            
            // Action buttons
            var actionsStack = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            
            var editBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#60A5FA"),
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label
                {
                    Text = "\u2710",
                    FontSize = 22,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (s, e) => OnEditCategoryClicked(category);
            editBtn.GestureRecognizers.Add(editTap);
            
            var deleteBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#F87171"),
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label
                {
                    Text = "\u2716",
                    FontSize = 20,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var deleteTap = new TapGestureRecognizer();
            deleteTap.Tapped += async (s, e) => await OnDeleteCategoryClicked(category);
            deleteBtn.GestureRecognizers.Add(deleteTap);
            
            actionsStack.Add(editBtn);
            actionsStack.Add(deleteBtn);
            grid.Add(actionsStack, 4);
            
            // Bottom border
            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            
            return container;
        }
        
        #region Category Drag and Drop
        
        private void OnCategoryDragStarting(MenuCategory category, View view, DragStartingEventArgs e)
        {
            _draggingCategory = category;
            _draggingView = view;
            _dragStartIndex = _topCategories.IndexOf(category);
            
            // Set the data being dragged
            e.Data.Text = category.Id.ToString();
            e.Data.Properties["CategoryId"] = category.Id;
            
            // Visual feedback - make row slightly transparent with smooth animation
            view.Opacity = 0.5;
            view.BackgroundColor = Color.FromArgb("#F8FAFC");
            view.Scale = 0.98;
        }
        
        private void OnCategoryDragOver(MenuCategory targetCategory, View targetView, DragEventArgs e)
        {
            if (_draggingCategory == null || _draggingCategory.Id == targetCategory.Id)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }
            
            // Allow drop and provide visual feedback
            e.AcceptedOperation = DataPackageOperation.Copy;
            
            // Highlight the drop target
            if (targetView != _draggingView)
            {
                targetView.BackgroundColor = Color.FromArgb("#EEF2FF");
            }
        }
        
        private void OnCategoryDragLeave(View view)
        {
            // Remove highlight when drag leaves
            if (view != _draggingView)
            {
                view.BackgroundColor = Colors.White;
            }
        }
        
        private void OnCategoryDragCompleted(View view)
        {
            // Reset visual state when drag ends
            view.Opacity = 1;
            view.BackgroundColor = Colors.White;
            view.Scale = 1;
        }
        
        private async void OnCategoryDrop(MenuCategory targetCategory, View targetView, DropEventArgs e)
        {
            // Reset target view background
            targetView.BackgroundColor = Colors.White;
            
            if (_draggingCategory == null || _draggingCategory.Id == targetCategory.Id)
            {
                OnCategoryDragCompleted(_draggingView);
                _draggingCategory = null;
                _draggingView = null;
                return;
            }
            
            try
            {
                // Get indices
                int fromIndex = _topCategories.IndexOf(_draggingCategory);
                int toIndex = _topCategories.IndexOf(targetCategory);
                
                if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
                {
                    OnCategoryDragCompleted(_draggingView);
                    return;
                }
                
                // Move the category in the list immediately
                var movingCategory = _topCategories[fromIndex];
                _topCategories.RemoveAt(fromIndex);
                _topCategories.Insert(toIndex, movingCategory);
                
                // Refresh the list immediately for instant visual feedback
                BuildCategoriesList();
                
                // Update display order in background without blocking UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < _topCategories.Count; i++)
                        {
                            _topCategories[i].DisplayOrder = i;
                            await _categoryService.UpdateCategoryAsync(_topCategories[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
            }
            finally
            {
                if (_draggingView != null)
                {
                    OnCategoryDragCompleted(_draggingView);
                }
                _draggingCategory = null;
                _draggingView = null;
            }
        }
        
        #endregion

        #region Category Move Up/Down Methods

        private async Task MoveCategoryUp(MenuCategory category)
        {
            try
            {
                int currentIndex = _topCategories.IndexOf(category);
                if (currentIndex <= 0) return;
                
                // Swap with previous item in data
                var temp = _topCategories[currentIndex - 1];
                _topCategories[currentIndex - 1] = category;
                _topCategories[currentIndex] = temp;
                
                // Update display orders
                _topCategories[currentIndex].DisplayOrder = currentIndex;
                _topCategories[currentIndex - 1].DisplayOrder = currentIndex - 1;
                
                // Instantly swap the visual rows in the UI container
                var children = CategoriesListContainer.Children.ToList();
                if (currentIndex < children.Count && currentIndex - 1 >= 0)
                {
                    var currentRow = children[currentIndex];
                    var previousRow = children[currentIndex - 1];
                    
                    CategoriesListContainer.Children.RemoveAt(currentIndex);
                    CategoriesListContainer.Children.RemoveAt(currentIndex - 1);
                    
                    CategoriesListContainer.Children.Insert(currentIndex - 1, currentRow);
                    CategoriesListContainer.Children.Insert(currentIndex, previousRow);
                }
                
                // Save to database in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _categoryService.UpdateCategoryAsync(_topCategories[currentIndex]);
                        await _categoryService.UpdateCategoryAsync(_topCategories[currentIndex - 1]);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
                
                // Rebuild to update arrow button states
                await Task.Delay(50);
                BuildCategoriesList();
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to move category", NotificationType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async Task MoveCategoryDown(MenuCategory category)
        {
            try
            {
                int currentIndex = _topCategories.IndexOf(category);
                if (currentIndex < 0 || currentIndex >= _topCategories.Count - 1) return;
                
                // Swap with next item in data
                var temp = _topCategories[currentIndex + 1];
                _topCategories[currentIndex + 1] = category;
                _topCategories[currentIndex] = temp;
                
                // Update display orders
                _topCategories[currentIndex].DisplayOrder = currentIndex;
                _topCategories[currentIndex + 1].DisplayOrder = currentIndex + 1;
                
                // Instantly swap the visual rows in the UI container
                var children = CategoriesListContainer.Children.ToList();
                if (currentIndex < children.Count - 1 && currentIndex >= 0)
                {
                    var currentRow = children[currentIndex];
                    var nextRow = children[currentIndex + 1];
                    
                    CategoriesListContainer.Children.RemoveAt(currentIndex + 1);
                    CategoriesListContainer.Children.RemoveAt(currentIndex);
                    
                    CategoriesListContainer.Children.Insert(currentIndex, nextRow);
                    CategoriesListContainer.Children.Insert(currentIndex + 1, currentRow);
                }
                
                // Save to database in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _categoryService.UpdateCategoryAsync(_topCategories[currentIndex]);
                        await _categoryService.UpdateCategoryAsync(_topCategories[currentIndex + 1]);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
                
                // Rebuild to update arrow button states
                await Task.Delay(50);
                BuildCategoriesList();
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to move category", NotificationType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        #endregion

        private async Task ToggleCategoryStatus(MenuCategory category)
        {
            try
            {
                category.Active = !category.Active;
                await _categoryService.UpdateCategoryAsync(category);
                BuildCategoriesList();
                await ToastNotification.ShowAsync("Success", $"Category {(category.Active ? "activated" : "deactivated")}", NotificationType.Success, 1500);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to update status", NotificationType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        #endregion

        #region Sub-Categories Tab

        private void BuildSubCategoriesList()
        {
            SubCategoriesListContainer.Children.Clear();
            
            var filteredSubCategories = _subCategories.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                filteredSubCategories = filteredSubCategories.Where(c => 
                    c.Name.ToLower().Contains(_searchText) ||
                    (c.Description?.ToLower().Contains(_searchText) ?? false) ||
                    (_topCategories.FirstOrDefault(p => p.Id == c.ParentId)?.Name.ToLower().Contains(_searchText) ?? false));
            }
            
            foreach (var subCat in filteredSubCategories)
            {
                var parentName = _topCategories.FirstOrDefault(c => c.Id == subCat.ParentId)?.Name ?? "Unknown";
                var itemCount = _allItems.Count(i => i.CategoryId == subCat.Id);
                var row = CreateSubCategoryRow(subCat, parentName, itemCount);
                SubCategoriesListContainer.Children.Add(row);
            }
            
            if (!filteredSubCategories.Any())
            {
                var message = string.IsNullOrEmpty(_searchText) 
                    ? "No sub-categories yet" 
                    : $"No sub-categories matching \"{_searchText}\"";
                SubCategoriesListContainer.Children.Add(CreateEmptyState(message, "Add sub-categories to organize items within categories"));
            }
        }

        private View CreateSubCategoryRow(MenuCategory subCategory, string parentName, int itemCount)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = 50 },   // Drag handle
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 150 },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 100 },
                },
                ColumnSpacing = 10,
                Padding = new Thickness(24, 16),
                BackgroundColor = Colors.White
            };
            
            // Store sub-category reference in the grid
            grid.BindingContext = subCategory;
            
            // Arrow buttons for reordering
            int currentIndex = _subCategories.IndexOf(subCategory);
            bool isFirst = currentIndex == 0;
            bool isLast = currentIndex == _subCategories.Count - 1;
            
            var arrowStack = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            
            // Up arrow button
            var upBtn = new Border
            {
                BackgroundColor = isFirst ? Color.FromArgb("#F1F5F9") : Color.FromArgb("#E2E8F0"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                WidthRequest = 32,
                HeightRequest = 24,
                Opacity = isFirst ? 0.5 : 1,
                Content = new Label
                {
                    Text = "↑",
                    FontSize = 16,
                    TextColor = isFirst ? Color.FromArgb("#94A3B8") : Color.FromArgb("#475569"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            
            if (!isFirst)
            {
                var upTap = new TapGestureRecognizer();
                upTap.Tapped += async (s, e) => await MoveSubCategoryUp(subCategory);
                upBtn.GestureRecognizers.Add(upTap);
            }
            
            // Down arrow button
            var downBtn = new Border
            {
                BackgroundColor = isLast ? Color.FromArgb("#F1F5F9") : Color.FromArgb("#E2E8F0"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                WidthRequest = 32,
                HeightRequest = 24,
                Opacity = isLast ? 0.5 : 1,
                Content = new Label
                {
                    Text = "↓",
                    FontSize = 16,
                    TextColor = isLast ? Color.FromArgb("#94A3B8") : Color.FromArgb("#475569"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            
            if (!isLast)
            {
                var downTap = new TapGestureRecognizer();
                downTap.Tapped += async (s, e) => await MoveSubCategoryDown(subCategory);
                downBtn.GestureRecognizers.Add(downTap);
            }
            
            arrowStack.Add(upBtn);
            arrowStack.Add(downBtn);
            
            var dragHandleBorder = arrowStack;
            
            // Add drag gesture to the entire grid for advanced users
            var dragGesture = new DragGestureRecognizer();
            dragGesture.DragStarting += (s, e) => OnSubCategoryDragStarting(subCategory, grid, e);
            dragGesture.DropCompleted += (s, e) => OnSubCategoryDragCompleted(grid);
            grid.GestureRecognizers.Add(dragGesture);
            
            // Make the whole row a drop target
            var dropGesture = new DropGestureRecognizer();
            dropGesture.DragOver += (s, e) => OnSubCategoryDragOver(subCategory, grid, e);
            dropGesture.DragLeave += (s, e) => OnSubCategoryDragLeave(grid);
            dropGesture.Drop += (s, e) => OnSubCategoryDrop(subCategory, grid, e);
            grid.GestureRecognizers.Add(dropGesture);
            
            grid.Add(dragHandleBorder, 0);
            
            // Sub-category name with color indicator
            var nameStack = new HorizontalStackLayout
            {
                Spacing = 12,
                VerticalOptions = LayoutOptions.Center
            };
            nameStack.Add(new Border
            {
                BackgroundColor = Color.FromArgb(subCategory.Color ?? "#8B5CF6"),
                WidthRequest = 24, HeightRequest = 24,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                StrokeThickness = 0,
                VerticalOptions = LayoutOptions.Center
            });
            nameStack.Add(new Label { Text = subCategory.Name, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B"), VerticalOptions = LayoutOptions.Center });
            grid.Add(nameStack, 1);
            
            // Parent category badge
            var parentBadge = new Border
            {
                BackgroundColor = Color.FromArgb("#F1F5F9"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 5),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = parentName, FontSize = 12, TextColor = Color.FromArgb("#475569"), FontAttributes = FontAttributes.Bold }
            };
            grid.Add(parentBadge, 2);
            
            // Items count
            grid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#F3E8FF"),
                Stroke = Color.FromArgb("#8B5CF6"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 4),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = $"{itemCount}", FontSize = 12, TextColor = Color.FromArgb("#8B5CF6"), FontAttributes = FontAttributes.Bold }
            }, 3);
            
            // Status
            var statusBadge = new Border
            {
                BackgroundColor = Color.FromArgb(subCategory.Active ? "#D1FAE5" : "#FEE2E2"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 6),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = subCategory.Active ? "Active" : "Inactive", FontSize = 12, TextColor = Color.FromArgb(subCategory.Active ? "#10B981" : "#EF4444"), FontAttributes = FontAttributes.Bold }
            };
            var statusTap = new TapGestureRecognizer();
            statusTap.Tapped += async (s, e) => await ToggleSubCategoryStatus(subCategory);
            statusBadge.GestureRecognizers.Add(statusTap);
            grid.Add(statusBadge, 4);
            
            // Actions
            var actionsStack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            var editBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#60A5FA"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2710", FontSize = 22, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (s, e) => OnEditSubCategoryClicked(subCategory);
            editBtn.GestureRecognizers.Add(editTap);
            
            var deleteBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#F87171"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2716", FontSize = 20, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var deleteTap = new TapGestureRecognizer();
            deleteTap.Tapped += async (s, e) => await OnDeleteSubCategoryClicked(subCategory);
            deleteBtn.GestureRecognizers.Add(deleteTap);
            
            actionsStack.Add(editBtn);
            actionsStack.Add(deleteBtn);
            grid.Add(actionsStack, 5);
            
            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            return container;
        }

        #region Sub-Category Drag and Drop
        
        private void OnSubCategoryDragStarting(MenuCategory subCategory, View view, DragStartingEventArgs e)
        {
            _draggingCategory = subCategory;
            _draggingView = view;
            _dragStartIndex = _subCategories.IndexOf(subCategory);
            
            // Set the data being dragged
            e.Data.Text = subCategory.Id.ToString();
            e.Data.Properties["SubCategoryId"] = subCategory.Id;
            
            // Visual feedback - make row slightly transparent with smooth animation
            view.Opacity = 0.5;
            view.BackgroundColor = Color.FromArgb("#F8FAFC");
            view.Scale = 0.98;
        }
        
        private void OnSubCategoryDragOver(MenuCategory targetSubCategory, View targetView, DragEventArgs e)
        {
            if (_draggingCategory == null || _draggingCategory.Id == targetSubCategory.Id)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }
            
            // Allow drop and provide visual feedback
            e.AcceptedOperation = DataPackageOperation.Copy;
            
            // Highlight the drop target
            if (targetView != _draggingView)
            {
                targetView.BackgroundColor = Color.FromArgb("#F3E8FF");
            }
        }
        
        private void OnSubCategoryDragLeave(View view)
        {
            // Remove highlight when drag leaves
            if (view != _draggingView)
            {
                view.BackgroundColor = Colors.White;
            }
        }
        
        private void OnSubCategoryDragCompleted(View view)
        {
            // Reset visual state when drag ends
            view.Opacity = 1;
            view.BackgroundColor = Colors.White;
            view.Scale = 1;
        }
        
        private async void OnSubCategoryDrop(MenuCategory targetSubCategory, View targetView, DropEventArgs e)
        {
            // Reset target view background
            targetView.BackgroundColor = Colors.White;
            
            if (_draggingCategory == null || _draggingCategory.Id == targetSubCategory.Id)
            {
                OnSubCategoryDragCompleted(_draggingView);
                _draggingCategory = null;
                _draggingView = null;
                return;
            }
            
            try
            {
                // Get indices
                int fromIndex = _subCategories.IndexOf(_draggingCategory);
                int toIndex = _subCategories.IndexOf(targetSubCategory);
                
                if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
                {
                    OnSubCategoryDragCompleted(_draggingView);
                    return;
                }
                
                // Move the sub-category in the list immediately
                var movingSubCategory = _subCategories[fromIndex];
                _subCategories.RemoveAt(fromIndex);
                _subCategories.Insert(toIndex, movingSubCategory);
                
                // Refresh the list immediately for instant visual feedback
                BuildSubCategoriesList();
                
                // Update display order in background without blocking UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < _subCategories.Count; i++)
                        {
                            _subCategories[i].DisplayOrder = i;
                            await _categoryService.UpdateCategoryAsync(_subCategories[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
            }
            finally
            {
                if (_draggingView != null)
                {
                    OnSubCategoryDragCompleted(_draggingView);
                }
                _draggingCategory = null;
                _draggingView = null;
            }
        }
        
        #endregion

        #region Sub-Category Move Up/Down Methods

        private async Task MoveSubCategoryUp(MenuCategory subCategory)
        {
            try
            {
                int currentIndex = _subCategories.IndexOf(subCategory);
                if (currentIndex <= 0) return;
                
                // Swap with previous item in data
                var temp = _subCategories[currentIndex - 1];
                _subCategories[currentIndex - 1] = subCategory;
                _subCategories[currentIndex] = temp;
                
                // Update display orders
                _subCategories[currentIndex].DisplayOrder = currentIndex;
                _subCategories[currentIndex - 1].DisplayOrder = currentIndex - 1;
                
                // Instantly swap the visual rows in the UI container
                var children = SubCategoriesListContainer.Children.ToList();
                if (currentIndex < children.Count && currentIndex - 1 >= 0)
                {
                    var currentRow = children[currentIndex];
                    var previousRow = children[currentIndex - 1];
                    
                    SubCategoriesListContainer.Children.RemoveAt(currentIndex);
                    SubCategoriesListContainer.Children.RemoveAt(currentIndex - 1);
                    
                    SubCategoriesListContainer.Children.Insert(currentIndex - 1, currentRow);
                    SubCategoriesListContainer.Children.Insert(currentIndex, previousRow);
                }
                
                // Save to database in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _categoryService.UpdateCategoryAsync(_subCategories[currentIndex]);
                        await _categoryService.UpdateCategoryAsync(_subCategories[currentIndex - 1]);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
                
                // Rebuild to update arrow button states
                await Task.Delay(50);
                BuildSubCategoriesList();
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to move sub-category", NotificationType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async Task MoveSubCategoryDown(MenuCategory subCategory)
        {
            try
            {
                int currentIndex = _subCategories.IndexOf(subCategory);
                if (currentIndex < 0 || currentIndex >= _subCategories.Count - 1) return;
                
                // Swap with next item in data
                var temp = _subCategories[currentIndex + 1];
                _subCategories[currentIndex + 1] = subCategory;
                _subCategories[currentIndex] = temp;
                
                // Update display orders
                _subCategories[currentIndex].DisplayOrder = currentIndex;
                _subCategories[currentIndex + 1].DisplayOrder = currentIndex + 1;
                
                // Instantly swap the visual rows in the UI container
                var children = SubCategoriesListContainer.Children.ToList();
                if (currentIndex < children.Count - 1 && currentIndex >= 0)
                {
                    var currentRow = children[currentIndex];
                    var nextRow = children[currentIndex + 1];
                    
                    SubCategoriesListContainer.Children.RemoveAt(currentIndex + 1);
                    SubCategoriesListContainer.Children.RemoveAt(currentIndex);
                    
                    SubCategoriesListContainer.Children.Insert(currentIndex, nextRow);
                    SubCategoriesListContainer.Children.Insert(currentIndex + 1, currentRow);
                }
                
                // Save to database in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _categoryService.UpdateCategoryAsync(_subCategories[currentIndex]);
                        await _categoryService.UpdateCategoryAsync(_subCategories[currentIndex + 1]);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background update error: {ex.Message}");
                    }
                });
                
                // Rebuild to update arrow button states
                await Task.Delay(50);
                BuildSubCategoriesList();
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to move sub-category", NotificationType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        #endregion

        private async Task ToggleSubCategoryStatus(MenuCategory subCategory)
        {
            try
            {
                subCategory.Active = !subCategory.Active;
                await _categoryService.UpdateCategoryAsync(subCategory);
                BuildSubCategoriesList();
                await ToastNotification.ShowAsync("Success", $"Sub-category {(subCategory.Active ? "activated" : "deactivated")}", NotificationType.Success, 1500);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", "Failed to update status", NotificationType.Error);
            }
        }

        #endregion

        #region Items Tab

        private void BuildItemsList()
        {
            ItemsListContainer.Children.Clear();
            
            var filteredItems = _allItems.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                filteredItems = filteredItems.Where(i => 
                    i.Name.ToLower().Contains(_searchText) ||
                    (i.Description?.ToLower().Contains(_searchText) ?? false) ||
                    (_allCategories.FirstOrDefault(c => c.Id == i.CategoryId)?.Name.ToLower().Contains(_searchText) ?? false));
            }
            
            foreach (var item in filteredItems)
            {
                var categoryName = _allCategories.FirstOrDefault(c => c.Id == item.CategoryId)?.Name ?? "Unknown";
                var row = CreateItemRow(item, categoryName);
                ItemsListContainer.Children.Add(row);
            }
            
            if (!filteredItems.Any())
            {
                var message = string.IsNullOrEmpty(_searchText) 
                    ? "No menu items yet" 
                    : $"No items matching \"{_searchText}\"";
                ItemsListContainer.Children.Add(CreateEmptyState(message, "Add your first menu item"));
            }
        }

        private View CreateItemRow(FoodMenuItem item, string categoryName)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = 60 },   // Row number
                    new ColumnDefinition { Width = GridLength.Star },  // Item name & description
                    new ColumnDefinition { Width = 120 },  // Price
                    new ColumnDefinition { Width = 150 },  // Category
                    new ColumnDefinition { Width = 100 },  // Status
                    new ColumnDefinition { Width = 120 },  // Actions
                },
                ColumnSpacing = 10,
                Padding = new Thickness(24, 16),
                BackgroundColor = Colors.White
            };
            
            // Store item reference in the grid
            grid.BindingContext = item;
            
            // Row number
            int rowNumber = _allItems.IndexOf(item) + 1;
            grid.Add(new Label 
            { 
                Text = rowNumber.ToString(), 
                FontSize = 14, 
                FontAttributes = FontAttributes.Bold, 
                TextColor = Color.FromArgb("#64748B"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            }, 0);
            
            // Item name with description
            var nameStack = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center
            };
            nameStack.Add(new Label
            {
                Text = item.Name,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B")
            });
            if (!string.IsNullOrEmpty(item.Description))
            {
                nameStack.Add(new Label
                {
                    Text = item.Description,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#64748B"),
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }
            grid.Add(nameStack, 1);
            
            // Price
            grid.Add(new Label { Text = $"£{item.Price:F2}", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#10B981"), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center }, 2);
            
            // Category badge
            grid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#EFF6FF"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 6),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = categoryName, FontSize = 12, TextColor = Color.FromArgb("#3B82F6"), FontAttributes = FontAttributes.Bold }
            }, 3);
            
            // Status
            grid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#D1FAE5"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(12, 6),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = "Active", FontSize = 12, TextColor = Color.FromArgb("#10B981"), FontAttributes = FontAttributes.Bold }
            }, 4);
            
            // Actions
            var actionsStack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            
            var editBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#60A5FA"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2710", FontSize = 22, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (s, e) => OnEditItemClicked(item);
            editBtn.GestureRecognizers.Add(editTap);
            
            var deleteBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#F87171"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2716", FontSize = 20, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var deleteTap = new TapGestureRecognizer();
            deleteTap.Tapped += async (s, e) => await OnDeleteItemClicked(item);
            deleteBtn.GestureRecognizers.Add(deleteTap);
            
            actionsStack.Add(editBtn);
            actionsStack.Add(deleteBtn);
            grid.Add(actionsStack, 5);
            
            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            return container;
        }



        #endregion

        #region Meal Deals Tab

        private void BuildMealDealsList()
        {
            MealDealsListContainer.Children.Clear();
            
            var filteredDeals = _allMealDeals.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                filteredDeals = filteredDeals.Where(d => 
                    d.Name.ToLower().Contains(_searchText) ||
                    (d.Description?.ToLower().Contains(_searchText) ?? false) ||
                    d.Choices.Any(c => c.Name.ToLower().Contains(_searchText)));
            }
            
            foreach (var deal in filteredDeals)
            {
                var row = CreateMealDealRow(deal);
                MealDealsListContainer.Children.Add(row);
            }
            
            if (!filteredDeals.Any())
            {
                var message = string.IsNullOrEmpty(_searchText) 
                    ? "No meal deals yet" 
                    : $"No meal deals matching \"{_searchText}\"";
                MealDealsListContainer.Children.Add(CreateEmptyState(message, "Create combo offers to boost sales"));
            }
        }

        private View CreateMealDealRow(MealDeal deal)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 120 },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 100 },
                },
                ColumnSpacing = 10,
                Padding = new Thickness(24, 16),
                BackgroundColor = Colors.White
            };
            
            // Name
            var nameStack = new VerticalStackLayout { Spacing = 2 };
            nameStack.Add(new Label { Text = deal.Name, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B") });
            if (!string.IsNullOrEmpty(deal.Description))
                nameStack.Add(new Label { Text = deal.Description, FontSize = 12, TextColor = Color.FromArgb("#64748B"), LineBreakMode = LineBreakMode.TailTruncation });
            grid.Add(nameStack, 0);
            
            // Price
            grid.Add(new Label { Text = $"£{deal.Price:F2}", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#F59E0B"), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center }, 1);
            
            // Pick rule badge
            grid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#FEF3C7"),
                Stroke = Color.FromArgb("#F59E0B"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 4),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = deal.PickRuleDisplay, FontSize = 12, TextColor = Color.FromArgb("#F59E0B"), FontAttributes = FontAttributes.Bold }
            }, 2);
            
            // Status (tap to toggle)
            var statusBorder = new Border
            {
                BackgroundColor = Color.FromArgb(deal.Active ? "#D1FAE5" : "#FEE2E2"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 4),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = deal.Active ? "Active" : "Inactive", FontSize = 11, TextColor = Color.FromArgb(deal.Active ? "#10B981" : "#EF4444"), FontAttributes = FontAttributes.Bold }
            };
            var statusTap = new TapGestureRecognizer();
            statusTap.Tapped += async (_, _) => await ToggleMealDealStatus(deal);
            statusBorder.GestureRecognizers.Add(statusTap);
            grid.Add(statusBorder, 3);
            
            // Actions
            var actionsStack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            var editBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#60A5FA"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2710", FontSize = 22, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (_, _) => OnEditMealDealClicked(deal);
            editBtn.GestureRecognizers.Add(editTap);
            
            var deleteBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#F87171"),
                WidthRequest = 40, HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2716", FontSize = 20, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var deleteTap = new TapGestureRecognizer();
            deleteTap.Tapped += async (_, _) => await OnDeleteMealDealClicked(deal);
            deleteBtn.GestureRecognizers.Add(deleteTap);
            
            actionsStack.Add(editBtn);
            actionsStack.Add(deleteBtn);
            grid.Add(actionsStack, 4);
            
            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            return container;
        }

        private async Task ToggleMealDealStatus(MealDeal deal)
        {
            try
            {
                await _mealDealService.ToggleActiveAsync(deal.Id);
                deal.Active = !deal.Active;
                OrderPlacementPageSimple.InvalidateMenuCache();
                BuildMealDealsList();
                await ToastNotification.ShowAsync("Success", $"Meal deal {(deal.Active ? "activated" : "deactivated")}", NotificationType.Success, 1500);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", ex.Message, NotificationType.Error);
            }
        }

        #endregion

        #region Tasting Menus Tab

        private void BuildTastingMenusList()
        {
            TastingMenusListContainer.Children.Clear();

            var filteredMenus = _allTastingMenus.AsEnumerable();
            if (!string.IsNullOrEmpty(_searchText))
            {
                filteredMenus = filteredMenus.Where(menu =>
                    menu.Name.ToLower().Contains(_searchText) ||
                    (menu.Description?.ToLower().Contains(_searchText) ?? false) ||
                    menu.Options.Any(option => option.DisplayName.ToLower().Contains(_searchText)) ||
                    menu.Courses.Any(course => course.Name.ToLower().Contains(_searchText)));
            }

            var materialized = filteredMenus.ToList();
            foreach (var menu in materialized)
            {
                TastingMenusListContainer.Children.Add(CreateTastingMenuRow(menu));
            }

            if (materialized.Count == 0)
            {
                var message = string.IsNullOrEmpty(_searchText)
                    ? "No tasting menus yet"
                    : $"No tasting menus matching \"{_searchText}\"";
                TastingMenusListContainer.Children.Add(CreateEmptyState(message, "Create multi-course packages with optional wine pairings"));
            }
        }

        private View CreateTastingMenuRow(TastingMenu menu)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 220 },
                    new ColumnDefinition { Width = 120 },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 100 }
                },
                ColumnSpacing = 10,
                Padding = new Thickness(24, 16),
                BackgroundColor = Colors.White
            };

            var nameStack = new VerticalStackLayout { Spacing = 2 };
            nameStack.Add(new Label { Text = menu.Name, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B") });
            if (!string.IsNullOrWhiteSpace(menu.Description))
            {
                nameStack.Add(new Label { Text = menu.Description, FontSize = 12, TextColor = Color.FromArgb("#64748B"), LineBreakMode = LineBreakMode.TailTruncation });
            }
            grid.Add(nameStack, 0);

            grid.Add(new Label
            {
                Text = menu.OptionsDisplay,
                FontSize = 12,
                TextColor = Color.FromArgb("#475569"),
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center
            }, 1);

            grid.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#E0F2FE"),
                Stroke = Color.FromArgb("#38BDF8"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 4),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = menu.CoursesDisplay, FontSize = 12, TextColor = Color.FromArgb("#0369A1"), FontAttributes = FontAttributes.Bold }
            }, 2);

            var statusBorder = new Border
            {
                BackgroundColor = Color.FromArgb(menu.Active ? "#D1FAE5" : "#FEE2E2"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 4),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = menu.Active ? "Active" : "Inactive", FontSize = 11, TextColor = Color.FromArgb(menu.Active ? "#10B981" : "#EF4444"), FontAttributes = FontAttributes.Bold }
            };
            var statusTap = new TapGestureRecognizer();
            statusTap.Tapped += async (_, _) => await ToggleTastingMenuStatus(menu);
            statusBorder.GestureRecognizers.Add(statusTap);
            grid.Add(statusBorder, 3);

            var actionsStack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            var editBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#60A5FA"),
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2710", FontSize = 22, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (_, _) => Navigation.PushAsync(new AddEditTastingMenuPage(menu));
            editBtn.GestureRecognizers.Add(editTap);

            var deleteBtn = new Border
            {
                BackgroundColor = Color.FromArgb("#F87171"),
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label { Text = "\u2716", FontSize = 20, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            var deleteTap = new TapGestureRecognizer();
            deleteTap.Tapped += async (_, _) => await OnDeleteTastingMenuClicked(menu);
            deleteBtn.GestureRecognizers.Add(deleteTap);

            actionsStack.Add(editBtn);
            actionsStack.Add(deleteBtn);
            grid.Add(actionsStack, 4);

            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            return container;
        }

        private void OnAddTastingMenuClicked(object sender, EventArgs e)
        {
            Navigation.PushAsync(new AddEditTastingMenuPage());
        }

        private async Task ToggleTastingMenuStatus(TastingMenu menu)
        {
            try
            {
                await _tastingMenuService.ToggleActiveAsync(menu.Id);
                menu.Active = !menu.Active;
                OrderPlacementPageSimple.InvalidateMenuCache();
                BuildTastingMenusList();
                await ToastNotification.ShowAsync("Success", $"Tasting menu {(menu.Active ? "activated" : "deactivated")}", NotificationType.Success, 1500);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", ex.Message, NotificationType.Error);
            }
        }

        private async Task OnDeleteTastingMenuClicked(TastingMenu menu)
        {
            var confirmed = await DisplayAlert("Delete Tasting Menu", $"Delete '{menu.Name}'?", "Delete", "Cancel");
            if (!confirmed)
            {
                return;
            }

            try
            {
                await _tastingMenuService.DeleteAsync(menu.Id);
                _allTastingMenus.Remove(menu);
                OrderPlacementPageSimple.InvalidateMenuCache();
                BuildTastingMenusList();
                await ToastNotification.ShowAsync("Deleted", "Tasting menu deleted", NotificationType.Success, 1500);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", ex.Message, NotificationType.Error);
            }
        }

        #endregion

        #region Category Dialog

        private void BuildColorPicker()
        {
            ColorPickerContainer.Children.Clear();
            
            foreach (var color in _presetColors)
            {
                var colorBorder = new Border
                {
                    BackgroundColor = Color.FromArgb(color),
                    WidthRequest = 36,
                    HeightRequest = 36,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    StrokeThickness = color == _selectedColor ? 3 : 0,
                    Stroke = Colors.White,
                    Margin = new Thickness(0, 0, 8, 8)
                };
                
                // Add shadow/border for selected
                if (color == _selectedColor)
                {
                    colorBorder.Shadow = new Shadow { Brush = Colors.Black, Offset = new Point(0, 2), Radius = 4, Opacity = 0.3f };
                }
                
                var tap = new TapGestureRecognizer();
                var capturedColor = color;
                tap.Tapped += (s, e) => SelectColor(capturedColor);
                colorBorder.GestureRecognizers.Add(tap);
                
                ColorPickerContainer.Children.Add(colorBorder);
            }
        }

        private void SelectColor(string color)
        {
            _selectedColor = color;
            CustomColorEntry.Text = ""; // Clear custom color entry
            CustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            CustomColorPreview.StrokeThickness = 2;
            BuildColorPicker();
        }

        private void OnAddCategoryClicked(object sender, EventArgs e)
        {
            _editingCategory = null;
            CategoryDialogTitle.Text = "Add New Category";
            CategoryNameEntry.Text = "";
            CategoryActiveSwitch.IsToggled = true;
            _selectedColor = "#3B82F6";
            CustomColorEntry.Text = "";
            CustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            CustomColorPreview.StrokeThickness = 2;
            BuildColorPicker();
            AddCategoryDialog.IsVisible = true;
        }

        private void OnEditCategoryClicked(MenuCategory category)
        {
            _editingCategory = category;
            CategoryDialogTitle.Text = "Edit Category";
            CategoryNameEntry.Text = category.Name;
            CategoryActiveSwitch.IsToggled = category.Active;
            _selectedColor = category.Color;
            
            // Check if the color is a preset or custom
            if (!_presetColors.Contains(category.Color))
            {
                CustomColorEntry.Text = category.Color;
                CustomColorPreview.BackgroundColor = Color.FromArgb(category.Color);
                CustomColorPreview.StrokeThickness = 3;
            }
            else
            {
                CustomColorEntry.Text = "";
                CustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
                CustomColorPreview.StrokeThickness = 2;
            }
            
            BuildColorPicker();
            AddCategoryDialog.IsVisible = true;
        }

        private void OnCategoryDialogClose(object sender, EventArgs e)
        {
            AddCategoryDialog.IsVisible = false;
        }

        private void OnCustomColorTextChanged(object sender, TextChangedEventArgs e)
        {
            var hexColor = e.NewTextValue?.Trim();
            if (string.IsNullOrEmpty(hexColor)) 
            {
                CustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
                return;
            }
            
            // Ensure it starts with #
            if (!hexColor.StartsWith("#"))
                hexColor = "#" + hexColor;
            
            try
            {
                // Validate hex format (3, 4, 6, or 8 characters after #)
                var hex = hexColor.TrimStart('#');
                if (hex.Length == 3 || hex.Length == 4 || hex.Length == 6 || hex.Length == 8)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]+$"))
                    {
                        var color = Color.FromArgb(hexColor);
                        CustomColorPreview.BackgroundColor = color;
                        _selectedColor = hexColor;
                        
                        // Deselect preset colors
                        DeselectAllPresetColors();
                        
                        CustomColorPreview.Stroke = new SolidColorBrush(Microsoft.Maui.Graphics.Colors.DarkGray);
                        CustomColorPreview.StrokeThickness = 3;
                    }
                }
            }
            catch
            {
                CustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            }
        }

        private void DeselectAllPresetColors()
        {
            // Reset all preset color boxes
            foreach (var child in ColorPickerContainer.Children)
            {
                if (child is Border colorBox)
                {
                    colorBox.StrokeThickness = 0;
                }
            }
        }

        private async void OnSaveCategoryClicked(object sender, EventArgs e)
        {
            var name = CategoryNameEntry.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                await ToastNotification.ShowAsync("Error", "Please enter a category name", NotificationType.Warning);
                return;
            }
            
            try
            {
                if (_editingCategory != null)
                {
                    // Update existing
                    _editingCategory.Name = name;
                    _editingCategory.Description = null;
                    _editingCategory.Color = _selectedColor;
                    _editingCategory.Active = CategoryActiveSwitch.IsToggled;
                    await _categoryService.UpdateCategoryAsync(_editingCategory);
                    await ToastNotification.ShowAsync("Success", "Category updated", NotificationType.Success, 1500);
                }
                else
                {
                    // Create new
                    var newCategory = new MenuCategory
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Description = null,
                        Color = _selectedColor,
                        Active = CategoryActiveSwitch.IsToggled,
                        ParentId = null,
                        DisplayOrder = _topCategories.Count
                    };
                    await _categoryService.CreateCategoryAsync(newCategory);
                    await ToastNotification.ShowAsync("Success", "Category created", NotificationType.Success, 1500);
                }
                
                AddCategoryDialog.IsVisible = false;
                await LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", $"Failed to save: {ex.Message}", NotificationType.Error);
            }
        }

        private async Task OnDeleteCategoryClicked(MenuCategory category)
        {
            var result = await ShowDeleteConfirmAsync(
                "Delete Category",
                $"Are you sure you want to delete '{category.Name}'?",
                "This action cannot be undone and will remove all items in this category."
            );
            
            if (!result) return;
            
            try
            {
                await _categoryService.DeleteCategoryAsync(category.Id);
                await ToastNotification.ShowAsync("Deleted", "Category removed successfully", NotificationType.Success, 1500);
                await LoadAllDataAsync();
            }
            catch (Exception)
            {
                await ToastNotification.ShowAsync("Error", "Failed to delete category", NotificationType.Error);
            }
        }

        #endregion

        #region Sub-Category Actions

        private async void OnAddSubCategoryClicked(object sender, EventArgs e)
        {
            // Ensure data is loaded
            if (_topCategories == null || !_topCategories.Any())
            {
                await LoadAllDataAsync();
            }
            
            if (!_topCategories.Any())
            {
                await ToastNotification.ShowAsync("Info", "Please create categories first", NotificationType.Info, 2000);
                return;
            }
            
            _editingCategory = null;
            _selectedParentCategory = null;
            SubCategoryDialogTitle.Text = "Add New Sub-Category";
            SubCategoryNameEntry.Text = "";
            SubCategoryActiveSwitch.IsToggled = true;
            _selectedColor = "#8B5CF6";
            SubCategoryCustomColorEntry.Text = "";
            SubCategoryCustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            SubCategoryCustomColorPreview.StrokeThickness = 2;
            ParentCategoryLabel.Text = "Select parent category";
            ParentCategoryLabel.TextColor = Color.FromArgb("#94A3B8");
            
            BuildSubCategoryColorPicker();
            AddSubCategoryDialog.IsVisible = true;
        }

        private void OnEditSubCategoryClicked(MenuCategory subCategory)
        {
            _editingCategory = subCategory;
            SubCategoryDialogTitle.Text = "Edit Sub-Category";
            SubCategoryNameEntry.Text = subCategory.Name;
            SubCategoryActiveSwitch.IsToggled = subCategory.Active;
            _selectedColor = subCategory.Color;
            
            // Set the selected parent category
            _selectedParentCategory = _topCategories.FirstOrDefault(c => c.Id == subCategory.ParentId);
            if (_selectedParentCategory != null)
            {
                ParentCategoryLabel.Text = _selectedParentCategory.Name;
                ParentCategoryLabel.TextColor = Color.FromArgb("#1E293B");
            }
            else
            {
                ParentCategoryLabel.Text = "Select parent category";
                ParentCategoryLabel.TextColor = Color.FromArgb("#94A3B8");
            }
            
            // Check if the color is a preset or custom
            if (!_presetColors.Contains(subCategory.Color))
            {
                SubCategoryCustomColorEntry.Text = subCategory.Color;
                SubCategoryCustomColorPreview.BackgroundColor = Color.FromArgb(subCategory.Color);
                SubCategoryCustomColorPreview.StrokeThickness = 3;
            }
            else
            {
                SubCategoryCustomColorEntry.Text = "";
                SubCategoryCustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
                SubCategoryCustomColorPreview.StrokeThickness = 2;
            }
            
            BuildSubCategoryColorPicker();
            AddSubCategoryDialog.IsVisible = true;
        }

        private void OnSubCategoryDialogClose(object sender, EventArgs e)
        {
            AddSubCategoryDialog.IsVisible = false;
        }

        private void OnParentCategoryFieldTapped(object sender, EventArgs e)
        {
            // Build the category selection list
            ParentCategoryListContainer.Children.Clear();
            
            var availableCategories = _topCategories?.OrderBy(c => c.Name).ToList() ?? new List<MenuCategory>();
            
            if (!availableCategories.Any())
            {
                var emptyLabel = new Label
                {
                    Text = "No categories available. Please create a main category first.",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#94A3B8"),
                    Padding = new Thickness(24, 32),
                    HorizontalTextAlignment = TextAlignment.Center
                };
                ParentCategoryListContainer.Children.Add(emptyLabel);
            }
            else
            {
                foreach (var category in availableCategories)
                {
                    var categoryRow = CreateParentCategorySelectionRow(category);
                    ParentCategoryListContainer.Children.Add(categoryRow);
                }
            }
            
            ParentCategorySelectionDialog.IsVisible = true;
        }

        private View CreateParentCategorySelectionRow(MenuCategory category)
        {
            var grid = new Grid
            {
                Padding = new Thickness(32, 20),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                BackgroundColor = Colors.White
            };
            
            // Color indicator
            var colorBox = new Border
            {
                BackgroundColor = Color.FromArgb(category.Color ?? "#3B82F6"),
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                StrokeThickness = 0,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            grid.Add(colorBox, 0);
            
            // Category name
            var nameStack = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center
            };
            
            var nameLabel = new Label
            {
                Text = category.Name,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B")
            };
            nameStack.Add(nameLabel);
            
            if (!category.Active)
            {
                var inactiveLabel = new Label
                {
                    Text = "Inactive",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#EF4444")
                };
                nameStack.Add(inactiveLabel);
            }
            
            grid.Add(nameStack, 1);
            
            // Selection indicator (checkmark if selected)
            if (_selectedParentCategory?.Id == category.Id)
            {
                var checkmark = new Label
                {
                    Text = "",
                    FontSize = 28,
                    TextColor = Color.FromArgb("#10B981"),
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(checkmark, 2);
            }
            
            // Tap gesture to select this category
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => OnParentCategorySelected(category);
            grid.GestureRecognizers.Add(tapGesture);
            
            // Container with border
            var container = new VerticalStackLayout { Spacing = 0 };
            container.Add(grid);
            container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E2E8F0") });
            
            return container;
        }

        private void OnParentCategorySelected(MenuCategory category)
        {
            _selectedParentCategory = category;
            ParentCategoryLabel.Text = category.Name;
            ParentCategoryLabel.TextColor = Color.FromArgb("#1E293B");
            ParentCategorySelectionDialog.IsVisible = false;
        }

        private void OnCloseParentCategoryDialog(object sender, EventArgs e)
        {
            ParentCategorySelectionDialog.IsVisible = false;
        }

        private void OnSubCategoryCustomColorTextChanged(object sender, TextChangedEventArgs e)
        {
            var hexColor = e.NewTextValue?.Trim();
            if (string.IsNullOrEmpty(hexColor)) 
            {
                SubCategoryCustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
                return;
            }
            
            // Ensure it starts with #
            if (!hexColor.StartsWith("#"))
                hexColor = "#" + hexColor;
            
            try
            {
                // Validate hex format
                var hex = hexColor.TrimStart('#');
                if (hex.Length == 3 || hex.Length == 4 || hex.Length == 6 || hex.Length == 8)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]+$"))
                    {
                        var color = Color.FromArgb(hexColor);
                        SubCategoryCustomColorPreview.BackgroundColor = color;
                        _selectedColor = hexColor;
                        
                        // Deselect preset colors
                        DeselectAllSubCategoryPresetColors();
                        
                        SubCategoryCustomColorPreview.Stroke = new SolidColorBrush(Microsoft.Maui.Graphics.Colors.DarkGray);
                        SubCategoryCustomColorPreview.StrokeThickness = 3;
                    }
                }
            }
            catch
            {
                SubCategoryCustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            }
        }

        private void BuildSubCategoryColorPicker()
        {
            SubCategoryColorPickerContainer.Children.Clear();
            
            foreach (var color in _presetColors)
            {
                var colorBorder = new Border
                {
                    BackgroundColor = Color.FromArgb(color),
                    WidthRequest = 36,
                    HeightRequest = 36,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    StrokeThickness = color == _selectedColor ? 3 : 0,
                    Stroke = Colors.White,
                    Margin = new Thickness(0, 0, 8, 8)
                };
                
                if (color == _selectedColor)
                {
                    colorBorder.Shadow = new Shadow { Brush = Colors.Black, Offset = new Point(0, 2), Radius = 4, Opacity = 0.3f };
                }
                
                var tap = new TapGestureRecognizer();
                var capturedColor = color;
                tap.Tapped += (s, e) => SelectSubCategoryColor(capturedColor);
                colorBorder.GestureRecognizers.Add(tap);
                
                SubCategoryColorPickerContainer.Children.Add(colorBorder);
            }
        }

        private void SelectSubCategoryColor(string color)
        {
            _selectedColor = color;
            
            // Clear custom color field
            SubCategoryCustomColorEntry.Text = "";
            SubCategoryCustomColorPreview.BackgroundColor = Microsoft.Maui.Graphics.Colors.LightGray;
            SubCategoryCustomColorPreview.StrokeThickness = 2;
            
            BuildSubCategoryColorPicker();
        }

        private void DeselectAllSubCategoryPresetColors()
        {
            foreach (var child in SubCategoryColorPickerContainer.Children)
            {
                if (child is Border colorBox)
                {
                    colorBox.StrokeThickness = 0;
                }
            }
        }

        private async void OnSaveSubCategoryClicked(object sender, EventArgs e)
        {
            if (_isSavingSubCategory)
            {
                return;
            }

            var name = SubCategoryNameEntry.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                await ToastNotification.ShowAsync("Required", "Please enter a sub-category name", NotificationType.Warning);
                return;
            }
            
            if (_selectedParentCategory == null)
            {
                await ToastNotification.ShowAsync("Required", "Please select a parent category", NotificationType.Warning);
                return;
            }
            
            try
            {
                _isSavingSubCategory = true;
                SaveSubCategoryButton.IsEnabled = false;
                SaveSubCategoryButton.Text = "Saving...";
                var isUpdate = _editingCategory != null;

                if (_editingCategory != null)
                {
                    _editingCategory.Name = name;
                    _editingCategory.ParentId = _selectedParentCategory.Id;
                    _editingCategory.Color = _selectedColor;
                    _editingCategory.Active = SubCategoryActiveSwitch.IsToggled;
                    _editingCategory.Icon = string.IsNullOrEmpty(_editingCategory.Icon) ? string.Empty : _editingCategory.Icon;
                    
                    await _categoryService.UpdateCategoryAsync(_editingCategory);
                }
                else
                {
                    var maxDisplayOrder = _subCategories.Any() 
                        ? _subCategories.Max(c => c.DisplayOrder) 
                        : 0;
                    
                    var newSubCategory = new MenuCategory
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        ParentId = _selectedParentCategory.Id,
                        Color = _selectedColor,
                        Icon = string.Empty,
                        Active = SubCategoryActiveSwitch.IsToggled,
                        DisplayOrder = maxDisplayOrder + 1,
                        Description = null
                    };
                    
                    var saved = await _categoryService.CreateCategoryAsync(newSubCategory);
                    if (!saved)
                    {
                        throw new InvalidOperationException("Database did not save the sub-category.");
                    }
                }

                AddSubCategoryDialog.IsVisible = false;
                _editingCategory = null;
                _selectedParentCategory = null;

                await LoadCategoriesAsync();

                _ = ToastNotification.ShowAsync(
                    "Saved",
                    isUpdate ? "Sub-category updated successfully" : "Sub-category created successfully",
                    NotificationType.Success,
                    1500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FoodMenu] Save sub-category failed: {ex.Message}");
                await ToastNotification.ShowAsync("Error", $"Failed to save sub-category: {ex.Message}", NotificationType.Error, 4000);
            }
            finally
            {
                _isSavingSubCategory = false;
                SaveSubCategoryButton.IsEnabled = true;
                SaveSubCategoryButton.Text = "Save Sub-Category";
            }
        }

        private async Task OnDeleteSubCategoryClicked(MenuCategory subCategory)
        {
            bool confirm = await ShowDeleteConfirmAsync(
                "Delete Sub-Category",
                $"Are you sure you want to delete '{subCategory.Name}'?",
                "This action cannot be undone."
            );
            if (!confirm) return;
            
            try
            {
                await _categoryService.DeleteCategoryAsync(subCategory.Id);
                await ToastNotification.ShowAsync("Deleted", "Sub-category removed", NotificationType.Success, 1500);
                await LoadAllDataAsync();
            }
            catch (Exception)
            {
                await ToastNotification.ShowAsync("Error", "Failed to delete", NotificationType.Error);
            }
        }

        private Task<bool> ShowDeleteConfirmAsync(string title, string message, string? warning = null)
        {
            DeleteConfirmTitleLabel.Text = title;
            DeleteConfirmMessageLabel.Text = message;

            var hasWarning = !string.IsNullOrWhiteSpace(warning);
            DeleteConfirmWarningBox.IsVisible = hasWarning;
            DeleteConfirmWarningLabel.Text = hasWarning ? warning : string.Empty;

            DeleteConfirmDialog.IsVisible = true;
            _deleteConfirmTaskSource = new TaskCompletionSource<bool>();
            return _deleteConfirmTaskSource.Task;
        }

        private void OnDeleteConfirmCancelClicked(object sender, EventArgs e)
        {
            CloseDeleteConfirm(false);
        }

        private void OnDeleteConfirmAcceptClicked(object sender, EventArgs e)
        {
            CloseDeleteConfirm(true);
        }

        private void CloseDeleteConfirm(bool accepted)
        {
            DeleteConfirmDialog.IsVisible = false;
            _deleteConfirmTaskSource?.TrySetResult(accepted);
            _deleteConfirmTaskSource = null;
        }

        #endregion

        #region Item & Meal Deal Actions

        private void OnAddItemClicked(object sender, EventArgs e)
        {
            Navigation.PushAsync(new AddEditItemPage());
        }
        
        private void OnEditItemClicked(FoodMenuItem item)
        {
            Navigation.PushAsync(new AddEditItemPage(item));
        }
        
        private async Task OnDeleteItemClicked(FoodMenuItem item)
        {
            bool confirm = await ShowDeleteConfirmAsync(
                "Delete Item",
                $"Are you sure you want to delete '{item.Name}'?",
                "This action cannot be undone."
            );
            
            if (confirm)
            {
                try
                {
                    await _menuItemService.DeleteItemAsync(item.Id);
                    _allItems.Remove(item);
                    BuildItemsList();
                    await ToastNotification.ShowAsync("Success", "Item deleted successfully", NotificationType.Success, 2000);
                }
                catch (Exception ex)
                {
                    await ToastNotification.ShowAsync("Error", $"Failed to delete item: {ex.Message}", NotificationType.Error, 3000);
                }
            }
        }

        private void OnAddMealDealClicked(object sender, EventArgs e)
        {
            Navigation.PushAsync(new AddEditMealDealPage());
        }

        private void OnEditMealDealClicked(MealDeal deal)
        {
            Navigation.PushAsync(new AddEditMealDealPage(deal));
        }

        private async Task OnDeleteMealDealClicked(MealDeal deal)
        {
            bool confirm = await ShowDeleteConfirmAsync(
                "Delete Meal Deal",
                $"Are you sure you want to delete '{deal.Name}'?",
                "This action cannot be undone.");

            if (!confirm)
            {
                return;
            }

            try
            {
                await _mealDealService.DeleteDealAsync(deal.Id);
                _allMealDeals.Remove(deal);
                OrderPlacementPageSimple.InvalidateMenuCache();
                BuildMealDealsList();
                await ToastNotification.ShowAsync("Success", "Meal deal deleted", NotificationType.Success, 2000);
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", ex.Message, NotificationType.Error, 3000);
            }
        }

        #endregion

        #region Helpers

        private View CreateEmptyState(string title, string message)
        {
            return new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(40, 60),
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#64748B"), HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = message, FontSize = 14, TextColor = Color.FromArgb("#94A3B8"), HorizontalOptions = LayoutOptions.Center }
                }
            };
        }

        #endregion
    }
}
