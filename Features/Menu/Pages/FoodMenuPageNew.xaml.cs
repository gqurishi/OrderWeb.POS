using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class FoodMenuPageNew : ContentPage
    {
        private readonly MenuItemService _menuItemService;
        private readonly MenuCategoryService _categoryService;
        private readonly CommentNoteService _noteService;
        private ObservableCollection<FoodMenuItem> _items;
        private ObservableCollection<MenuCategory> _categories;
        private ObservableCollection<MenuCategory> _subCategories;
        private ObservableCollection<PredefinedNote> _notes;
        private ObservableCollection<PredefinedNote> _allNotes;
        private string _selectedCategory = "All Categories";
        private string _selectedPriority = "All Priorities";
        private string _activeTab = "Items";
        private bool _hasLoadedItems;
        private bool _hasLoadedCategories;
        private bool _hasLoadedSubCategories;
        private bool _hasLoadedNotes;

        public FoodMenuPageNew()
        {
            InitializeComponent();
            
            _menuItemService = new MenuItemService();
            _categoryService = new MenuCategoryService();
            _noteService = new CommentNoteService();
            _items = new ObservableCollection<FoodMenuItem>();
            _categories = new ObservableCollection<MenuCategory>();
            _subCategories = new ObservableCollection<MenuCategory>();
            _notes = new ObservableCollection<PredefinedNote>();
            _allNotes = new ObservableCollection<PredefinedNote>();

            ItemsCollectionView.ItemsSource = _items;
            CategoriesCollectionView.ItemsSource = _categories;
            SubCategoriesCollectionView.ItemsSource = _subCategories;
            NotesCollectionView.ItemsSource = _notes;
            
            // Subscribe to NotificationService events
            NotificationService.Instance.NotificationRequested += OnNotificationRequested;
            
            if (TopBar != null)
            {
                TopBar.SetPageTitle("Food Menu");
            }
        }
        
        private async void OnNotificationRequested(object? sender, NotificationEventArgs e)
        {
            await ToastNotification.ShowAsync(e.Title, e.Message, e.Type, e.DurationMs);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            System.Diagnostics.Debug.WriteLine(" NEW Food Menu Page Loading");
            
            await EnsureActiveTabLoadedAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(" Loading items...");
                // Commented out to start with empty list
                // var items = await _menuItemService.GetAllItemsAsync();
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _items.Clear();
                    // foreach (var item in items)
                    // {
                    //     _items.Add(item);
                    // }
                    ItemsCollectionView.ItemsSource = _items;
                    System.Diagnostics.Debug.WriteLine($" Loaded {_items.Count} items");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading data: {ex.Message}");
            }
        }

        private async Task LoadItemsAsync()
        {
            try
            {
                var items = await _menuItemService.GetAllItemsAsync();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _items.Clear();
                    foreach (var item in items)
                    {
                        _items.Add(item);
                    }
                    _hasLoadedItems = true;
                    System.Diagnostics.Debug.WriteLine($" Loaded {_items.Count} items");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading items: {ex.Message}");
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var allCategories = await _categoryService.GetAllCategoriesAsync();
                var topLevelCategories = allCategories
                    .Where(c => c.ParentId == null)
                    .OrderBy(c => c.DisplayOrder)
                    .ToList();
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _categories.Clear();
                    foreach (var category in topLevelCategories)
                    {
                        _categories.Add(category);
                    }
                    _hasLoadedCategories = true;
                    System.Diagnostics.Debug.WriteLine($" Loaded {_categories.Count} categories");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading categories: {ex.Message}");
            }
        }

        private async Task LoadSubCategoriesAsync()
        {
            try
            {
                var allCategories = await _categoryService.GetAllCategoriesAsync();
                var subCategoriesOnly = allCategories
                    .Where(c => c.ParentId != null)
                    .OrderBy(c => c.DisplayOrder)
                    .ToList();
                
                // Populate ParentCategoryName for each sub-category
                foreach (var subCategory in subCategoriesOnly)
                {
                    var parent = allCategories.FirstOrDefault(c => c.Id == subCategory.ParentId);
                    if (parent != null)
                    {
                        subCategory.ParentCategoryName = parent.Name;
                        System.Diagnostics.Debug.WriteLine($" Sub-category '{subCategory.Name}' → Parent: '{parent.Name}'");
                    }
                }
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _subCategories.Clear();
                    foreach (var subCategory in subCategoriesOnly)
                    {
                        _subCategories.Add(subCategory);
                    }
                    _hasLoadedSubCategories = true;
                    System.Diagnostics.Debug.WriteLine($" Loaded {_subCategories.Count} sub-categories");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading sub-categories: {ex.Message}");
            }
        }

        private async Task LoadNotesAsync()
        {
            try
            {
                var notes = await _noteService.GetAllNotesAsync();
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _notes.Clear();
                    _allNotes = new ObservableCollection<PredefinedNote>(notes);
                    
                    foreach (var note in notes)
                    {
                        _notes.Add(note);
                    }
                    
                    UpdateNoteStats();
                    _hasLoadedNotes = true;
                    System.Diagnostics.Debug.WriteLine($" Loaded {_notes.Count} notes");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading notes: {ex.Message}");
            }
        }

        private void UpdateNoteStats()
        {
            var urgentCount = _notes.Count(n => n.Priority?.ToLower() == "urgent");
            var highCount = _notes.Count(n => n.Priority?.ToLower() == "high");
            var normalCount = _notes.Count(n => n.Priority?.ToLower() == "normal");
            var lowCount = _notes.Count(n => n.Priority?.ToLower() == "low");

            UrgentCountLabel.Text = urgentCount.ToString();
            HighCountLabel.Text = highCount.ToString();
            NormalCountLabel.Text = normalCount.ToString();
            LowCountLabel.Text = lowCount.ToString();
        }

        private void FilterNotes()
        {
            var filteredNotes = _allNotes.AsEnumerable();

            if (_selectedCategory != "All Categories")
            {
                filteredNotes = filteredNotes.Where(n => n.Category == _selectedCategory);
            }

            if (_selectedPriority != "All Priorities")
            {
                filteredNotes = filteredNotes.Where(n => 
                    string.Equals(n.Priority, _selectedPriority, StringComparison.OrdinalIgnoreCase));
            }

            // Sort by priority (urgent > high > normal > low)
            var priorityOrder = new Dictionary<string, int>
            {
                { "urgent", 1 },
                { "high", 2 },
                { "normal", 3 },
                { "low", 4 }
            };

            filteredNotes = filteredNotes.OrderBy(n => 
            {
                var priority = n.Priority?.ToLower() ?? "";
                return priorityOrder.ContainsKey(priority) ? priorityOrder[priority] : 5;
            });

            _notes.Clear();
            foreach (var note in filteredNotes)
            {
                _notes.Add(note);
            }

            UpdateNoteStats();
        }

        private void OnCategoryFilterChanged(object sender, EventArgs e)
        {
            if (sender is Picker picker && picker.SelectedIndex >= 0)
            {
                _selectedCategory = picker.Items[picker.SelectedIndex];
                FilterNotes();
            }
        }

        private void OnPriorityFilterChanged(object sender, EventArgs e)
        {
            if (sender is Picker picker && picker.SelectedIndex >= 0)
            {
                _selectedPriority = picker.Items[picker.SelectedIndex];
                FilterNotes();
            }
        }

        private void OnClearFiltersClicked(object sender, EventArgs e)
        {
            CategoryFilterPicker.SelectedIndex = 0;
            PriorityFilterPicker.SelectedIndex = 0;
            _selectedCategory = "All Categories";
            _selectedPriority = "All Priorities";
            FilterNotes();
        }

        // Tab Button Handlers
        private void OnItemsButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" ITEMS BUTTON CLICKED!");
            ActivateTab("Items");
        }

        private void OnCategoriesButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" CATEGORIES BUTTON CLICKED!");
            ActivateTab("Categories");
        }

        private void OnSubCategoriesButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" SUB-CATEGORIES BUTTON CLICKED!");
            ActivateTab("SubCategories");
        }

        private void OnNotesButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" NOTES BUTTON CLICKED!");
            ActivateTab("Notes");
        }

        private void OnMealDealsButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" MEAL DEALS BUTTON CLICKED!");
            ActivateTab("MealDeals");
        }

        private void ActivateTab(string tabName)
        {
            System.Diagnostics.Debug.WriteLine($" Activating tab: {tabName}");
            _activeTab = tabName;

            // Hide all content
            ItemsContent.IsVisible = false;
            CategoriesContent.IsVisible = false;
            SubCategoriesContent.IsVisible = false;
            NotesContent.IsVisible = false;
            MealDealsContent.IsVisible = false;

            // Reset all buttons
            ResetAllButtons();

            // Show selected content and activate button
            switch (tabName)
            {
                case "Items":
                    ItemsContent.IsVisible = true;
                    SetButtonActive(ItemsButton);
                    _ = EnsureActiveTabLoadedAsync();
                    break;
                case "Categories":
                    CategoriesContent.IsVisible = true;
                    SetButtonActive(CategoriesButton);
                    _ = EnsureActiveTabLoadedAsync();
                    break;
                case "SubCategories":
                    SubCategoriesContent.IsVisible = true;
                    SetButtonActive(SubCategoriesButton);
                    _ = EnsureActiveTabLoadedAsync();
                    break;
                case "Notes":
                    NotesContent.IsVisible = true;
                    SetButtonActive(NotesButton);
                    _ = EnsureActiveTabLoadedAsync();
                    break;
                case "MealDeals":
                    MealDealsContent.IsVisible = true;
                    SetButtonActive(MealDealsButton);
                    break;
            }

            System.Diagnostics.Debug.WriteLine($" Tab activated: {tabName}");
        }

        private Task EnsureActiveTabLoadedAsync()
        {
            return _activeTab switch
            {
                "Items" when !_hasLoadedItems => LoadItemsAsync(),
                "Categories" when !_hasLoadedCategories => LoadCategoriesAsync(),
                "SubCategories" when !_hasLoadedSubCategories => LoadSubCategoriesAsync(),
                "Notes" when !_hasLoadedNotes => LoadNotesAsync(),
                _ => Task.CompletedTask
            };
        }

        private void ResetAllButtons()
        {
            SetButtonInactive(ItemsButton);
            SetButtonInactive(CategoriesButton);
            SetButtonInactive(SubCategoriesButton);
            SetButtonInactive(NotesButton);
            SetButtonInactive(MealDealsButton);
        }

        private void SetButtonActive(Button button)
        {
            button.BackgroundColor = Color.FromArgb("#6366F1");
            button.TextColor = Colors.White;
            button.FontAttributes = FontAttributes.Bold;
            button.BorderWidth = 0;
        }

        private void SetButtonInactive(Button button)
        {
            button.BackgroundColor = Colors.Transparent;
            button.TextColor = Color.FromArgb("#64748B");
            button.FontAttributes = FontAttributes.None;
            button.BorderColor = Color.FromArgb("#CBD5E1");
            button.BorderWidth = 1;
        }

        // Item Actions
        private async void OnAddItemClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Add Item clicked");
            var result = await AddItemDialog.ShowAsync();
            if (result)
            {
                await LoadItemsAsync();
            }
        }

        private async void OnEditItemClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Edit Item clicked");
            if (sender is Button button && button.CommandParameter is FoodMenuItem item)
            {
                var result = await AddItemDialog.ShowAsync(item);
                if (result)
                {
                    await LoadItemsAsync();
                }
            }
        }

        private async void OnDeleteItemClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Delete Item clicked");
            if (sender is Button button && button.CommandParameter is FoodMenuItem item)
            {
                bool confirm = await DisplayAlert("Confirm Delete", 
                    $"Are you sure you want to delete '{item.Name}'?", 
                    "Yes", "No");
                
                if (confirm)
                {
                    try
                    {
                        await _menuItemService.DeleteItemAsync(item.Id);
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Item deleted successfully");
                        await LoadItemsAsync();
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete item: {ex.Message}");
                    }
                }
            }
        }

        // Category Actions
        private async void OnAddCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Add Category clicked");
            var result = await AddCategoryDialogView.ShowAsync();
            if (result)
            {
                await LoadCategoriesAsync();
            }
        }

        private async void OnEditCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Edit Category clicked");
            if (sender is Button button && button.CommandParameter is MenuCategory category)
            {
                var result = await AddCategoryDialogView.ShowAsync(category);
                if (result)
                {
                    await LoadCategoriesAsync();
                }
            }
        }

        private async void OnDeleteCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Delete Category clicked");
            if (sender is Button button && button.CommandParameter is MenuCategory category)
            {
                bool confirm = await DisplayAlert("Confirm Delete", 
                    $"Are you sure you want to delete '{category.Name}'?", 
                    "Yes", "No");
                
                if (confirm)
                {
                    try
                    {
                        await _categoryService.DeleteCategoryAsync(category.Id);
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Category deleted successfully");
                        await LoadCategoriesAsync();
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete category: {ex.Message}");
                    }
                }
            }
        }

        // Sub-Category Actions
        private async void OnAddSubCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Add Sub-Category clicked");
            var result = await AddSubCategoryDialogView.ShowAsync();
            if (result)
            {
                await LoadSubCategoriesAsync();
            }
        }

        private async void OnEditSubCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Edit Sub-Category clicked");
            if (sender is Button button && button.CommandParameter is MenuCategory subCategory)
            {
                var result = await AddSubCategoryDialogView.ShowAsync(subCategory);
                if (result)
                {
                    await LoadSubCategoriesAsync();
                }
            }
        }

        private async void OnDeleteSubCategoryClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Delete Sub-Category clicked");
            if (sender is Button button && button.CommandParameter is MenuCategory subCategory)
            {
                bool confirm = await DisplayAlert("Confirm Delete", 
                    $"Are you sure you want to delete '{subCategory.Name}'?", 
                    "Yes", "No");
                
                if (confirm)
                {
                    try
                    {
                        await _categoryService.DeleteCategoryAsync(subCategory.Id);
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Sub-category deleted successfully");
                        await LoadSubCategoriesAsync();
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete sub-category: {ex.Message}");
                    }
                }
            }
        }

        // Note Actions
        private async void OnAddNoteClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Add Note clicked");
            var result = await AddNoteDialogView.ShowAsync();
            if (result)
            {
                await LoadNotesAsync();
            }
        }

        private async void OnEditNoteClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Edit Note clicked");
            if (sender is Button button && button.CommandParameter is PredefinedNote note)
            {
                var result = await AddNoteDialogView.ShowAsync(note);
                if (result)
                {
                    await LoadNotesAsync();
                }
            }
        }

        private async void OnDeleteNoteClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Delete Note clicked");
            if (sender is Button button && button.CommandParameter is PredefinedNote note)
            {
                bool confirm = await DisplayAlert("Confirm Delete", 
                    $"Are you sure you want to delete '{note.NoteText}'?", 
                    "Yes", "No");
                
                if (confirm)
                {
                    try
                    {
                        await _noteService.DeleteNoteAsync(note.Id);
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Note deleted successfully");
                        await LoadNotesAsync();
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete note: {ex.Message}");
                    }
                }
            }
        }

        private async void OnToggleNoteActiveClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Toggle Note Active clicked");
            if (sender is Button button && button.CommandParameter is PredefinedNote note)
            {
                try
                {
                    note.Active = !note.Active;
                    note.UpdatedAt = DateTime.Now;
                    await _noteService.UpdateNoteAsync(note);
                    
                    // Refresh the display
                    var index = _notes.IndexOf(note);
                    if (index >= 0)
                    {
                        _notes[index] = note;
                    }
                    
                    await DisplayAlert("Success", 
                        $"Note {(note.Active ? "activated" : "deactivated")} successfully", 
                        "OK");
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to toggle note status: {ex.Message}");
                }
            }
        }

        private async void OnDuplicateNoteClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Duplicate Note clicked");
            if (sender is Button button && button.CommandParameter is PredefinedNote note)
            {
                try
                {
                    var duplicatedNote = new PredefinedNote
                    {
                        Id = Guid.NewGuid().ToString(),
                        NoteText = note.NoteText + " (Copy)",
                        Category = note.Category,
                        Priority = note.Priority,
                        DisplayOrder = note.DisplayOrder + 1,
                        Active = note.Active,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _noteService.CreateNoteAsync(duplicatedNote);
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Note duplicated successfully");
                    await LoadNotesAsync();
                }
                catch (Exception ex)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to duplicate note: {ex.Message}");
                }
            }
        }

        // Category Reordering
        private async void OnMoveCategoryUpClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is MenuCategory category)
            {
                var currentIndex = _categories.IndexOf(category);
                if (currentIndex > 0)
                {
                    try
                    {
                        // Swap with previous item
                        var previousCategory = _categories[currentIndex - 1];
                        
                        // Update display orders
                        var tempOrder = category.DisplayOrder;
                        category.DisplayOrder = previousCategory.DisplayOrder;
                        previousCategory.DisplayOrder = tempOrder;
                        
                        // Save to database
                        await _categoryService.UpdateCategoryAsync(category);
                        await _categoryService.UpdateCategoryAsync(previousCategory);
                        
                        // Update UI
                        _categories.Move(currentIndex, currentIndex - 1);
                        
                        System.Diagnostics.Debug.WriteLine($"⬆ Moved '{category.Name}' up");
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to move category: {ex.Message}");
                    }
                }
            }
        }

        private async void OnMoveCategoryDownClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is MenuCategory category)
            {
                var currentIndex = _categories.IndexOf(category);
                if (currentIndex < _categories.Count - 1)
                {
                    try
                    {
                        // Swap with next item
                        var nextCategory = _categories[currentIndex + 1];
                        
                        // Update display orders
                        var tempOrder = category.DisplayOrder;
                        category.DisplayOrder = nextCategory.DisplayOrder;
                        nextCategory.DisplayOrder = tempOrder;
                        
                        // Save to database
                        await _categoryService.UpdateCategoryAsync(category);
                        await _categoryService.UpdateCategoryAsync(nextCategory);
                        
                        // Update UI
                        _categories.Move(currentIndex, currentIndex + 1);
                        
                        System.Diagnostics.Debug.WriteLine($"⬇ Moved '{category.Name}' down");
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to move category: {ex.Message}");
                    }
                }
            }
        }

        // Sub-Category Reordering
        private async void OnMoveSubCategoryUpClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is MenuCategory subCategory)
            {
                var currentIndex = _subCategories.IndexOf(subCategory);
                if (currentIndex > 0)
                {
                    try
                    {
                        // Swap with previous item
                        var previousSubCategory = _subCategories[currentIndex - 1];
                        
                        // Update display orders
                        var tempOrder = subCategory.DisplayOrder;
                        subCategory.DisplayOrder = previousSubCategory.DisplayOrder;
                        previousSubCategory.DisplayOrder = tempOrder;
                        
                        // Save to database
                        await _categoryService.UpdateCategoryAsync(subCategory);
                        await _categoryService.UpdateCategoryAsync(previousSubCategory);
                        
                        // Update UI
                        _subCategories.Move(currentIndex, currentIndex - 1);
                        
                        System.Diagnostics.Debug.WriteLine($"⬆ Moved '{subCategory.Name}' up");
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to move sub-category: {ex.Message}");
                    }
                }
            }
        }

        private async void OnMoveSubCategoryDownClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is MenuCategory subCategory)
            {
                var currentIndex = _subCategories.IndexOf(subCategory);
                if (currentIndex < _subCategories.Count - 1)
                {
                    try
                    {
                        // Swap with next item
                        var nextSubCategory = _subCategories[currentIndex + 1];
                        
                        // Update display orders
                        var tempOrder = subCategory.DisplayOrder;
                        subCategory.DisplayOrder = nextSubCategory.DisplayOrder;
                        nextSubCategory.DisplayOrder = tempOrder;
                        
                        // Save to database
                        await _categoryService.UpdateCategoryAsync(subCategory);
                        await _categoryService.UpdateCategoryAsync(nextSubCategory);
                        
                        // Update UI
                        _subCategories.Move(currentIndex, currentIndex + 1);
                        
                        System.Diagnostics.Debug.WriteLine($"⬇ Moved '{subCategory.Name}' down");
                    }
                    catch (Exception ex)
                    {
                        await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to move sub-category: {ex.Message}");
                    }
                }
            }
        }

        // Meal Deal Actions
        private void OnAddMealDealClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Add Meal Deal clicked");
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Add Meal Deal", "Add meal deal functionality coming soon");
        }

        private void OnEditMealDealClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Edit Meal Deal clicked");
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Edit Meal Deal", "Edit meal deal functionality coming soon");
        }

        private void OnDeleteMealDealClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(" Delete Meal Deal clicked");
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Delete Meal Deal", "Delete meal deal functionality coming soon");
        }
    }
}
