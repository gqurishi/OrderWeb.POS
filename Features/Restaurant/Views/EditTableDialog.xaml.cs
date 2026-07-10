using POS_in_NET.Models;
using POS_in_NET.Services;
using Microsoft.Maui.Controls.Shapes;

namespace POS_in_NET.Views;

public partial class EditTableDialog : ContentView
{
    private TaskCompletionSource<(bool success, int floorId, string floorName, string? tableName, string tableDesignIcon)>? _taskCompletionSource;
    private int _currentTableId;
    private string _selectedDesignIcon = "table_1.png"; // Default design
    private readonly FloorService _floorService;
    private List<Floor> _floors = new();
    private readonly List<Border> _floorBorders = new();
    private int _selectedFloorId;
    private string _selectedFloorName = string.Empty;

    public EditTableDialog()
    {
        InitializeComponent();
        _floorService = new FloorService();
    }

    public void SetFloors(List<Floor> floors)
    {
        _floors = floors;
        FloorSelectionStack.Children.Clear();
        _floorBorders.Clear();

        for (int i = 0; i < floors.Count; i++)
        {
            var floor = floors[i];
            var index = i;
            var isSelected = floor.Id == _selectedFloorId || (_selectedFloorId == 0 && i == 0);

            var border = CreateFloorCard(floor, isSelected);
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => OnFloorSelected(index);
            border.GestureRecognizers.Add(tapGesture);

            _floorBorders.Add(border);
            FloorSelectionStack.Children.Add(border);
        }

        if (floors.Count > 0)
        {
            var selectedIndex = Math.Max(0, floors.FindIndex(f => f.Id == _selectedFloorId));
            OnFloorSelected(selectedIndex);
        }
    }

    public void SetTableData(int tableId, string tableName, int floorId, string tableDesignIcon = "table_1.png")
    {
        _currentTableId = tableId;
        TableNumberEntry.Text = tableName;
        _selectedDesignIcon = tableDesignIcon;
        
        // Select the current design
        int designNumber = tableDesignIcon switch
        {
            "table_2.png" => 2,
            "table_3.png" => 3,
            "table_4.png" => 4,
            _ => 1
        };
        SelectDesign(designNumber);
        
        _selectedFloorId = floorId;

        if (_floors.Count > 0)
        {
            var selectedIndex = Math.Max(0, _floors.FindIndex(f => f.Id == floorId));
            OnFloorSelected(selectedIndex);
        }
    }

    public Task<(bool success, int floorId, string floorName, string? tableName, string tableDesignIcon)> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<(bool, int, string, string?, string)>();
        IsVisible = true;
        
        // Auto-focus on table number entry
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
        {
            try
            {
                TableNumberEntry.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditTableDialog focus skipped: {ex.Message}");
            }
        });
        
        return _taskCompletionSource.Task;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        IsVisible = false;
        _taskCompletionSource?.SetResult((false, 0, string.Empty, null, "table_1.png"));
    }

    private async void OnSaveClicked(object sender, EventArgs e)
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

        var freshFloor = await _floorService.GetFloorByIdAsync(_selectedFloorId);
        if (freshFloor == null)
        {
            var latestFloors = await _floorService.GetAllFloorsAsync();
            freshFloor = latestFloors.FirstOrDefault(f => string.Equals(f.Name.Trim(), _selectedFloorName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (freshFloor == null)
            {
                _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Floor Removed", "The selected floor was deleted. Please refresh and choose another floor.");
                return;
            }

            _selectedFloorId = freshFloor.Id;
        }

        _selectedFloorName = freshFloor.Name;
        IsVisible = false;
        _taskCompletionSource?.SetResult((true, _selectedFloorId, _selectedFloorName, tableName, _selectedDesignIcon));
    }

    private Border CreateFloorCard(Floor floor, bool isSelected)
    {
        var border = new Border
        {
            BackgroundColor = isSelected ? Color.FromArgb("#EEF2FF") : Colors.White,
            Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB"),
            StrokeThickness = isSelected ? 2 : 1,
            Padding = new Thickness(15, 12),
            StrokeShape = new RoundRectangle { CornerRadius = 10 }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 10
        };

        var floorStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };

        floorStack.Children.Add(new Label
        {
            Text = floor.Name,
            FontSize = 15,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb("#1F2937"),
            FontAttributes = FontAttributes.Bold
        });

        floorStack.Children.Add(new Label
        {
            Text = $"{floor.TableCount} table(s)",
            FontSize = 12,
            FontFamily = "OpenSansRegular",
            TextColor = Color.FromArgb("#6B7280")
        });

        var radioCircle = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            BackgroundColor = isSelected ? Color.FromArgb("#6366F1") : Colors.Transparent,
            Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#D1D5DB"),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            VerticalOptions = LayoutOptions.Center
        };

        grid.Children.Add(floorStack);
        Grid.SetColumn(floorStack, 0);
        grid.Children.Add(radioCircle);
        Grid.SetColumn(radioCircle, 1);

        border.Content = grid;
        return border;
    }

    private void OnFloorSelected(int index)
    {
        if (index < 0 || index >= _floors.Count)
        {
            return;
        }

        _selectedFloorId = _floors[index].Id;
        _selectedFloorName = _floors[index].Name;

        for (int i = 0; i < _floorBorders.Count; i++)
        {
            var border = _floorBorders[i];
            var isSelected = i == index;

            border.BackgroundColor = isSelected ? Color.FromArgb("#EEF2FF") : Colors.White;
            border.Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB");
            border.StrokeThickness = isSelected ? 2 : 1;

            if (border.Content is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border radioCircle)
            {
                radioCircle.BackgroundColor = isSelected ? Color.FromArgb("#6366F1") : Colors.Transparent;
                radioCircle.Stroke = isSelected ? Color.FromArgb("#6366F1") : Color.FromArgb("#D1D5DB");
            }
        }
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

    private async void DisplayAlert(string title, string message, string cancel)
    {
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, cancel);
        }
    }
}
