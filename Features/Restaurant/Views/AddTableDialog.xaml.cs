using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
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
        private Grid? _parentGrid;
        private readonly FloorService _floorService;
        private List<Floor> _floors = new List<Floor>();
        private string _selectedDesignIcon = "table_1.png"; // Default design
        private int _selectedFloorId = 0;
        private string _selectedFloorName = string.Empty;
        private List<Border> _floorBorders = new List<Border>();

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
            
            // Create elegant floor selection cards
            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                var index = i;
                
                // Create floor card
                var border = new Border
                {
                    BackgroundColor = i == 0 ? Color.FromArgb("#EEF2FF") : Colors.White,
                    Stroke = i == 0 ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB"),
                    StrokeThickness = i == 0 ? 2 : 1,
                    Padding = new Thickness(15, 12),
                    StrokeShape = new RoundRectangle { CornerRadius = 10 }
                };
                
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 10
                };
                
                // Floor info
                var floorStack = new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center
                };
                
                var floorNameLabel = new Label
                {
                    Text = floor.Name,
                    FontSize = 15,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#1F2937"),
                    FontAttributes = FontAttributes.Bold
                };
                
                var tableCountLabel = new Label
                {
                    Text = $"{floor.TableCount} table(s)",
                    FontSize = 12,
                    FontFamily = "OpenSansRegular",
                    TextColor = Color.FromArgb("#6B7280")
                };
                
                floorStack.Children.Add(floorNameLabel);
                floorStack.Children.Add(tableCountLabel);
                
                // Radio button indicator
                var radioCircle = new Border
                {
                    WidthRequest = 20,
                    HeightRequest = 20,
                    BackgroundColor = i == 0 ? Color.FromArgb("#6366F1") : Colors.Transparent,
                    Stroke = i == 0 ? Color.FromArgb("#6366F1") : Color.FromArgb("#D1D5DB"),
                    StrokeThickness = 2,
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    VerticalOptions = LayoutOptions.Center
                };
                
                if (i == 0)
                {
                    var checkmark = new Label
                    {
                        Text = "",
                        FontSize = 12,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        FontAttributes = FontAttributes.Bold
                    };
                    radioCircle.Content = checkmark;
                }
                
                grid.Children.Add(floorStack);
                Grid.SetColumn(floorStack, 0);
                
                grid.Children.Add(radioCircle);
                Grid.SetColumn(radioCircle, 1);
                
                border.Content = grid;
                
                // Add tap gesture
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
            
            // Update all floor cards
            for (int i = 0; i < _floorBorders.Count; i++)
            {
                var border = _floorBorders[i];
                var isSelected = i == index;
                
                // Update border styling
                border.BackgroundColor = isSelected ? Color.FromArgb("#EEF2FF") : Colors.White;
                border.Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
                border.StrokeThickness = isSelected ? 2 : 1;
                
                // Update radio circle
                if (border.Content is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border radioCircle)
                {
                    radioCircle.BackgroundColor = isSelected ? Color.FromArgb("#6366F1") : Colors.Transparent;
                    radioCircle.Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#D1D5DB");
                    
                    if (isSelected && radioCircle.Content == null)
                    {
                        radioCircle.Content = new Label
                        {
                            Text = "",
                            FontSize = 12,
                            TextColor = Colors.White,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            FontAttributes = FontAttributes.Bold
                        };
                    }
                    else if (!isSelected)
                    {
                        radioCircle.Content = null;
                    }
                }
            }
        }

        public async Task<(bool success, int floorId, string floorName, string? tableName, string tableDesignIcon)> ShowAsync()
        {
            _taskCompletionSource = new TaskCompletionSource<(bool, int, string, string?, string)>();
            
            // Find the page's content grid
            if (Application.Current?.MainPage is Shell shell)
            {
                var currentPage = shell.CurrentPage;
                
                if (currentPage != null)
                {
                    var pageContent = FindPageContent(currentPage);
                    if (pageContent is Grid mainGrid)
                    {
                        _parentGrid = mainGrid;
                        
                        // Make sure dialog fills entire grid
                        Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                        Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                        
                        // Add dialog overlay to the grid
                        mainGrid.Children.Add(this);
                        
                        // Focus on the entry field
                        await Task.Delay(100);
                        TableNumberEntry.Focus();
                    }
                }
            }

            return await _taskCompletionSource.Task;
        }

        private Element? FindPageContent(Element element)
        {
            var contentProperty = element.GetType().GetProperty("Content");
            if (contentProperty != null)
            {
                return contentProperty.GetValue(element) as Element;
            }
            return null;
        }

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

        private void OnDesign4Clicked(object sender, EventArgs e)
        {
            SelectDesign(4);
        }

        private void SelectDesign(int designNumber)
        {
            // Reset all borders
            Design1Border.BackgroundColor = Colors.White;
            Design1Border.Stroke = Color.FromArgb("#E5E7EB");
            Design1Border.StrokeThickness = 2;

            Design2Border.BackgroundColor = Colors.White;
            Design2Border.Stroke = Color.FromArgb("#E5E7EB");
            Design2Border.StrokeThickness = 2;

            Design3Border.BackgroundColor = Colors.White;
            Design3Border.Stroke = Color.FromArgb("#E5E7EB");
            Design3Border.StrokeThickness = 2;

            Design4Border.BackgroundColor = Colors.White;
            Design4Border.Stroke = Color.FromArgb("#E5E7EB");
            Design4Border.StrokeThickness = 2;

            // Highlight selected design
            switch (designNumber)
            {
                case 1:
                    Design1Border.BackgroundColor = Color.FromArgb("#F0F9FF");
                    Design1Border.Stroke = Color.FromArgb("#3B82F6");
                    Design1Border.StrokeThickness = 3;
                    _selectedDesignIcon = "table_1.png";
                    break;
                case 2:
                    Design2Border.BackgroundColor = Color.FromArgb("#F0F9FF");
                    Design2Border.Stroke = Color.FromArgb("#3B82F6");
                    Design2Border.StrokeThickness = 3;
                    _selectedDesignIcon = "table_2.png";
                    break;
                case 3:
                    Design3Border.BackgroundColor = Color.FromArgb("#F0F9FF");
                    Design3Border.Stroke = Color.FromArgb("#3B82F6");
                    Design3Border.StrokeThickness = 3;
                    _selectedDesignIcon = "table_3.png";
                    break;
                case 4:
                    Design4Border.BackgroundColor = Color.FromArgb("#F0F9FF");
                    Design4Border.Stroke = Color.FromArgb("#3B82F6");
                    Design4Border.StrokeThickness = 3;
                    _selectedDesignIcon = "table_4.png";
                    break;
            }
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }
    }
}
