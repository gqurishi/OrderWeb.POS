using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Views;

public partial class TableTransferDialog : ContentView
{
    private TaskCompletionSource<RestaurantTable?>? _taskCompletionSource;
    private string _currentTableNumber = "";
    private RestaurantTable? _selectedTable;
    private Button? _selectedButton;
    private readonly RestaurantTableService _tableService;
    
    public TableTransferDialog()
    {
        InitializeComponent();
        _tableService = new RestaurantTableService();
    }
    
    public void SetCurrentTable(string tableNumber)
    {
        _currentTableNumber = tableNumber;
        CurrentTableLabel.Text = $"Table {tableNumber}";
    }
    
    public async Task<RestaurantTable?> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<RestaurantTable?>();
        
        // Load available tables
        await LoadAvailableTables();
        
        // Add to page
        if (Application.Current?.MainPage is Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage contentPage)
            {
                AddToPage(contentPage);
            }
            else if (page is ContentPage cp)
            {
                AddToPage(cp);
            }
        }
        
        return await _taskCompletionSource.Task;
    }
    
    private async Task LoadAvailableTables()
    {
        try
        {
            var allTables = await _tableService.GetAllTablesAsync();
            
            // Filter only available (empty) tables, excluding current table
            var availableTables = allTables
                .Where(t => t.Status == TableStatus.Available && t.TableNumber != _currentTableNumber)
                .OrderBy(t => t.FloorId)
                .ThenBy(t => t.TableNumber)
                .ToList();
            
            TablesContainer.Children.Clear();
            
            if (availableTables.Count == 0)
            {
                NoTablesMessage.IsVisible = true;
                return;
            }
            
            NoTablesMessage.IsVisible = false;
            
            foreach (var table in availableTables)
            {
                var tableButton = new Button
                {
                    Text = $"Table {table.TableNumber}",
                    BackgroundColor = Color.FromArgb("#E8F5E8"), // Light green for available
                    TextColor = Color.FromArgb("#2E7D32"),
                    FontSize = 14,
                    HeightRequest = 50,
                    WidthRequest = 120,
                    CornerRadius = 8,
                    Margin = new Thickness(5),
                    CommandParameter = table
                };
                
                tableButton.Clicked += OnTableSelected;
                TablesContainer.Children.Add(tableButton);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading tables: {ex.Message}");
            NoTablesMessage.Text = "Error loading tables";
            NoTablesMessage.IsVisible = true;
        }
    }
    
    private void OnTableSelected(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is RestaurantTable table)
        {
            // Deselect previous
            if (_selectedButton != null)
            {
                _selectedButton.BackgroundColor = Color.FromArgb("#E8F5E8");
                _selectedButton.TextColor = Color.FromArgb("#2E7D32");
            }
            
            // Select new
            _selectedButton = button;
            button.BackgroundColor = Color.FromArgb("#4CAF50");
            button.TextColor = Colors.White;
            
            _selectedTable = table;
            
            // Show selected table
            SelectedTableFrame.IsVisible = true;
            SelectedTableLabel.Text = $"Table {table.TableNumber}";
            
            // Enable transfer button
            TransferButton.IsEnabled = true;
            TransferButton.BackgroundColor = Color.FromArgb("#4CAF50");
        }
    }
    
    private void AddToPage(ContentPage page)
    {
        if (page.Content is Grid grid)
        {
            // Span all rows and columns to cover full page
            if (grid.RowDefinitions.Count > 0)
                Grid.SetRowSpan(this, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
            
            grid.Children.Add(this);
        }
        else if (page.Content is Layout layout)
        {
            var newGrid = new Grid();
            var existingContent = page.Content;
            page.Content = null;
            newGrid.Children.Add(existingContent);
            newGrid.Children.Add(this);
            page.Content = newGrid;
        }
    }
    
    private void CloseDialog()
    {
        if (Parent is Grid grid)
        {
            grid.Children.Remove(this);
        }
    }
    
    private void OnTransferClicked(object sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(_selectedTable);
        CloseDialog();
    }
    
    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }
}
