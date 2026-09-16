using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddTableDialog : ContentView
    {
        private TaskCompletionSource<(bool success, int floorId, string floorName, string? tableName, string tableDesignIcon)>? _taskCompletionSource;
        private readonly FloorService _floorService;
        private List<Floor> _floors = new List<Floor>();
        private string _selectedDesignIcon = "table_1.png"; // Default design
        private int _selectedFloorId = 0;
        private string _selectedFloorName = string.Empty;
        private List<Border> _floorBorders = new List<Border>();
        private bool _keyboardOpen;

        public AddTableDialog()
        {
            InitializeComponent();
            _floorService = new FloorService();
            // Design 1 is selected by default
            SelectDesign(1);
        }

        public void SetFloors(List<Floor> floors)
        {
            _floors = floors;
            FloorSelectionStack.Children.Clear();
            _floorBorders.Clear();
            _selectedFloorId = 0;
            _selectedFloorName = string.Empty;
            
            // Slim single-line floor rows
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                var index = i;
                var selected = i == 0;

                var border = new Border
                {
                    BackgroundColor = selected ? Color.FromArgb("#EFF6FF") : Color.FromArgb("#FAFAFA"),
                    Stroke = selected ? Color.FromArgb("#3B82F6") : Color.FromArgb("#E5E7EB"),
                    StrokeThickness = 1,
                    Padding = new Thickness(12, 8),
                    HeightRequest = 40,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 }
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 8,
                    VerticalOptions = LayoutOptions.Center
                };

                var floorNameLabel = new Label
                {
                    Text = floor.Name,
                    FontSize = 13,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#111827"),
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                };

                var tableCountLabel = new Label
                {
                    Text = $"{floor.TableCount}",
                    FontSize = 12,
                    FontFamily = "OpenSansRegular",
                    TextColor = Color.FromArgb("#6B7280"),
                    VerticalOptions = LayoutOptions.Center
                };

                var radioCircle = new Border
                {
                    WidthRequest = 14,
                    HeightRequest = 14,
                    BackgroundColor = selected ? Color.FromArgb("#2563EB") : Colors.Transparent,
                    Stroke = selected ? Color.FromArgb("#2563EB") : Color.FromArgb("#D1D5DB"),
                    StrokeThickness = 1.5,
                    StrokeShape = new RoundRectangle { CornerRadius = 7 },
                    VerticalOptions = LayoutOptions.Center
                };

                grid.Children.Add(floorNameLabel);
                Grid.SetColumn(floorNameLabel, 0);
                grid.Children.Add(tableCountLabel);
                Grid.SetColumn(tableCountLabel, 1);
                grid.Children.Add(radioCircle);
                Grid.SetColumn(radioCircle, 2);

                border.Content = grid;

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) => OnFloorSelected(index);
                border.GestureRecognizers.Add(tapGesture);

                _floorBorders.Add(border);
                FloorSelectionStack.Children.Add(border);
            }
            
            // Select first floor by default
            if (floors.Count > 0)
            {
                _selectedFloorId = floors[0].Id;
                _selectedFloorName = floors[0].Name;
            }
        }

        private void OnFloorSelected(int index)
        {
            if (index < 0 || index >= _floors.Count) return;
            
            _selectedFloorId = _floors[index].Id;
            _selectedFloorName = _floors[index].Name;
            
            for (int i = 0; i < _floorBorders.Count; i++)
            {
                var border = _floorBorders[i];
                var isSelected = i == index;

                border.BackgroundColor = isSelected ? Color.FromArgb("#EFF6FF") : Color.FromArgb("#FAFAFA");
                border.Stroke = isSelected ? Color.FromArgb("#3B82F6") : Color.FromArgb("#E5E7EB");
                border.StrokeThickness = 1;

                if (border.Content is Grid grid && grid.Children.Count > 2 && grid.Children[2] is Border radioCircle)
                {
                    radioCircle.BackgroundColor = isSelected ? Color.FromArgb("#2563EB") : Colors.Transparent;
                    radioCircle.Stroke = isSelected ? Color.FromArgb("#2563EB") : Color.FromArgb("#D1D5DB");
                    radioCircle.Content = null;
                }
            }
        }

        public Task<(bool success, int floorId, string floorName, string? tableName, string tableDesignIcon)> ShowAsync()
        {
            _taskCompletionSource = new TaskCompletionSource<(bool, int, string, string?, string)>();

            TableNumberEntry.Text = string.Empty;
            SelectDesign(1);
            SharedTouchKeyboard.SetEnabled(TableNumberEntry, false);
            IsVisible = true;
            return _taskCompletionSource.Task;
        }

        private async void OnTableNumberTapped(object? sender, TappedEventArgs e)
        {
            if (_keyboardOpen)
            {
                return;
            }

            _keyboardOpen = true;
            try
            {
                var value = await new NumericKeyboardDialog().ShowDigitsAsync(
                    TableNumberEntry.Text,
                    "Table number",
                    maxDigits: 6,
                    hostPage: FindHostPage(),
                    overlayHost: DialogRoot);
                if (value is not null)
                {
                    TableNumberEntry.Text = value;
                }
            }
            finally
            {
                _keyboardOpen = false;
            }
        }

        private static ContentPage? FindHostPage() =>
            Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage
            ?? Shell.Current?.CurrentPage as ContentPage;

        private async void OnCreateClicked(object sender, EventArgs e)
        {
            var tableName = TableNumberEntry.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(tableName))
            {
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Required", "Please enter a table number");
                return;
            }

            if (_selectedFloorId == 0)
            {
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Required", "Please select a floor");
                return;
            }

            var selectedFloor = await _floorService.GetFloorByIdAsync(_selectedFloorId);
            if (selectedFloor == null)
            {
                var latestFloors = await _floorService.GetAllFloorsAsync();
                selectedFloor = latestFloors.FirstOrDefault(f => string.Equals(f.Name.Trim(), _selectedFloorName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (selectedFloor == null)
                {
                    _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Floor Removed", "The selected floor was deleted. Please refresh and choose another floor.");
                    return;
                }

                _selectedFloorId = selectedFloor.Id;
            }

            _selectedFloorName = selectedFloor.Name;
            _taskCompletionSource?.TrySetResult((true, _selectedFloorId, _selectedFloorName, tableName, _selectedDesignIcon));
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult((false, 0, string.Empty, null, "table_1.png"));
            CloseDialog();
        }

        // Table Design Selection Methods
        private void OnDesign1Clicked(object sender, EventArgs e)
        {
            SelectDesign(1);
        }

        private void OnDesign2Clicked(object sender, EventArgs e)
        {
            SelectDesign(2);
        }

        private void OnDesign3Clicked(object sender, EventArgs e)
        {
            SelectDesign(3);
        }

        private void SelectDesign(int designNumber)
        {
            Design1Border.BackgroundColor = Colors.White;
            Design1Border.Stroke = Color.FromArgb("#E5E7EB");
            Design1Border.StrokeThickness = 1;

            Design2Border.BackgroundColor = Colors.White;
            Design2Border.Stroke = Color.FromArgb("#E5E7EB");
            Design2Border.StrokeThickness = 1;

            Design3Border.BackgroundColor = Colors.White;
            Design3Border.Stroke = Color.FromArgb("#E5E7EB");
            Design3Border.StrokeThickness = 1;

            switch (designNumber)
            {
                case 1:
                    Design1Border.BackgroundColor = Color.FromArgb("#EFF6FF");
                    Design1Border.Stroke = Color.FromArgb("#3B82F6");
                    Design1Border.StrokeThickness = 1.5;
                    _selectedDesignIcon = "table_1.png";
                    break;
                case 2:
                    Design2Border.BackgroundColor = Color.FromArgb("#EFF6FF");
                    Design2Border.Stroke = Color.FromArgb("#3B82F6");
                    Design2Border.StrokeThickness = 1.5;
                    _selectedDesignIcon = "table_2.png";
                    break;
                case 3:
                    Design3Border.BackgroundColor = Color.FromArgb("#EFF6FF");
                    Design3Border.Stroke = Color.FromArgb("#3B82F6");
                    Design3Border.StrokeThickness = 1.5;
                    _selectedDesignIcon = "table_3.png";
                    break;
            }
        }

        private void CloseDialog()
        {
            IsVisible = false;
        }
    }
}
