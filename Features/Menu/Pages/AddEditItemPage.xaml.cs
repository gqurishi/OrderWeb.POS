using System.Collections.ObjectModel;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using MySqlConnector;
using POS_in_NET.Services;
using Microsoft.Maui.Controls.Shapes;

namespace POS_in_NET.Pages;

public partial class AddEditItemPage : ContentPage
{
    private readonly MenuItemService _menuItemService;
    private readonly MenuCategoryService _categoryService;
    private readonly CommentNoteService _noteService;
    private readonly PrintGroupService _printGroupService;
    
    private ObservableCollection<MenuCategory> _allCategories = new();
    private List<MenuCategory> _topLevelCategories = new();
    private List<MenuCategory> _currentSubCategories = new();
    private ObservableCollection<MenuItemVariant> _variants = new();
    private ObservableCollection<Addon> _addons = new();
    private ObservableCollection<MenuItemComponent> _components = new();
    private ObservableCollection<MenuItemQuickNote> _quickNotes = new();
    private ObservableCollection<string> _componentLabels = new();
    private List<PrintGroup> _printGroups = new();
    private string _selectedColor = "#3B82F6";
    private string? _selectedCategoryId;
    private string? _selectedSubCategoryId;
    private FoodMenuItem? _editingItem;
    private bool _isEditMode = false;
    private string _selectedItemType = "Food";
    private string _selectedVatCategory = "HotFood";
    private bool _isMixedVatSelected = false;
    private Task _initializationTask = Task.CompletedTask;
    private bool _isApplyingSavedCategory;

    // Constructor for Add Mode
    public AddEditItemPage()
    {
        InitializeComponent();
        
        _menuItemService = new MenuItemService();
        _categoryService = new MenuCategoryService();
        _noteService = new CommentNoteService();
        _printGroupService = new PrintGroupService();
        
        _initializationTask = LoadDataAsync();
    }

    // Constructor for Edit Mode
    public AddEditItemPage(FoodMenuItem item) : this()
    {
        _isEditMode = true;
        _editingItem = item;
        
        PageTitle.Text = "Edit Item";
        SaveButton.Text = "Update Item";
        
        _ = LoadItemDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Load all data in parallel for speed
            var categoriesTask = _categoryService.GetAllCategoriesAsync();
            var notesTask = _noteService.GetAllNotesAsync();
            var printGroupsTask = _printGroupService.GetActivePrintGroupsAsync();
            
            await Task.WhenAll(categoriesTask, notesTask, printGroupsTask);
            
            // Cache all categories
            var allCategories = await categoriesTask;
            _allCategories.Clear();
            foreach (var cat in allCategories)
            {
                _allCategories.Add(cat);
            }
            
            // Cache print groups
            _printGroups = (await printGroupsTask).OrderBy(g => g.DisplayOrder).ToList();
            
            // Get only top-level categories (no parent) and cache them
            _topLevelCategories = allCategories
                .Where(IsTopLevelCategory)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded {_topLevelCategories.Count} top-level categories");
            foreach (var cat in _topLevelCategories)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Top Category: {cat.Name} (ID: {cat.Id})");
            }
            
            // Set category picker source with category names
            if (_topLevelCategories.Any())
            {
                CategoryComboBox.ItemsSource = _topLevelCategories.Select(c => c.Name).ToList();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Category picker loaded with {_topLevelCategories.Count} items");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] WARNING: No top-level categories found!");
            }
            
            // Initialize VAT UI defaults
            _selectedItemType = "Food";
            UpdateItemTypeSelection();
            _selectedVatCategory = "HotFood";
            _isMixedVatSelected = false;
            UpdateVatRateCardSelection(_selectedVatCategory);
            
            // Load Print Groups
            LoadPrintGroups();
            
            // Initialize Quick Notes UI
            UpdateQuickNotesUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] LoadDataAsync failed: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load data: {ex.Message}");
        }
    }

    private async Task LoadItemDataAsync()
    {
        if (_editingItem == null) return;

        try
        {
            await _initializationTask;
            
            // Basic info
            ItemNameEntry.Text = _editingItem.Name;
            var dineInPrice = _editingItem.PriceDineIn ?? _editingItem.Price;
            var takeawayPrice = _editingItem.PriceTakeaway ?? _editingItem.Price;
            DineInPriceEntry.Text = dineInPrice.ToString("F2");
            TakeawayPriceEntry.Text = takeawayPrice.ToString("F2");
            _selectedItemType = NormalizeItemType(_editingItem.ItemType);
            UpdateItemTypeSelection();
            _selectedColor = _editingItem.Color ?? "#3B82F6";
            CustomColorEntry.Text = _selectedColor;
            
            // Set category - find by ID first
            var category = _allCategories.FirstOrDefault(c => CategoryIdsEqual(c.Id, _editingItem.CategoryId));
            if (category != null)
            {
                // Check if this is a sub-category (has parent)
                if (!IsTopLevelCategory(category))
                {
                    // This is a sub-category, find parent first
                    var parentCategory = _allCategories.FirstOrDefault(c => CategoryIdsEqual(c.Id, category.ParentId));
                    if (parentCategory != null)
                    {
                        ApplySavedCategorySelection(parentCategory, category);
                    }
                }
                else
                {
                    // This is a top-level category
                    ApplySavedCategorySelection(category, null);
                }
            }
            
            // Load addons quickly
            _variants.Clear();
            VariantsContainer.Children.Clear();
            if (_editingItem.Variants != null && _editingItem.Variants.Count > 0)
            {
                foreach (var variant in _editingItem.Variants.OrderBy(v => v.DisplayOrder))
                {
                    var loadedVariant = new MenuItemVariant
                    {
                        Id = variant.Id,
                        MenuItemId = variant.MenuItemId,
                        Name = variant.Name,
                        Description = variant.Description,
                        Price = variant.Price,
                        DisplayOrder = variant.DisplayOrder,
                        Active = variant.Active,
                        CreatedAt = variant.CreatedAt,
                        UpdatedAt = variant.UpdatedAt
                    };

                    _variants.Add(loadedVariant);
                    AddVariantCard(loadedVariant);
                }
            }
            UpdateVariantsEmptyState();

            // Load addons quickly
            if (_editingItem.Addons != null && _editingItem.Addons.Count > 0)
            {
                _addons.Clear();
                foreach (var addon in _editingItem.Addons)
                {
                    var loadedAddon = new Addon
                    {
                        Id = addon.Id,
                        Name = addon.Name,
                        Price = addon.Price
                    };

                    _addons.Add(loadedAddon);
                    AddAddonCard(loadedAddon);
                }
                UpdateAddonsEmptyState();
            }
            
            // Load VAT configuration
            if (!string.IsNullOrEmpty(_editingItem.VatConfigType))
            {
                if (_editingItem.VatConfigType == "component")
                {
                    _isMixedVatSelected = true;
                    MealDealSection.IsVisible = true;
                    UpdateVatRateCardSelection(_selectedVatCategory);
                    
                    // Load components
                    var components = await _menuItemService.GetItemComponentsAsync(_editingItem.Id);
                    _components.Clear();
                    foreach (var component in components)
                    {
                        _components.Add(component);
                        AddComponentCard(component);
                    }
                    UpdateVatBreakdown();
                }
                else
                {
                    _isMixedVatSelected = false;
                    MealDealSection.IsVisible = false;
                    
                    // Load VAT category
                    var vatCategories = VATCalculator.GetVatCategories();
                    var vatCategory = vatCategories.FirstOrDefault(v => v.Value == _editingItem.VatCategory);
                    if (vatCategory != null)
                    {
                        _selectedVatCategory = vatCategory.Value;
                        UpdateVatRateCardSelection(_selectedVatCategory);
                    }
                }
            }
            
            // Load quick notes
            var quickNotes = await _menuItemService.GetQuickNotesAsync(_editingItem.Id);
            _quickNotes.Clear();
            foreach (var note in quickNotes)
            {
                _quickNotes.Add(note);
            }
            UpdateQuickNotesUI();
            
            // Load label print settings
            LabelTextEntry.Text = _editingItem.LabelText;
            PrintComponentLabelsSwitch.IsToggled = _editingItem.PrintComponentLabels;
            PrintInRedSwitch.IsToggled = _editingItem.PrintInRed;
            LoadComponentLabels(_editingItem.ComponentLabelsJson);
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Loaded print settings - LabelText: {_editingItem.LabelText}, PrintComponentLabels: {_editingItem.PrintComponentLabels}");
            
            // Load print group
            LoadPrintGroupSelection(_editingItem.PrintGroupId);
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load item data: {ex.Message}");
        }
    }

    private void OnItemNameChanged(object sender, TextChangedEventArgs e)
    {
        // Character counter removed for cleaner UI
    }

    private void OnItemTypeClicked(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            _selectedItemType = NormalizeItemType(button.StyleId);
            UpdateItemTypeSelection();
        }
    }

    private void UpdateItemTypeSelection()
    {
        SetItemTypeButtonState(FoodTypeButton, _selectedItemType == "Food");
        SetItemTypeButtonState(DrinkTypeButton, _selectedItemType == "Drink");
        SetItemTypeButtonState(OtherTypeButton, _selectedItemType == "Other");
    }

    private static void SetItemTypeButtonState(Button button, bool isSelected)
    {
        button.BackgroundColor = Color.FromArgb(isSelected ? "#DBEAFE" : "#F8FAFC");
        button.TextColor = Color.FromArgb(isSelected ? "#1D4ED8" : "#475569");
        button.BorderColor = Color.FromArgb(isSelected ? "#3B82F6" : "#CBD5E1");
        button.BorderWidth = isSelected ? 2 : 1;
    }

    private static string NormalizeItemType(string? itemType)
    {
        return itemType?.Trim().ToLowerInvariant() switch
        {
            "drink" => "Drink",
            "other" => "Other",
            _ => "Food"
        };
    }

    private void OnCategoryChanged(object sender, Syncfusion.Maui.Inputs.SelectionChangedEventArgs e)
    {
        try
        {
            if (_isApplyingSavedCategory)
            {
                return;
            }

            var selectedCategoryName = e.AddedItems?.FirstOrDefault()?.ToString();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Category changed to: {selectedCategoryName}");
            
            if (string.IsNullOrEmpty(selectedCategoryName))
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Category name is empty, returning");
                return;
            }

            // Find selected category from cached top-level categories
            var selectedCategory = _topLevelCategories.FirstOrDefault(c =>
                string.Equals(c.Name, selectedCategoryName, StringComparison.OrdinalIgnoreCase));
            
            if (selectedCategory == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] WARNING: Could not find category '{selectedCategoryName}' in top-level list");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Found category: {selectedCategory.Name} (ID: {selectedCategory.Id})");
            ApplyCategorySelection(selectedCategory, null, updateItemColor: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] OnCategoryChanged failed: {ex.Message}");
        }
    }

    private void OnSubCategoryChanged(object sender, Syncfusion.Maui.Inputs.SelectionChangedEventArgs e)
    {
        try
        {
            var selectedSubCategoryName = e.AddedItems?.FirstOrDefault() as string;
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Sub-category changed to: {selectedSubCategoryName}");
            
            if (string.IsNullOrEmpty(selectedSubCategoryName))
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Sub-category name is empty");
                _selectedSubCategoryId = null;
                return;
            }

            // Find selected sub-category from current sub-categories list
            var selectedSubCategory = _currentSubCategories.FirstOrDefault(c =>
                string.Equals(c.Name, selectedSubCategoryName, StringComparison.OrdinalIgnoreCase));
            
            if (selectedSubCategory != null)
            {
                _selectedSubCategoryId = selectedSubCategory.Id;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Selected sub-category ID: {_selectedSubCategoryId}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] WARNING: Could not find sub-category '{selectedSubCategoryName}'");
                _selectedSubCategoryId = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] OnSubCategoryChanged failed: {ex.Message}");
        }
    }

    private void ApplySavedCategorySelection(MenuCategory parentCategory, MenuCategory? subCategory)
    {
        _isApplyingSavedCategory = true;
        try
        {
            CategoryComboBox.SelectedItem = parentCategory.Name;
            ApplyCategorySelection(parentCategory, subCategory?.Id, updateItemColor: false);
            SubCategoryComboBox.SelectedItem = subCategory?.Name;
        }
        finally
        {
            _isApplyingSavedCategory = false;
        }
    }

    private void ApplyCategorySelection(MenuCategory category, string? selectedSubCategoryId, bool updateItemColor)
    {
        _selectedCategoryId = category.Id;
        if (updateItemColor)
        {
            _selectedColor = category.Color ?? "#3B82F6";
            CustomColorEntry.Text = _selectedColor;
        }

        _currentSubCategories = _allCategories
            .Where(candidate => CategoryIdsEqual(candidate.ParentId, category.Id))
            .OrderBy(candidate => candidate.DisplayOrder)
            .ThenBy(candidate => candidate.Name)
            .ToList();

        SubCategorySection.IsVisible = true;
        SubCategoryComboBox.ItemsSource = _currentSubCategories.Select(candidate => candidate.Name).ToList();
        SubCategoryComboBox.IsEnabled = _currentSubCategories.Count > 0;

        var selected = _currentSubCategories.FirstOrDefault(candidate =>
            CategoryIdsEqual(candidate.Id, selectedSubCategoryId));
        _selectedSubCategoryId = selected?.Id;
        SubCategoryComboBox.SelectedItem = selected?.Name;
        if (selected == null)
        {
            SubCategoryComboBox.SelectedIndex = -1;
        }
    }

    private static bool IsTopLevelCategory(MenuCategory category) =>
        string.IsNullOrWhiteSpace(category.ParentId)
        || string.Equals(category.ParentId.Trim(), "NULL", StringComparison.OrdinalIgnoreCase);

    private static bool CategoryIdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private void OnColorChipTapped(object sender, EventArgs e)
    {
        if (sender is Border chip)
        {
            // Color mapping for chips
            var colorMap = new Dictionary<string, string>
            {
                { "ColorChip1", "#EF4444" },  // Red
                { "ColorChip2", "#F59E0B" },  // Orange
                { "ColorChip3", "#10B981" },  // Green
                { "ColorChip4", "#3B82F6" },  // Blue
                { "ColorChip5", "#8B5CF6" },  // Purple
                { "ColorChip6", "#EC4899" }   // Pink
            };

            if (colorMap.TryGetValue(chip.StyleId ?? "", out var colorHex))
            {
                _selectedColor = colorHex;
                CustomColorEntry.Text = colorHex;
            }
            
            // Reset all chips
            ColorChip1.StrokeThickness = 2;
            ColorChip1.Stroke = Color.FromArgb("#FEE2E2");
            ColorChip2.StrokeThickness = 2;
            ColorChip2.Stroke = Color.FromArgb("#FED7AA");
            ColorChip3.StrokeThickness = 2;
            ColorChip3.Stroke = Color.FromArgb("#D1FAE5");
            ColorChip4.StrokeThickness = 2;
            ColorChip4.Stroke = Color.FromArgb("#DBEAFE");
            ColorChip5.StrokeThickness = 2;
            ColorChip5.Stroke = Color.FromArgb("#EDE9FE");
            ColorChip6.StrokeThickness = 2;
            ColorChip6.Stroke = Color.FromArgb("#FCE7F3");
            
            // Highlight selected
            chip.StrokeThickness = 3;
            chip.Stroke = Color.FromArgb(_selectedColor);
        }
    }

    private void OnCustomColorChanged(object sender, TextChangedEventArgs e)
    {
        var colorText = e.NewTextValue?.Trim();
        if (string.IsNullOrEmpty(colorText)) return;
        
        // Ensure # prefix
        if (!colorText.StartsWith("#"))
        {
            colorText = "#" + colorText;
        }
        
        // Validate hex color
        if (colorText.Length == 7 && colorText.All(c => "0123456789ABCDEFabcdef#".Contains(c)))
        {
            _selectedColor = colorText.ToUpper();
            
            // Reset all color chips
            ColorChip1.StrokeThickness = 2;
            ColorChip1.Stroke = Color.FromArgb("#FEE2E2");
            ColorChip2.StrokeThickness = 2;
            ColorChip2.Stroke = Color.FromArgb("#FED7AA");
            ColorChip3.StrokeThickness = 2;
            ColorChip3.Stroke = Color.FromArgb("#D1FAE5");
            ColorChip4.StrokeThickness = 2;
            ColorChip4.Stroke = Color.FromArgb("#DBEAFE");
            ColorChip5.StrokeThickness = 2;
            ColorChip5.Stroke = Color.FromArgb("#EDE9FE");
            ColorChip6.StrokeThickness = 2;
            ColorChip6.Stroke = Color.FromArgb("#FCE7F3");
        }
    }

    private void OnAddAddonClicked(object sender, EventArgs e)
    {
        var addon = new Addon
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.Empty,
            Price = 0m
        };

        _addons.Add(addon);
        AddAddonCard(addon);
        UpdateAddonsEmptyState();
    }

    private void OnAddVariantClicked(object sender, EventArgs e)
    {
        var variant = new MenuItemVariant
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.Empty,
            Description = null,
            Price = 0m,
            DisplayOrder = _variants.Count,
            Active = true
        };

        _variants.Add(variant);
        AddVariantCard(variant);
        UpdateVariantsEmptyState();
    }

    private void AddVariantCard(MenuItemVariant variant)
    {
        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 6, 0, 10)
        };

        var contentStack = new VerticalStackLayout { Spacing = 14 };
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12
        };

        var titleLabel = new Label
        {
            StyleId = "VariantCardTitle",
            Text = "Variant",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalOptions = LayoutOptions.Center
        };

        var activeSwitch = new Switch
        {
            IsToggled = variant.Active,
            VerticalOptions = LayoutOptions.Center
        };
        activeSwitch.Toggled += (_, e) => variant.Active = e.Value;

        var deleteButton = new Button
        {
            Text = "",
            FontSize = 24,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#0F172A"),
            WidthRequest = 40,
            HeightRequest = 40,
            CornerRadius = 20,
            Padding = 0
        };
        deleteButton.Clicked += (_, _) =>
        {
            _variants.Remove(variant);
            VariantsContainer.Children.Remove(card);
            RefreshVariantCardTitles();
            UpdateVariantsEmptyState();
        };

        headerGrid.Add(titleLabel, 0, 0);
        headerGrid.Add(activeSwitch, 1, 0);
        headerGrid.Add(deleteButton, 2, 0);
        contentStack.Add(headerGrid);

        var detailsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        var nameEntry = CreateVariantEntry("Variant Name", "e.g., Large", variant.Name);
        nameEntry.TextChanged += (_, e) => variant.Name = e.NewTextValue?.Trim() ?? string.Empty;
        detailsGrid.Add(WrapVariantField("Variant Name", nameEntry), 0, 0);

        var priceEntry = CreateVariantEntry("Price", "0.00", variant.Price == 0m ? string.Empty : variant.Price.ToString("F2"));
        priceEntry.Keyboard = Keyboard.Numeric;
        priceEntry.TextChanged += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                variant.Price = 0m;
                return;
            }

            if (decimal.TryParse(e.NewTextValue, out var parsedPrice))
            {
                variant.Price = Math.Max(0m, parsedPrice);
            }
        };
        detailsGrid.Add(WrapVariantField("Price (£)", priceEntry), 1, 0);

        var descriptionEntry = CreateVariantEntry("Description", "Optional", variant.Description ?? string.Empty);
        descriptionEntry.TextChanged += (_, e) => variant.Description = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue.Trim();
        detailsGrid.Add(WrapVariantField("Description", descriptionEntry), 2, 0);

        contentStack.Add(detailsGrid);
        card.Content = contentStack;

        VariantsContainer.Children.Add(card);
        RefreshVariantCardTitles();
    }

    private static Entry CreateVariantEntry(string automationId, string placeholder, string text) => new()
    {
        AutomationId = automationId,
        Placeholder = placeholder,
        Text = text,
        FontSize = 14,
        TextColor = Color.FromArgb("#1E293B"),
        PlaceholderColor = Color.FromArgb("#94A3B8"),
        BackgroundColor = Colors.Transparent,
        HeightRequest = 44
    };

    private static View WrapVariantField(string label, Entry entry)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Add(new Label
        {
            Text = label,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569")
        });
        stack.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 0),
            Content = entry
        });
        return stack;
    }

    private void RefreshVariantCardTitles()
    {
        for (int i = 0; i < VariantsContainer.Children.Count; i++)
        {
            if (VariantsContainer.Children[i] is Border card &&
                card.Content is VerticalStackLayout stack &&
                stack.Children.FirstOrDefault() is Grid header &&
                header.Children.FirstOrDefault() is Label titleLabel &&
                titleLabel.StyleId == "VariantCardTitle")
            {
                titleLabel.Text = $"Variant {i + 1}";
            }
        }
    }

    private void UpdateVariantsEmptyState()
    {
        VariantsEmptyState.IsVisible = !_variants.Any();
        VariantCountLabel.Text = $"{_variants.Count} variant{(_variants.Count == 1 ? string.Empty : "s")}";
    }

    private bool TryBuildValidatedVariants(out List<MenuItemVariant> validatedVariants, out string validationError)
    {
        validatedVariants = new List<MenuItemVariant>();
        validationError = string.Empty;

        for (int i = 0; i < _variants.Count; i++)
        {
            var variant = _variants[i];
            var name = variant.Name?.Trim() ?? string.Empty;
            var description = variant.Description?.Trim();
            var price = Math.Round(variant.Price, 2);

            if (string.IsNullOrEmpty(name))
            {
                if (price > 0m || !string.IsNullOrWhiteSpace(description))
                {
                    validationError = $"Variant {i + 1} has details but no name. Please enter a name.";
                    return false;
                }

                continue;
            }

            if (price <= 0m)
            {
                validationError = $"Variant {i + 1} needs a price greater than 0.";
                return false;
            }

            validatedVariants.Add(new MenuItemVariant
            {
                Id = string.IsNullOrWhiteSpace(variant.Id) ? Guid.NewGuid().ToString() : variant.Id,
                Name = name,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                Price = price,
                DisplayOrder = validatedVariants.Count,
                Active = variant.Active,
                CreatedAt = variant.CreatedAt == default ? DateTime.Now : variant.CreatedAt,
                UpdatedAt = DateTime.Now
            });
        }

        return true;
    }

    private void AddAddonCard(Addon addon)
    {
        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 6, 0, 10)
        };

        var contentStack = new VerticalStackLayout
        {
            Spacing = 14
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var titleLabel = new Label
        {
            StyleId = "AddonCardTitle",
            Text = "Add-on",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalOptions = LayoutOptions.Center
        };

        var deleteButton = new Button
        {
            Text = "",
            FontSize = 24,
            FontAttributes = FontAttributes.None,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#0F172A"),
            WidthRequest = 40,
            HeightRequest = 40,
            CornerRadius = 20,
            Padding = 0
        };
        deleteButton.Clicked += (s, e) =>
        {
            _addons.Remove(addon);
            AddonsContainer.Children.Remove(card);
            RefreshAddonCardTitles();
            UpdateAddonsEmptyState();
        };

        headerGrid.Add(titleLabel, 0, 0);
        headerGrid.Add(deleteButton, 1, 0);
        contentStack.Add(headerGrid);

        var detailsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        var nameStack = new VerticalStackLayout { Spacing = 6 };
        nameStack.Add(new Label
        {
            Text = "Add-on Name",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569")
        });

        var nameEntry = new Entry
        {
            Placeholder = "e.g., Extra Cheese",
            Text = addon.Name,
            FontSize = 14,
            TextColor = Color.FromArgb("#1E293B"),
            PlaceholderColor = Color.FromArgb("#94A3B8"),
            BackgroundColor = Colors.Transparent,
            HeightRequest = 44
        };
        nameEntry.TextChanged += (s, e) => addon.Name = e.NewTextValue?.Trim() ?? string.Empty;

        nameStack.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 0),
            Content = nameEntry
        });
        detailsGrid.Add(nameStack, 0, 0);

        var priceStack = new VerticalStackLayout { Spacing = 6 };
        priceStack.Add(new Label
        {
            Text = "Price (£)",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569")
        });

        var priceEntry = new Entry
        {
            Placeholder = "0.00",
            Text = addon.Price == 0m ? string.Empty : addon.Price.ToString("F2"),
            FontSize = 14,
            TextColor = Color.FromArgb("#1E293B"),
            PlaceholderColor = Color.FromArgb("#94A3B8"),
            BackgroundColor = Colors.Transparent,
            Keyboard = Keyboard.Numeric,
            HeightRequest = 44
        };
        priceEntry.TextChanged += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                addon.Price = 0m;
                return;
            }

            if (decimal.TryParse(e.NewTextValue, out var parsedPrice))
            {
                addon.Price = Math.Max(0m, parsedPrice);
            }
        };

        priceStack.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 0),
            Content = priceEntry
        });
        detailsGrid.Add(priceStack, 1, 0);

        contentStack.Add(detailsGrid);
        card.Content = contentStack;

        AddonsContainer.Children.Add(card);
        RefreshAddonCardTitles();
    }

    private void RefreshAddonCardTitles()
    {
        for (int i = 0; i < AddonsContainer.Children.Count; i++)
        {
            if (AddonsContainer.Children[i] is Border card &&
                card.Content is VerticalStackLayout stack &&
                stack.Children.FirstOrDefault() is Grid header &&
                header.Children.FirstOrDefault() is Label titleLabel &&
                titleLabel.StyleId == "AddonCardTitle")
            {
                titleLabel.Text = $"Add-on {i + 1}";
            }
        }
    }

    private void UpdateAddonsEmptyState()
    {
        AddonsEmptyState.IsVisible = !_addons.Any();
        AddonCountLabel.Text = $"{_addons.Count} addon{(_addons.Count == 1 ? string.Empty : "s")}";
    }

    private bool TryBuildValidatedAddons(out List<Addon> validatedAddons, out string validationError)
    {
        validatedAddons = new List<Addon>();
        validationError = string.Empty;

        for (int i = 0; i < _addons.Count; i++)
        {
            var addon = _addons[i];
            var name = addon.Name?.Trim() ?? string.Empty;
            var price = Math.Round(addon.Price, 2);

            if (string.IsNullOrEmpty(name))
            {
                if (price > 0m)
                {
                    validationError = $"Add-on {i + 1} has a price but no name. Please enter a name.";
                    return false;
                }

                continue;
            }

            if (price < 0m)
            {
                validationError = $"Add-on {i + 1} has invalid price. Price cannot be negative.";
                return false;
            }

            validatedAddons.Add(new Addon
            {
                Id = string.IsNullOrWhiteSpace(addon.Id) ? Guid.NewGuid().ToString() : addon.Id,
                Name = name,
                Price = price
            });
        }

        return true;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation, source: sender as VisualElement);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation, source: sender as VisualElement);
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(ItemNameEntry.Text))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please enter an item name.");
                return;
            }

            if (string.IsNullOrEmpty(_selectedCategoryId))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please select a category.");
                return;
            }

            if (string.IsNullOrWhiteSpace(DineInPriceEntry.Text) || !decimal.TryParse(DineInPriceEntry.Text, out decimal dineInPrice) || dineInPrice < 0)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Validation Error",
                    "Please enter a valid dine-in/table price. Use 0.00 if this is a free item.");
                return;
            }

            if (string.IsNullOrWhiteSpace(TakeawayPriceEntry.Text) || !decimal.TryParse(TakeawayPriceEntry.Text, out decimal takeawayPrice) || takeawayPrice < 0)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Validation Error",
                    "Please enter a valid takeaway/collection/delivery price. Use 0 if this item is dine-in/table only.");
                return;
            }

            if (!TryBuildValidatedVariants(out var validatedVariants, out var variantValidationError))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", variantValidationError);
                return;
            }

            if (!TryBuildValidatedAddons(out var validatedAddons, out var addonValidationError))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", addonValidationError);
                return;
            }

            _variants = new ObservableCollection<MenuItemVariant>(validatedVariants);
            UpdateVariantsEmptyState();

            _addons = new ObservableCollection<Addon>(validatedAddons);
            UpdateAddonsEmptyState();

            bool isStandard = IsSimpleVatTypeSelected();
            if (!isStandard && takeawayPrice > 0)
            {
                if (!ValidateMixedItemComponents(takeawayPrice, out var mixedValidationError))
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", mixedValidationError);
                    return;
                }
            }

            // Determine final category ID to save
            // If sub-category is selected, use it; otherwise use main category
            string finalCategoryId = _selectedSubCategoryId ?? _selectedCategoryId;
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Saving item:");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Name: {ItemNameEntry.Text}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Category ID: {_selectedCategoryId}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Sub-Category ID: {_selectedSubCategoryId}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Final Category ID: {finalCategoryId}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Dine-in Price: {dineInPrice}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Takeaway Price: {takeawayPrice}");
            System.Diagnostics.Debug.WriteLine($"[DEBUG]   Color: {_selectedColor}");

            if (_isEditMode && _editingItem != null)
            {
                // Update existing item
                _editingItem.Name = ItemNameEntry.Text.Trim();
                _editingItem.CategoryId = finalCategoryId;
                _editingItem.Price = dineInPrice; // Legacy fallback field
                _editingItem.PriceDineIn = dineInPrice;
                _editingItem.PriceTakeaway = takeawayPrice;
                _editingItem.ItemType = _selectedItemType;
                _editingItem.Color = _selectedColor;
                _editingItem.Variants = _variants.ToList();
                _editingItem.Addons = _addons.ToList();
                _editingItem.UpdatedAt = DateTime.Now;
                
                // Update VAT configuration
                _editingItem.VatConfigType = isStandard ? "standard" : "component";
                
                if (isStandard)
                {
                    _editingItem.VatCategory = _selectedVatCategory;
                    _editingItem.CalculatedVatRate = GetStandardTakeawayVatRate(_editingItem.VatCategory);
                }
                else
                {
                    // Calculate effective VAT rate from components
                    decimal totalPrice = _components.Sum(c => c.ComponentPrice);
                    decimal totalVat = _components.Sum(c => c.ComponentType switch
                    {
                        "ColdFood" => 0,
                        "ColdBeverage" => 0,
                        _ => ExtractVatFromInclusivePrice(c.ComponentPrice, 20m)
                    });
                    _editingItem.CalculatedVatRate = totalPrice > 0 ? (totalVat / totalPrice) * 100 : 0;
                }
                
                // Keep legacy fields for backward compatibility
                _editingItem.VatRate = _editingItem.CalculatedVatRate;
                _editingItem.VatType = _editingItem.VatConfigType;
                
                // Save label print settings
                _editingItem.LabelText = string.IsNullOrWhiteSpace(LabelTextEntry.Text) ? null : LabelTextEntry.Text.Trim();
                _editingItem.PrintComponentLabels = PrintComponentLabelsSwitch.IsToggled;
                _editingItem.ComponentLabelsJson = GetComponentLabelsJson();
                _editingItem.PrintInRed = PrintInRedSwitch.IsToggled;
                
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Print settings - LabelText: {_editingItem.LabelText}, PrintComponentLabels: {_editingItem.PrintComponentLabels}, ComponentLabels: {_editingItem.ComponentLabelsJson}");
                
                // Save print group
                _editingItem.PrintGroupId = GetSelectedPrintGroupId();

                System.Diagnostics.Debug.WriteLine($"[DEBUG] Updating item with CategoryId: {_editingItem.CategoryId}");
                var updateResult = await _menuItemService.UpdateItemAsync(_editingItem);
                if (!updateResult)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Item details could not be updated.");
                    return;
                }
                
                // Save components if meal deal
                if (!isStandard)
                {
                    var componentsSaved = await _menuItemService.SaveItemComponentsAsync(_editingItem.Id, _components.ToList());
                    if (!componentsSaved)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Mixed item components were not saved. Please try again.");
                        return;
                    }
                }
                
                // Save quick notes
                var quickNotesSaved = await _menuItemService.SaveQuickNotesAsync(
                    _editingItem.Id,
                    PrepareQuickNotesForSave(_editingItem.Id));
                if (!quickNotesSaved)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Quick notes were not saved. Please try again.");
                    return;
                }

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Item updated successfully!");
            }
            else
            {
                // Create new item
                
                // Get VAT category and calculate rate
                string vatCategory = _selectedVatCategory;
                decimal calculatedVatRate = 20;
                
                if (isStandard)
                {
                    vatCategory = _selectedVatCategory;
                    calculatedVatRate = GetStandardTakeawayVatRate(vatCategory);
                }
                else
                {
                    // Calculate from components
                    decimal totalPrice = _components.Sum(c => c.ComponentPrice);
                    decimal totalVat = _components.Sum(c => c.ComponentType switch
                    {
                        "ColdFood" => 0,
                        "ColdBeverage" => 0,
                        _ => ExtractVatFromInclusivePrice(c.ComponentPrice, 20m)
                    });
                    calculatedVatRate = totalPrice > 0 ? (totalVat / totalPrice) * 100 : 0;
                }
                
                var newItem = new FoodMenuItem
                {
                    Id = Guid.NewGuid().ToString(),
                    CategoryId = finalCategoryId,
                    Name = ItemNameEntry.Text.Trim(),
                    Price = dineInPrice, // Legacy fallback field
                    PriceDineIn = dineInPrice,
                    PriceTakeaway = takeawayPrice,
                    ItemType = _selectedItemType,
                    Color = _selectedColor,
                    Variants = _variants.ToList(),
                    Addons = _addons.ToList(),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    
                    // New VAT fields
                    VatConfigType = isStandard ? "standard" : "component",
                    VatCategory = vatCategory,
                    CalculatedVatRate = calculatedVatRate,
                    
                    // Legacy fields
                    VatRate = calculatedVatRate,
                    VatType = isStandard ? "simple" : "component",
                    IsVatExempt = vatCategory == "NoVAT",
                    
                    // Label print settings
                    LabelText = string.IsNullOrWhiteSpace(LabelTextEntry.Text) ? null : LabelTextEntry.Text.Trim(),
                    PrintComponentLabels = PrintComponentLabelsSwitch.IsToggled,
                    ComponentLabelsJson = GetComponentLabelsJson(),
                    PrintInRed = PrintInRedSwitch.IsToggled,
                    
                    // Print group
                    PrintGroupId = GetSelectedPrintGroupId()
                };

                System.Diagnostics.Debug.WriteLine($"[DEBUG] Creating new item with CategoryId: {newItem.CategoryId}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] VAT Config: Type={newItem.VatConfigType}, Category={newItem.VatCategory}, Rate={newItem.CalculatedVatRate}");
                
                var createResult = await _menuItemService.CreateItemAsync(newItem);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Create result: {createResult}");
                if (!createResult)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Item could not be created.");
                    return;
                }
                
                // Save components if meal deal
                if (!isStandard && _components.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Saving {_components.Count} components...");
                    var componentsSaved = await _menuItemService.SaveItemComponentsAsync(newItem.Id, _components.ToList());
                    if (!componentsSaved)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Item was created but mixed components were not saved. Please edit item and retry.");
                        return;
                    }
                }
                
                // Save quick notes
                if (_quickNotes.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Saving {_quickNotes.Count} quick notes...");
                    var quickNotesSaved = await _menuItemService.SaveQuickNotesAsync(
                        newItem.Id,
                        PrepareQuickNotesForSave(newItem.Id));
                    if (!quickNotesSaved)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Save Failed", "Item was created but quick notes were not saved. Please edit item and retry.");
                        return;
                    }
                }

                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Item created successfully!");
            }

            // Go back
            OrderPlacementPageSimple.InvalidateMenuCache();
            await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation);
        }
        catch (MySqlConnector.MySqlException mysqlEx)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] MySQL Error: {mysqlEx.Message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] MySQL Error Number: {mysqlEx.Number}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] Stack trace: {mysqlEx.StackTrace}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Database Error", $"Failed to save item: {mysqlEx.Message}\n\nError Code: {mysqlEx.Number}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] General Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save item: {ex.Message}\n\n{ex.GetType().Name}");
        }
    }

    // ========================================
    // VAT Configuration Handlers
    // ========================================

    private static decimal GetStandardTakeawayVatRate(string vatCategory)
    {
        return vatCategory switch
        {
            "NoVAT" => 0m,
            "ColdFood" => 0m,
            "ColdBeverage" => 0m,
            "HotFood" => 20m,
            "HotBeverage" => 20m,
            "Alcohol" => 20m,
            _ => 20m
        };
    }

    private static decimal ExtractVatFromInclusivePrice(decimal grossPrice, decimal vatRatePercent)
    {
        if (grossPrice <= 0m || vatRatePercent <= 0m)
        {
            return 0m;
        }

        return grossPrice * vatRatePercent / (100m + vatRatePercent);
    }

    private bool ValidateMixedItemComponents(decimal targetTakeawayPrice, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (_components.Count == 0)
        {
            errorMessage = "Mixed item requires at least one component.";
            return false;
        }

        var invalidComponent = _components.FirstOrDefault(c =>
            string.IsNullOrWhiteSpace(c.ComponentName) ||
            c.ComponentPrice <= 0m ||
            string.IsNullOrWhiteSpace(c.ComponentType));

        if (invalidComponent != null)
        {
            errorMessage = "Each mixed item component must have a name, type, and valid price greater than 0.";
            return false;
        }

        var componentTotal = _components.Sum(c => c.ComponentPrice);
        if (Math.Abs(componentTotal - targetTakeawayPrice) > 0.01m)
        {
            errorMessage = $"Mixed item components total (£{componentTotal:F2}) must match Takeaway price (£{targetTakeawayPrice:F2}).";
            return false;
        }

        return true;
    }

    private bool IsSimpleVatTypeSelected()
    {
        return !_isMixedVatSelected;
    }

    private void OnVatTypeChanged(object sender, CheckedChangedEventArgs e)
    {
        // Legacy handler kept intentionally for backward compatibility with archived XAML variants.
    }

    private void OnVatRateCardTapped(object sender, TappedEventArgs e)
    {
        var selection = e.Parameter?.ToString();

        if (selection == "mix")
        {
            _isMixedVatSelected = true;
            MealDealSection.IsVisible = true;

            if (_components.Count == 0 && !_isEditMode)
            {
                AddExampleComponents();
            }

            UpdateVatRateCardSelection(_selectedVatCategory);
            return;
        }

        _isMixedVatSelected = false;
        MealDealSection.IsVisible = false;

        if (selection == "0")
        {
            _selectedVatCategory = "NoVAT";
            UpdateVatRateCardSelection("NoVAT");
            return;
        }

        _selectedVatCategory = "HotFood";
        UpdateVatRateCardSelection("HotFood");
    }

    private void UpdateVatRateCardSelection(string vatCategoryValue)
    {
        bool zeroRated = vatCategoryValue is "NoVAT" or "ColdFood" or "ColdBeverage";
        bool standardRated = !zeroRated && !_isMixedVatSelected;
        bool mixedSelected = _isMixedVatSelected;

        SetVatCardState(ZeroVatCard, zeroRated, zeroRated ? "#DBEAFE" : "#CBD5E1", zeroRated ? "#1D4ED8" : "#475569", zeroRated ? 3 : 2);
        SetVatCardState(StandardVatCard, standardRated, standardRated ? "#DBEAFE" : "#CBD5E1", standardRated ? "#1D4ED8" : "#475569", standardRated ? 3 : 2);
        SetVatCardState(MixedVatCard, mixedSelected, mixedSelected ? "#DBEAFE" : "#CBD5E1", mixedSelected ? "#1D4ED8" : "#475569", mixedSelected ? 3 : 2);
    }

    private static void SetVatCardState(Border card, bool isSelected, string selectedStroke, string selectedTextColor, double strokeThickness)
    {
        card.BackgroundColor = isSelected ? Color.FromArgb("#EFF6FF") : Colors.White;
        card.Stroke = Color.FromArgb(selectedStroke);
        card.StrokeThickness = strokeThickness;

        if (card.Content is VerticalStackLayout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Label label)
                {
                    if (label.Text == "0%" || label.Text == "20%" || label.Text == "Mix VAT")
                    {
                        label.TextColor = Color.FromArgb(selectedTextColor);
                    }
                    else
                    {
                        label.TextColor = isSelected ? Color.FromArgb("#2563EB") : Color.FromArgb("#64748B");
                    }
                }
            }
        }
    }

    private void OnAddComponentClicked(object sender, EventArgs e)
    {
        AddComponentCard();
    }

    private void AddExampleComponents()
    {
        // Add example components for first-time users
        var component1 = new MenuItemComponent
        {
            ComponentName = "Main Item (Hot)",
            ComponentPrice = 12.00m,
            ComponentType = "HotFood",
            SortOrder = 1
        };
        
        var component2 = new MenuItemComponent
        {
            ComponentName = "Side (Cold)",
            ComponentPrice = 3.00m,
            ComponentType = "ColdFood",
            SortOrder = 2
        };

        _components.Add(component1);
        _components.Add(component2);
        
        AddComponentCard(component1);
        AddComponentCard(component2);
        
        UpdateVatBreakdown();
    }

    private void AddComponentCard(MenuItemComponent? component = null)
    {
        var newComponent = component ?? new MenuItemComponent
        {
            ComponentName = "",
            ComponentPrice = 0m,
            ComponentType = "HotFood",
            SortOrder = _components.Count + 1
        };

        if (component == null)
        {
            _components.Add(newComponent);
        }

        // Create component card UI
        var card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };

        var stack = new VerticalStackLayout { Spacing = 10 };

        // Component number and remove button
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var numberLabel = new Label
        {
            Text = $"Component {newComponent.SortOrder}",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569")
        };

        var removeButton = new Button
        {
            Text = "Remove",
            BackgroundColor = Color.FromArgb("#EF4444"),
            TextColor = Colors.White,
            FontSize = 12,
            CornerRadius = 6,
            HeightRequest = 32,
            Padding = new Thickness(12, 0)
        };
        removeButton.Clicked += (s, e) =>
        {
            _components.Remove(newComponent);
            ComponentsContainer.Children.Remove(card);
            UpdateVatBreakdown();
        };

        headerGrid.Add(numberLabel, 0, 0);
        headerGrid.Add(removeButton, 1, 0);
        stack.Add(headerGrid);

        // Component name
        var nameEntry = new Entry
        {
            Placeholder = "e.g., Chicken Biryani",
            Text = newComponent.ComponentName,
            FontSize = 14
        };
        nameEntry.TextChanged += (s, e) =>
        {
            newComponent.ComponentName = e.NewTextValue;
            UpdateVatBreakdown();
        };
        stack.Add(nameEntry);

        // Price and Type grid
        var detailsGrid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        // Price entry
        var priceStack = new VerticalStackLayout { Spacing = 4 };
        priceStack.Add(new Label { Text = "Price (£)", FontSize = 12, TextColor = Color.FromArgb("#64748B") });
        var priceEntry = new Entry
        {
            Placeholder = "0.00",
            Keyboard = Keyboard.Numeric,
            Text = newComponent.ComponentPrice > 0 ? newComponent.ComponentPrice.ToString("F2") : "",
            FontSize = 14
        };
        priceEntry.TextChanged += (s, e) =>
        {
            if (decimal.TryParse(e.NewTextValue, out var price))
            {
                newComponent.ComponentPrice = price;
                UpdateVatBreakdown();
            }
        };
        priceStack.Add(priceEntry);
        detailsGrid.Add(priceStack, 0, 0);

        // Type selector with modern card-based UI
        var typeStack = new VerticalStackLayout { Spacing = 8 };
        typeStack.Add(new Label 
        { 
            Text = "Component Type", 
            FontSize = 13, 
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E293B") 
        });

        var typeGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            RowSpacing = 8,
            ColumnSpacing = 8
        };

        var componentTypes = new[]
        {
            new { Display = "Hot Food", Value = "HotFood", Color = "#EF4444" },
            new { Display = "Cold Food", Value = "ColdFood", Color = "#3B82F6" },
            new { Display = "Hot Beverage", Value = "HotBeverage", Color = "#F59E0B" },
            new { Display = "Cold Beverage", Value = "ColdBeverage", Color = "#06B6D4" },
            new { Display = "Alcohol", Value = "Alcohol", Color = "#8B5CF6" }
        };

        var typeButtons = new List<Border>();
        int row = 0, col = 0;

        foreach (var type in componentTypes)
        {
            var isSelected = newComponent.ComponentType == type.Value;
            
            var border = new Border
            {
                BackgroundColor = isSelected ? Color.FromArgb(type.Color) : Colors.White,
                Stroke = Color.FromArgb(type.Color),
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                HeightRequest = 50
            };

            var label = new Label
            {
                Text = type.Display,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = isSelected ? Colors.White : Color.FromArgb(type.Color),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            border.Content = label;
            
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) =>
            {
                // Reset all buttons
                foreach (var btn in typeButtons)
                {
                    var btnLabel = (Label)btn.Content;
                    var btnColor = btn.Stroke.ToString();
                    btn.BackgroundColor = Colors.White;
                    btnLabel.TextColor = Color.FromArgb(btnColor);
                }

                // Select this button
                border.BackgroundColor = Color.FromArgb(type.Color);
                label.TextColor = Colors.White;
                
                // Update component type
                newComponent.ComponentType = type.Value;
                UpdateVatBreakdown();
            };
            
            border.GestureRecognizers.Add(tapGesture);
            typeButtons.Add(border);

            typeGrid.Add(border, col, row);
            
            col++;
            if (col > 1)
            {
                col = 0;
                row++;
            }
        }

        typeStack.Add(typeGrid);
        detailsGrid.Add(typeStack, 1, 0);

        stack.Add(detailsGrid);
        card.Content = stack;
        ComponentsContainer.Add(card);
    }

    private void UpdateVatBreakdown()
    {
        if (_components.Count == 0)
        {
            VatBreakdownBorder.IsVisible = false;
            VatMismatchWarningLabel.IsVisible = false;
            return;
        }

        VatBreakdownBorder.IsVisible = true;
        VatBreakdownStack.Clear();

        decimal totalPrice = 0m;
        decimal totalVat = 0m;

        foreach (var component in _components)
        {
            if (component.ComponentPrice <= 0) continue;

            totalPrice += component.ComponentPrice;
            decimal componentVat = component.ComponentType switch
            {
                "ColdFood" => 0m,
                "ColdBeverage" => 0m,
                _ => ExtractVatFromInclusivePrice(component.ComponentPrice, 20m)
            };
            totalVat += componentVat;

            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Add(new Label
            {
                Text = $"• {component.ComponentName}: £{component.ComponentPrice:F2} (incl) -> VAT ",
                FontSize = 12,
                TextColor = Color.FromArgb("#64748B")
            });
            row.Add(new Label
            {
                Text = $"£{componentVat:F2}",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = componentVat > 0 ? Color.FromArgb("#10B981") : Color.FromArgb("#64748B")
            });
            VatBreakdownStack.Add(row);
        }

        UpdateMixedItemWarning(totalPrice);
        EffectiveVatLabel.Text = $"£{totalVat:F2}";
    }

    private void OnMixedItemPriceChanged(object sender, TextChangedEventArgs e)
    {
        UpdateVatBreakdown();
    }

    private void UpdateMixedItemWarning(decimal componentTotal)
    {
        if (!_isMixedVatSelected)
        {
            VatMismatchWarningLabel.IsVisible = false;
            return;
        }

        if (!decimal.TryParse(TakeawayPriceEntry.Text, out var takeawayPrice) || takeawayPrice <= 0m)
        {
            VatMismatchWarningLabel.IsVisible = false;
            return;
        }

        var mismatch = Math.Abs(componentTotal - takeawayPrice) > 0.01m;
        VatMismatchWarningLabel.IsVisible = mismatch;

        if (mismatch)
        {
            VatMismatchWarningLabel.Text = $"Warning: mixed item total (£{componentTotal:F2}) does not match item price (£{takeawayPrice:F2}). Please fix the prices.";
        }
    }

    // ===================================
    // PRINT GROUP HANDLERS
    // ===================================
    
    private void LoadPrintGroups()
    {
        // Print groups are loaded on-demand when needed
    }
    
    private void LoadPrintGroupSelection(string? printGroupId)
    {
        _selectedPrintGroupId = printGroupId;
        UpdatePrintGroupButton();
    }
    
    private async void OnSelectPrintGroupClicked(object sender, EventArgs e)
    {
        if (_printGroupService == null) return;

        try
        {
            // Get all print groups created in Printer Setup
            var allGroups = await _printGroupService.GetAllPrintGroupsAsync();
            var activeGroups = allGroups.Where(g => g.IsActive).ToList();

            if (!activeGroups.Any())
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("No Print Groups", "No print groups found. Please create print groups in Printer Setup first.");
                return;
            }

            // Show selection dialog
            var dialog = new Views.PrintGroupDialog(activeGroups, _selectedPrintGroupId);
            DialogOverlay.Content = dialog;
            DialogOverlay.IsVisible = true;

            var selectedId = await dialog.ShowAsync();

            DialogOverlay.IsVisible = false;
            DialogOverlay.Content = null;

            // Update selection
            if (!string.IsNullOrEmpty(selectedId))
            {
                _selectedPrintGroupId = selectedId;
                UpdatePrintGroupButton();
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load print groups: {ex.Message}");
        }
    }

    private void UpdatePrintGroupButton()
    {
        if (string.IsNullOrEmpty(_selectedPrintGroupId))
        {
            PrintGroupButton.Text = "Select Print Group...";
            PrintGroupButton.TextColor = Color.FromArgb("#94A3B8");
            return;
        }

        // Find group name
        if (_printGroupService != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    var allGroups = await _printGroupService.GetAllPrintGroupsAsync();
                    var group = allGroups.FirstOrDefault(g => g.Id == _selectedPrintGroupId);
                    
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (group != null)
                        {
                            PrintGroupButton.Text = $" {group.Name}";
                            PrintGroupButton.TextColor = Color.FromArgb("#22C55E");
                        }
                    });
                }
                catch { }
            });
        }
    }
    
    private string? GetSelectedPrintGroupId()
    {
        return _selectedPrintGroupId;
    }
    
    private Task<string?> GetSelectedPrintGroupIdAsync()
    {
        return Task.FromResult(_selectedPrintGroupId);
    }
    
    private string? _selectedPrintGroupId;

    // ===================================
    // QUICK NOTES HANDLERS
    // ===================================
    
    private void UpdateQuickNotesUI()
    {
        QuickNotesListContainer.Children.Clear();
        QuickNotesCountLabel.Text = $"{_quickNotes.Count}/6";
        AddQuickNoteButton.IsEnabled = _quickNotes.Count < 6;
        AddQuickNoteButton.Opacity = _quickNotes.Count < 6 ? 1.0 : 0.5;
        
        // Update display order
        for (int i = 0; i < _quickNotes.Count; i++)
        {
            _quickNotes[i].DisplayOrder = i + 1;
        }

        if (_quickNotes.Count == 0)
        {
            QuickNotesListContainer.Children.Add(new Label
            {
                Text = "No quick notes yet. Click '+ Add Quick Note' below.",
                FontSize = 13,
                TextColor = Color.FromArgb("#94A3B8"),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20)
            });
            return;
        }

        foreach (var note in _quickNotes.OrderBy(note => note.DisplayOrder))
        {
            QuickNotesListContainer.Children.Add(CreateQuickNoteRow(note));
        }
    }

    private Border CreateQuickNoteRow(MenuItemQuickNote note)
    {
        var border = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        grid.Add(new Label
        {
            Text = note.NoteText,
            FontSize = 14,
            TextColor = Color.FromArgb("#1E293B"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 0, 0);

        var editButton = new Button
        {
            Text = "Edit",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            TextColor = Color.FromArgb("#64748B"),
            WidthRequest = 54,
            HeightRequest = 36,
            CornerRadius = 8,
            Padding = 0,
            CommandParameter = note
        };
        editButton.Clicked += OnEditQuickNoteClicked;
        grid.Add(editButton, 1, 0);

        var deleteButton = new Button
        {
            Text = "X",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#FEE2E2"),
            TextColor = Color.FromArgb("#DC2626"),
            WidthRequest = 36,
            HeightRequest = 36,
            CornerRadius = 8,
            Padding = 0,
            CommandParameter = note
        };
        deleteButton.Clicked += OnDeleteQuickNoteClicked;
        grid.Add(deleteButton, 2, 0);

        border.Content = grid;
        return border;
    }

    private List<MenuItemQuickNote> PrepareQuickNotesForSave(string menuItemId)
    {
        return _quickNotes
            .Where(note => !string.IsNullOrWhiteSpace(note.NoteText))
            .Select((note, index) =>
            {
                note.Id = string.IsNullOrWhiteSpace(note.Id) ? Guid.NewGuid().ToString() : note.Id.Trim();
                note.MenuItemId = menuItemId;
                note.NoteText = note.NoteText.Trim();
                note.DisplayOrder = index + 1;
                note.Active = true;
                note.CreatedAt = note.CreatedAt == default ? DateTime.Now : note.CreatedAt;
                note.UpdatedAt = DateTime.Now;
                return note;
            })
            .ToList();
    }

    private async void OnAddQuickNoteClicked(object sender, EventArgs e)
    {
        if (_quickNotes.Count >= 6)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Maximum Reached", "You can only add up to 6 quick notes per item.");
            return;
        }

        var dialog = new Views.QuickNoteDialog();

        DialogOverlay.Content = dialog;
        DialogOverlay.IsVisible = true;

        var result = await dialog.ShowAsync();

        DialogOverlay.IsVisible = false;
        DialogOverlay.Content = null;

        if (result != null)
        {
            result.DisplayOrder = _quickNotes.Count + 1;
            _quickNotes.Add(result);
            UpdateQuickNotesUI();
        }
    }

    private async void OnEditQuickNoteClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is MenuItemQuickNote note)
        {
            var dialog = new Views.QuickNoteDialog();

            DialogOverlay.Content = dialog;
            DialogOverlay.IsVisible = true;

            var result = await dialog.ShowAsync(note);

            DialogOverlay.IsVisible = false;
            DialogOverlay.Content = null;

            if (result != null)
            {
                UpdateQuickNotesUI();
            }
        }
    }

    private async void OnDeleteQuickNoteClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is MenuItemQuickNote note)
        {
            bool confirm = await DisplayAlert("Delete Quick Note", 
                $"Are you sure you want to delete '{note.NoteText}'?", 
                "Delete", "Cancel");
            
            if (confirm)
            {
                _quickNotes.Remove(note);
                UpdateQuickNotesUI();
            }
        }
    }

    // ===================================
    // COMPONENT LABELS HANDLERS
    // ===================================
    
    private void OnPrintComponentLabelsToggled(object sender, ToggledEventArgs e)
    {
        ComponentsListContainer.IsVisible = e.Value;
        
        if (e.Value && _componentLabels.Count == 0)
        {
            // Auto-suggest adding a component
            Dispatcher.Dispatch(async () =>
            {
                await Task.Delay(100); // Small delay for UI to update
                OnAddComponentLabelClicked(sender, EventArgs.Empty);
            });
        }
    }
    
    private async void OnAddComponentLabelClicked(object sender, EventArgs e)
    {
        var dialog = new Views.ComponentLabelDialog();
        
        // Show dialog in the overlay container
        DialogOverlay.Content = dialog;
        DialogOverlay.IsVisible = true;
        
        var result = await dialog.ShowAsync();
        
        // Hide dialog
        DialogOverlay.IsVisible = false;
        DialogOverlay.Content = null;
        
        if (!string.IsNullOrWhiteSpace(result))
        {
            var componentName = result.Trim();
            if (!_componentLabels.Contains(componentName))
            {
                _componentLabels.Add(componentName);
                UpdateComponentLabelsUI();
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Duplicate", $"'{componentName}' already exists in the list.");
            }
        }
    }
    
    private async void OnRemoveComponentLabel(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string componentLabel)
        {
            bool confirm = await DisplayAlert("Remove Component Label", 
                $"Remove '{componentLabel}' from print labels?", 
                "Remove", "Cancel");
            
            if (confirm)
            {
                _componentLabels.Remove(componentLabel);
                UpdateComponentLabelsUI();
            }
        }
    }
    
    private void UpdateComponentLabelsUI()
    {
        ComponentLabelsCollectionView.ItemsSource = null;
        ComponentLabelsCollectionView.ItemsSource = _componentLabels;
    }
    
    private void LoadComponentLabels(string? componentLabelsJson)
    {
        _componentLabels.Clear();
        
        if (string.IsNullOrWhiteSpace(componentLabelsJson))
            return;
            
        try
        {
            var labels = System.Text.Json.JsonSerializer.Deserialize<List<string>>(componentLabelsJson);
            if (labels != null)
            {
                foreach (var label in labels)
                {
                    _componentLabels.Add(label);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to parse component labels: {ex.Message}");
        }
        
        UpdateComponentLabelsUI();
        ComponentsListContainer.IsVisible = _componentLabels.Count > 0;
    }
    
    private string? GetComponentLabelsJson()
    {
        if (_componentLabels.Count == 0)
            return null;
            
        return System.Text.Json.JsonSerializer.Serialize(_componentLabels.ToList());
    }
}
