using Microsoft.Maui.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages
{
    public partial class TablePage : ContentPage
    {
        private ObservableCollection<RestaurantTable> _tables;
        private readonly RestaurantTableService _tableService;
        private readonly FloorService _floorService;
        private bool _isSubscribedToRefreshEvents;
        private bool _isLoadingTables;
        private DateTime _lastSuccessfulLoadAt = DateTime.MinValue;

        public TablePage()
        {
            InitializeComponent();
            
            // Set the page title in the TopBar
            TopBar.SetPageTitle("Table Management");
            
            _tables = new ObservableCollection<RestaurantTable>();
            TablesCollectionView.ItemsSource = _tables;
            _tableService = new RestaurantTableService();
            _floorService = new FloorService();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SubscribeToRefreshEvents();
            _ = LoadTablesAsync();
        }

        protected override void OnDisappearing()
        {
            UnsubscribeFromRefreshEvents();
            base.OnDisappearing();
        }

        private void SubscribeToRefreshEvents()
        {
            if (_isSubscribedToRefreshEvents)
            {
                return;
            }

            AppDataRefreshService.RefreshRequested += OnRefreshRequested;
            AppDataRefreshService.DataChanged += OnAppDataChanged;
            _isSubscribedToRefreshEvents = true;
        }

        private void UnsubscribeFromRefreshEvents()
        {
            if (!_isSubscribedToRefreshEvents)
            {
                return;
            }

            AppDataRefreshService.RefreshRequested -= OnRefreshRequested;
            AppDataRefreshService.DataChanged -= OnAppDataChanged;
            _isSubscribedToRefreshEvents = false;
        }

        private async void OnRefreshRequested(object? sender, EventArgs e)
        {
            await RefreshTablesIfReadyAsync();
        }

        private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (e.IsFromCurrentTerminal || (e.Kind != AppDataChangeKind.TableLayout && e.Kind != AppDataChangeKind.All))
            {
                return;
            }

            await RefreshTablesIfReadyAsync();
        }

        private async Task RefreshTablesIfReadyAsync()
        {
            if ((DateTime.UtcNow - _lastSuccessfulLoadAt).TotalMilliseconds < 600)
            {
                return;
            }

            await LoadTablesAsync();
        }

        private async Task LoadTablesAsync()
        {
            if (_isLoadingTables)
            {
                return;
            }

            try
            {
                _isLoadingTables = true;
                System.Diagnostics.Debug.WriteLine(" LoadTablesAsync START");
                SetLoading(true);

                System.Diagnostics.Debug.WriteLine("� Loading floors...");
                // Check if floors exist
                var floorsTask = _floorService.GetAllFloorsAsync();
                var tablesTask = _tableService.GetAllTablesAsync();
                await Task.WhenAll(floorsTask, tablesTask);
                var floors = floorsTask.Result;
                System.Diagnostics.Debug.WriteLine($" Loaded {floors.Count} floors");
                
                bool hasFloors = floors.Count > 0;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    NoFloorsWarning.IsVisible = !hasFloors;
                    AddTableButton.IsEnabled = hasFloors;
                });

                System.Diagnostics.Debug.WriteLine(" Loading tables...");
                // Load tables
                var tables = tablesTask.Result;
                System.Diagnostics.Debug.WriteLine($" Loaded {tables.Count} tables from database");
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _tables.Clear();
                    foreach (var table in tables)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Adding table: {table.TableNumber} on {table.FloorName}");
                        _tables.Add(table);
                    }
                });

                System.Diagnostics.Debug.WriteLine($" LoadTablesAsync COMPLETE - {_tables.Count} tables displayed in UI");
                _lastSuccessfulLoadAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading tables: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
                
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Error", $"Could not load tables: {ex.Message}", NotificationType.Error, 4000);
                });
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine(" LoadTablesAsync FINALLY block");
                _isLoadingTables = false;
                SetLoading(false);
            }
        }

        private void SetLoading(bool isLoading)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsRunning = isLoading;
                LoadingIndicator.IsVisible = isLoading;
                AddTableButton.IsEnabled = !isLoading && !NoFloorsWarning.IsVisible;
            });
        }

        private async void OnGoToFloorManagementClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(" Navigating to Floor Management...");
                await Shell.Current.GoToAsync("//floor");
                System.Diagnostics.Debug.WriteLine(" Navigation successful");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Navigation error: {ex.Message}");
                await ToastNotification.ShowAsync("Navigation Error", $"Could not navigate to Floor Management: {ex.Message}", NotificationType.Error, 4000);
            }
        }

        private async void OnAddTableClicked(object sender, EventArgs e)
        {
            try
            {
                // Get all floors
                var floors = await _floorService.GetAllFloorsAsync();
                
                if (floors.Count == 0)
                {
                    await ToastNotification.ShowAsync("No Floors", "Please create a floor first", NotificationType.Warning, 3000);
                    return;
                }

                // Show elegant custom dialog
                AddTableDialogOverlay.SetFloors(floors);
                AddTableDialogOverlay.IsVisible = true;
                
                var result = await AddTableDialogOverlay.ShowAsync();
                
                AddTableDialogOverlay.IsVisible = false;
                
                if (!result.success || string.IsNullOrWhiteSpace(result.tableName))
                    return;

                // Create table with default values: capacity = 4, shape = Square
                SetLoading(true);

                var createResult = await _tableService.CreateTableAsync(result.tableName, result.floorId, 4, TableShape.Square, result.tableDesignIcon);

                if (createResult.success)
                {
                    await ToastNotification.ShowAsync("Success", createResult.message, NotificationType.Success, 2000);
                    AppDataRefreshService.RequestRefresh();
                }
                else
                {
                    await ToastNotification.ShowAsync("Error", createResult.message, NotificationType.Error, 4000);
                }
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", $"Could not add table: {ex.Message}", NotificationType.Error, 4000);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void OnEditTableClicked(object sender, EventArgs e)
        {
            try
            {
                var button = sender as Button;
                var table = button?.CommandParameter as RestaurantTable;

                if (table == null)
                    return;

                // Load floors for the dialog
                var floors = await _floorService.GetAllFloorsAsync();
                
                // Show custom edit dialog
                EditTableDialogOverlay.SetFloors(floors);
                EditTableDialogOverlay.SetTableData(table.Id, table.TableNumber, table.FloorId, table.TableDesignIcon);
                
                var result = await EditTableDialogOverlay.ShowAsync();

                if (!result.success)
                    return;

                // Update table - keep existing capacity, shape, and status
                SetLoading(true);

                var updateResult = await _tableService.UpdateTableAsync(
                    table.Id, 
                    result.tableName, 
                    result.floorId, 
                    table.Capacity, 
                    table.Shape, 
                    table.Status,
                    result.tableDesignIcon);

                if (updateResult.success)
                {
                    await ToastNotification.ShowAsync("Success", updateResult.message, NotificationType.Success, 2000);
                    AppDataRefreshService.RequestRefresh();
                }
                else
                {
                    await ToastNotification.ShowAsync("Error", updateResult.message, NotificationType.Error, 4000);
                }
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", $"Could not edit table: {ex.Message}", NotificationType.Error, 4000);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void OnDeleteTableClicked(object sender, EventArgs e)
        {
            try
            {
                var button = sender as Button;
                var table = button?.CommandParameter as RestaurantTable;

                if (table == null)
                    return;

                bool confirm = await DisplayAlert(
                    "Delete Table",
                    $"Are you sure you want to delete '{table.TableNumber}' on {table.FloorName}?",
                    "Delete",
                    "Cancel");

                if (confirm)
                {
                    SetLoading(true);

                    var result = await _tableService.DeleteTableAsync(table.Id);

                    if (result.success)
                    {
                        await ToastNotification.ShowAsync("Success", result.message, NotificationType.Success, 2000);
                        AppDataRefreshService.RequestRefresh();
                    }
                    else
                    {
                        await ToastNotification.ShowAsync("Error", result.message, NotificationType.Error, 4000);
                    }
                }
            }
            catch (Exception ex)
            {
                await ToastNotification.ShowAsync("Error", $"Could not delete table: {ex.Message}", NotificationType.Error, 4000);
            }
            finally
            {
                SetLoading(false);
            }
        }

        // Show custom styled prompt dialog
        private async Task<string?> ShowStyledPromptAsync(string title, string message, string placeholder, Keyboard? keyboard = null)
        {
            DialogOverlay.SetDialog(title, message, placeholder, keyboard);
            DialogOverlay.IsVisible = true;
            
            var result = await DialogOverlay.ShowAsync();
            
            DialogOverlay.IsVisible = false;
            return result;
        }
    }
}
