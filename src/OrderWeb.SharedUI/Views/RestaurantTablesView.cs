using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace OrderWeb.SharedUI.Views;

public sealed class TableSelectedEventArgs(RestaurantTableDto table) : EventArgs
{
    public RestaurantTableDto Table { get; } = table;
}

/// <summary>Mother-style floor tabs plus positioned table canvas.</summary>
public class RestaurantTablesView : ContentView
{
    private readonly HorizontalStackLayout _floorTabs = new() { Spacing = 8, VerticalOptions = LayoutOptions.Center };
    private readonly Label _lastSync = new()
    {
        Text = "Not synced yet",
        FontSize = 11,
        VerticalOptions = LayoutOptions.Center,
        TextColor = Color.FromArgb("#0F766E")
    };
    private readonly Label _connection = new() { FontSize = 13, FontAttributes = FontAttributes.Bold, IsVisible = false };
    private readonly ChefLoaderView _loading = new()
    {
        Mode = ChefLoaderMode.Inline,
        Size = ChefLoaderSize.Sm,
        Message = string.Empty,
        DelayMilliseconds = 0,
        IsLoading = false
    };
    private readonly Image _floorBackground = new()
    {
        Aspect = Aspect.Fill,
        Opacity = 0.82,
        IsVisible = false
    };
    private readonly AbsoluteLayout _canvas;
    private readonly VerticalStackLayout _empty;
    private readonly List<(Border Card, RestaurantTableDto Table)> _tableViews = [];
    private string? _selectedFloorId;
    private string? _highlightedTableId;

    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(RestaurantTablesView), "Mother online", propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(RestaurantTablesView), false, propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty FloorBackgroundProperty = BindableProperty.Create(nameof(FloorBackground), typeof(ImageSource), typeof(RestaurantTablesView), propertyChanged: (b, _, v) =>
    {
        var view = (RestaurantTablesView)b;
        view._floorBackground.Source = (ImageSource?)v;
        view._floorBackground.IsVisible = v is ImageSource;
    });

    public RestaurantTablesView()
    {
        _connection.TextColor = Color.FromArgb("#B45309");

        _empty = new VerticalStackLayout
        {
            Spacing = 15,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    Padding = 20,
                    BackgroundColor = Color.FromArgb("#F3F4F6"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 50 },
                    HorizontalOptions = LayoutOptions.Center,
                    Content = new Image { Source = "table_1.png", WidthRequest = 60, HeightRequest = 60, Opacity = 0.4 }
                },
                new Label
                {
                    Text = "No Tables on This Floor",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#9CA3AF"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        _canvas = new AbsoluteLayout { BackgroundColor = Colors.White };
        AbsoluteLayout.SetLayoutBounds(_floorBackground, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_floorBackground, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_empty, new Rect(0.5, 0.5, -1, -1));
        AbsoluteLayout.SetLayoutFlags(_empty, AbsoluteLayoutFlags.PositionProportional);
        _canvas.Children.Add(_floorBackground);
        _canvas.Children.Add(_empty);

        var floorScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Center,
            Content = _floorTabs
        };

        var syncChip = new Border
        {
            BackgroundColor = Color.FromArgb("#F0FDFA"),
            Stroke = Color.FromArgb("#CCFBF1"),
            StrokeThickness = 1,
            Padding = new Thickness(10, 7),
            MinimumHeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _lastSync
        };

        var actions = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children = { _connection, _loading, syncChip }
        };
        var toolbarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star))
            },
            ColumnSpacing = 12
        };
        toolbarGrid.Add(floorScroll);
        toolbarGrid.Add(actions, 1);

        var toolbar = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 0,
            Padding = new Thickness(12, 9),
            MinimumHeightRequest = 64,
            Content = toolbarGrid
        };

        var canvasHost = new Grid
        {
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            Padding = new Thickness(12, 10, 12, 12),
            Children = { _canvas }
        };

        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        root.Add(toolbar);
        root.Add(canvasHost, 0, 1);
        Content = root;
    }

    public event EventHandler<TableSelectedEventArgs>? TableSelected;
    public event EventHandler<string>? FloorSelected;

    public FloorSnapshotDto? FloorSnapshot { get; private set; }
    public TableSnapshotDto? TableSnapshot { get; private set; }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public ImageSource? FloorBackground { get => (ImageSource?)GetValue(FloorBackgroundProperty); set => SetValue(FloorBackgroundProperty, value); }

    public void Bind(FloorSnapshotDto floors, TableSnapshotDto tables, string? preferredFloorId = null)
    {
        FloorSnapshot = floors;
        TableSnapshot = tables;
        _selectedFloorId = preferredFloorId
            ?? _selectedFloorId
            ?? floors.Floors.OrderBy(f => f.SortOrder).Select(f => f.Id).FirstOrDefault();
        _lastSync.Text = $"Synced {DateTime.Now:HH:mm:ss}";
        ApplyPresentationState();
        RebuildFloorTabs();
        RebuildTables();
    }

    /// <summary>Highlights a table in Mother blue while the guest picker is open.</summary>
    public void SetHighlightedTable(string? tableId)
    {
        _highlightedTableId = string.IsNullOrWhiteSpace(tableId) ? null : tableId.Trim();
        foreach (var (card, table) in _tableViews)
        {
            ApplyTableAppearance(card, table);
        }
    }

    public void ClearHighlightedTable() => SetHighlightedTable(null);

    private void RebuildFloorTabs()
    {
        _floorTabs.Children.Clear();
        foreach (var floor in (FloorSnapshot?.Floors ?? []).OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
        {
            var count = TableSnapshot?.Tables.Count(t => string.Equals(t.FloorId, floor.Id, StringComparison.OrdinalIgnoreCase)) ?? 0;
            var selected = string.Equals(floor.Id, _selectedFloorId, StringComparison.OrdinalIgnoreCase);
            var tab = new Border
            {
                BackgroundColor = Color.FromArgb(selected ? "#3B82F6" : "#F3F4F6"),
                Stroke = Color.FromArgb(selected ? "#3B82F6" : "#E5E7EB"),
                StrokeThickness = 1,
                Padding = new Thickness(16, 8),
                MinimumHeightRequest = 44,
                VerticalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new Label
                {
                    Text = $"{floor.Name} ({count})",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = selected ? Colors.White : Color.FromArgb("#374151"),
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var id = floor.Id;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _selectedFloorId = id;
                RebuildFloorTabs();
                RebuildTables();
                FloorSelected?.Invoke(this, id);
            };
            tab.GestureRecognizers.Add(tap);
            _floorTabs.Children.Add(tab);
        }
    }

    private void RebuildTables()
    {
        foreach (var (card, _) in _tableViews)
        {
            _canvas.Children.Remove(card);
        }
        _tableViews.Clear();

        var tables = TableSnapshot?.Tables
            .Where(t => string.Equals(t.FloorId, _selectedFloorId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name)
            .ToList() ?? [];

        _empty.IsVisible = tables.Count == 0;
        var index = 0;
        foreach (var table in tables)
        {
            var card = CreateTableCard(table);
            var x = table.X > 0 ? table.X : 40 + (index % 6) * 140;
            var y = table.Y > 0 ? table.Y : 40 + (index / 6) * 140;
            AbsoluteLayout.SetLayoutBounds(card, new Rect(x, y, 120, 120));
            AbsoluteLayout.SetLayoutFlags(card, AbsoluteLayoutFlags.None);
            _canvas.Children.Add(card);
            _tableViews.Add((card, table));
            index++;
        }
    }

    private Border CreateTableCard(RestaurantTableDto table)
    {
        var card = new Border
        {
            Padding = 8,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(2, 2),
                Radius = 8,
                Opacity = 0.15f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image
                    {
                        Source = string.IsNullOrWhiteSpace(table.Icon) ? "table_1.png" : table.Icon,
                        WidthRequest = 50,
                        HeightRequest = 50,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = table.Name,
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Ellipse
                    {
                        WidthRequest = 10,
                        HeightRequest = 10,
                        HorizontalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        ApplyTableAppearance(card, table);

        var captured = table;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => TableSelected?.Invoke(this, new TableSelectedEventArgs(captured));
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void ApplyTableAppearance(Border card, RestaurantTableDto table)
    {
        var highlighted = !string.IsNullOrWhiteSpace(_highlightedTableId) &&
                          string.Equals(table.Id, _highlightedTableId, StringComparison.OrdinalIgnoreCase);
        var (bg, border, text) = highlighted
            ? (Color.FromArgb("#3B82F6"), Color.FromArgb("#1D4ED8"), Colors.White)
            : TableColors(table);

        card.BackgroundColor = bg;
        card.Stroke = border;
        if (card.Content is VerticalStackLayout stack)
        {
            if (stack.Children.Count > 1 && stack.Children[1] is Label label)
            {
                label.TextColor = text;
            }

            if (stack.Children.Count > 2 && stack.Children[2] is Ellipse dot)
            {
                dot.Fill = new SolidColorBrush(highlighted ? Colors.White : border);
            }
        }
    }

    private void ApplyPresentationState()
    {
        _loading.IsLoading = IsLoading;
        var stale = ConnectionStatus.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                    ConnectionStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
                    ConnectionStatus.Contains("outdated", StringComparison.OrdinalIgnoreCase);
        _connection.Text = stale ? "Mother offline — cached tables may be outdated" : ConnectionStatus;
        _connection.IsVisible = stale;
    }

    private static (Color Bg, Color Border, Color Text) TableColors(RestaurantTableDto table)
    {
        var status = TableStatusText(table).ToLowerInvariant();
        if (status is "occupied" or "payment" or "needs attention" or "cleaning")
        {
            return (Color.FromArgb("#FEE2E2"), Color.FromArgb("#EF4444"), Color.FromArgb("#991B1B"));
        }

        if (status == "reserved" || !string.IsNullOrWhiteSpace(table.OpenOrderId))
        {
            return (Color.FromArgb("#FEF3C7"), Color.FromArgb("#F59E0B"), Color.FromArgb("#92400E"));
        }

        if (!string.Equals(table.Status, "Available", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(table.OpenOrderId))
        {
            return (Color.FromArgb("#FEF3C7"), Color.FromArgb("#F59E0B"), Color.FromArgb("#92400E"));
        }

        if (!string.IsNullOrWhiteSpace(table.OpenOrderId) ||
            string.Equals(table.Status, "Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return (Color.FromArgb("#FEF3C7"), Color.FromArgb("#F59E0B"), Color.FromArgb("#92400E"));
        }

        return (Color.FromArgb("#D1FAE5"), Color.FromArgb("#10B981"), Color.FromArgb("#065F46"));
    }

    private static string TableStatusText(RestaurantTableDto table)
    {
        if (string.Equals(table.SessionStatus, "Payment", StringComparison.OrdinalIgnoreCase)) return "Payment";
        if (string.Equals(table.SessionStatus, "Cleaning", StringComparison.OrdinalIgnoreCase)) return "Needs attention";
        if (!string.IsNullOrWhiteSpace(table.OpenOrderId) || string.Equals(table.Status, "Occupied", StringComparison.OrdinalIgnoreCase)) return "Occupied";
        return string.Equals(table.Status, "Reserved", StringComparison.OrdinalIgnoreCase) ? "Reserved" : "Available";
    }
}
