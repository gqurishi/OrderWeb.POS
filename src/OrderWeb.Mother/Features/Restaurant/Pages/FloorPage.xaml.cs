using Microsoft.Maui.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages
{
    public partial class FloorPage : ContentPage
    {
        private ObservableCollection<Floor> _floors;
        private readonly FloorService _floorService;
        private bool _isSubscribedToRefreshEvents;
        private bool _isLoadingFloors;
        private bool _isFloorOperationInProgress;
        private DateTime _lastSuccessfulLoadAt = DateTime.MinValue;

        public FloorPage()
        {
            InitializeComponent();
            
            // Set the page title in the TopBar
            TopBar.SetPageTitle("Floor Management");
            
            _floors = new ObservableCollection<Floor>();
            FloorsCollectionView.ItemsSource = _floors;
            _floorService = ServiceHelper.GetService<FloorService>() ?? new FloorService();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SubscribeToRefreshEvents();
            _ = LoadFloorsAsync();
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

            AppDataRefreshService.DataChanged += OnAppDataChanged;
            _isSubscribedToRefreshEvents = true;
        }

        private void UnsubscribeFromRefreshEvents()
        {
            if (!_isSubscribedToRefreshEvents)
            {
                return;
            }

            AppDataRefreshService.DataChanged -= OnAppDataChanged;
            _isSubscribedToRefreshEvents = false;
        }

        private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (e.IsFromCurrentTerminal || !e.HasKind(AppDataChangeKind.TableLayout))
            {
                return;
            }

            await RefreshFloorsIfReadyAsync();
        }

        private async Task RefreshFloorsIfReadyAsync()
        {
            if (_isFloorOperationInProgress)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastSuccessfulLoadAt).TotalMilliseconds < 600)
            {
                return;
            }

            await LoadFloorsAsync();
        }

        private async Task LoadFloorsAsync()
        {
            if (_isLoadingFloors)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine(" LoadFloorsAsync START");
            
            try
            {
                _isLoadingFloors = true;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = true;
                    AddFloorButton.IsEnabled = false;
                });
                
                System.Diagnostics.Debug.WriteLine(" Calling FloorService.GetAllFloorsAsync()...");
                var floors = await _floorService.GetAllFloorsAsync();
                System.Diagnostics.Debug.WriteLine($" Received {floors.Count} floors from service");
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _floors = new ObservableCollection<Floor>(floors);
                    FloorsCollectionView.ItemsSource = _floors;
                });
                
                System.Diagnostics.Debug.WriteLine($" LoadFloorsAsync COMPLETE");
                _lastSuccessfulLoadAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error in LoadFloorsAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
                
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Error", $"Could not load floors: {ex.Message}", NotificationType.Error, 4000);
                });
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine(" LoadFloorsAsync FINALLY - Stopping loading indicator");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = false;
                    AddFloorButton.IsEnabled = true;
                });
                _isLoadingFloors = false;
            }
        }

        private async void OnAddFloorClicked(object sender, EventArgs e)
        {
            if (_isFloorOperationInProgress)
            {
                return;
            }

            try
            {
                _isFloorOperationInProgress = true;
                System.Diagnostics.Debug.WriteLine(" Add Floor button clicked");
                
                // Show custom dialog
                var result = await AddFloorDialogOverlay.ShowAsync();
                System.Diagnostics.Debug.WriteLine($" Dialog result: success={result.success}, name={result.floorName}");

                if (!result.success || string.IsNullOrWhiteSpace(result.floorName))
                {
                    System.Diagnostics.Debug.WriteLine(" User cancelled or empty name");
                    return; // User cancelled
                }

                var floorName = result.floorName.Trim();
                System.Diagnostics.Debug.WriteLine($" Creating floor: {floorName}");
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = true;
                    AddFloorButton.IsEnabled = false;
                });

                var createResult = await _floorService.CreateFloorAsync(result.floorName, "");
                System.Diagnostics.Debug.WriteLine($" Create result: success={createResult.success}, message={createResult.message}");

                if (createResult.success)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (createResult.floorId.HasValue)
                        {
                            UpsertFloorInList(new Floor
                            {
                                Id = createResult.floorId.Value,
                                Name = floorName,
                                Description = string.Empty,
                                IsActive = true,
                                TableCount = 0,
                                CreatedDate = DateTime.Now,
                                UpdatedDate = DateTime.Now
                            });
                        }
                    });

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ToastNotification.ShowAsync("Success", createResult.message, NotificationType.Success, 2000);
                    });
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ToastNotification.ShowAsync("Error", createResult.message, NotificationType.Error, 4000);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error in OnAddFloorClicked: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Error", $"Could not add floor: {ex.Message}", NotificationType.Error, 4000);
                });
            }
            finally
            {
                _isFloorOperationInProgress = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = false;
                    AddFloorButton.IsEnabled = true;
                });
            }
        }

        private async void OnEditFloorClicked(object sender, EventArgs e)
        {
            if (_isFloorOperationInProgress)
            {
                return;
            }

            try
            {
                _isFloorOperationInProgress = true;
                var button = sender as Button;
                var floor = button?.CommandParameter as Floor;

                if (floor == null)
                    return;

                System.Diagnostics.Debug.WriteLine($" Edit floor clicked: {floor.Name} (ID: {floor.Id})");

                // Show custom dialog with current floor name
                EditFloorDialogOverlay.SetFloorName(floor.Name);
                var result = await EditFloorDialogOverlay.ShowAsync();

                if (!result.success || string.IsNullOrWhiteSpace(result.floorName))
                {
                    System.Diagnostics.Debug.WriteLine(" User cancelled edit");
                    return; // User cancelled
                }

                var floorName = result.floorName.Trim();
                System.Diagnostics.Debug.WriteLine($" Updating floor to: {floorName}");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = true;
                    AddFloorButton.IsEnabled = false;
                });

                var updateResult = await _floorService.UpdateFloorAsync(floor.Id, floorName, "");
                System.Diagnostics.Debug.WriteLine($" Update result: success={updateResult.success}");

                if (updateResult.success)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        UpsertFloorInList(new Floor
                        {
                            Id = floor.Id,
                            Name = floorName,
                            Description = floor.Description,
                            BackgroundImage = floor.BackgroundImage,
                            IsActive = floor.IsActive,
                            TableCount = floor.TableCount,
                            CreatedDate = floor.CreatedDate,
                            UpdatedDate = DateTime.Now
                        });
                    });

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ToastNotification.ShowAsync("Success", updateResult.message, NotificationType.Success, 2000);
                    });
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ToastNotification.ShowAsync("Error", updateResult.message, NotificationType.Error, 4000);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error editing floor: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Error", $"Could not edit floor: {ex.Message}", NotificationType.Error, 4000);
                });
            }
            finally
            {
                _isFloorOperationInProgress = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = false;
                    AddFloorButton.IsEnabled = true;
                });
            }
        }

        private async void OnDeleteFloorClicked(object sender, EventArgs e)
        {
            if (_isFloorOperationInProgress)
            {
                return;
            }

            try
            {
                _isFloorOperationInProgress = true;
                var button = sender as Button;
                var floor = button?.CommandParameter as Floor;

                if (floor == null)
                    return;

                System.Diagnostics.Debug.WriteLine($" Delete floor clicked: {floor.Name} (ID: {floor.Id})");

                // Check if floor has tables
                var tableCount = await _floorService.GetTableCountForFloorAsync(floor.Id);
                System.Diagnostics.Debug.WriteLine($" Floor has {tableCount} tables");

                string message;
                string deleteButton;

                if (tableCount > 0)
                {
                    message = $" WARNING\n\n" +
                             $"Floor: {floor.Name}\n" +
                             $"Tables: {tableCount} table(s)\n\n" +
                             $"Deleting this floor will also delete all {tableCount} table(s) on this floor.\n\n" +
                             $"This action cannot be undone. Continue?";
                    deleteButton = "Delete All";
                }
                else
                {
                    message = $"Are you sure you want to delete '{floor.Name}'?";
                    deleteButton = "Delete";
                }

                bool confirm = await DisplayAlert(
                    "Delete Floor",
                    message,
                    deleteButton,
                    "Cancel");

                if (confirm)
                {
                    System.Diagnostics.Debug.WriteLine($" User confirmed deletion of {floor.Name}");

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        LoadingIndicator.IsLoading = true;
                        AddFloorButton.IsEnabled = false;
                    });

                    var result = await _floorService.DeleteFloorAsync(floor.Id);
                    System.Diagnostics.Debug.WriteLine($" Delete result: success={result.success}");

                    if (result.success)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            RemoveFloorFromList(floor.Id);
                        });

                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await ToastNotification.ShowAsync("Success", result.message, NotificationType.Success, 2000);
                        });
                    }
                    else
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await ToastNotification.ShowAsync("Error", result.message, NotificationType.Error, 4000);
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" User cancelled deletion");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error deleting floor: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ToastNotification.ShowAsync("Error", $"Could not delete floor: {ex.Message}", NotificationType.Error, 4000);
                });
            }
            finally
            {
                _isFloorOperationInProgress = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadingIndicator.IsLoading = false;
                    AddFloorButton.IsEnabled = true;
                });
            }
        }

        private void UpsertFloorInList(Floor floor)
        {
            var existing = _floors.FirstOrDefault(item => item.Id == floor.Id);
            if (existing == null)
            {
                _floors.Add(floor);
            }
            else
            {
                var index = _floors.IndexOf(existing);
                if (index >= 0)
                {
                    _floors[index] = floor;
                }
            }

            _lastSuccessfulLoadAt = DateTime.UtcNow;
        }

        private void RemoveFloorFromList(int floorId)
        {
            var existing = _floors.FirstOrDefault(item => item.Id == floorId);
            if (existing != null)
            {
                _floors.Remove(existing);
            }

            _lastSuccessfulLoadAt = DateTime.UtcNow;
        }
    }
}
