using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace OrderWeb.SharedUI.Views;

public sealed class TableSelectedEventArgs(RestaurantTableDto table) : EventArgs
{
    public RestaurantTableDto Table { get; } = table;
}

public sealed class TableMovedEventArgs(string tableId, double x, double y) : EventArgs
{
    public string TableId { get; } = tableId;
    public double X { get; } = x;
    public double Y { get; } = y;
}

public enum RestaurantSyncMode
{
    NotSynced,
    Live,
    Fallback,
    Updating
}

/// <summary>Mother-style floor tabs + positioned table canvas (shared by Mother and Client).</summary>
public class RestaurantTablesView : ContentView
{
    private const int GridSnap = 20;
    private readonly HorizontalStackLayout _floorTabs = new() { Spacing = 8, VerticalOptions = LayoutOptions.Center };
    private readonly HorizontalStackLayout _adminToolsHost = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Center
    };
    private readonly Label _lastSync = new()
    {
        Text = "Not synced yet",
        FontSize = 11,
        FontFamily = "OpenSansRegular",
        VerticalOptions = LayoutOptions.Center,
        TextColor = Color.FromArgb("#6B7280")
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
    private readonly Image _floorBackgroundBlur = new()
    {
        Aspect = Aspect.Fill,
        Opacity = 0.12,
        TranslationX = 1.5,
        TranslationY = 1.5,
        IsVisible = false
    };
    private readonly AbsoluteLayout _canvas;
    private readonly VerticalStackLayout _empty;
    private readonly Button _emptyAction;
    private readonly Dictionary<string, TableCardHost> _tableHosts = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedFloorId;
    private string? _highlightedTableId;
    private string? _floorTabsFingerprint;
    private bool _layoutEditEnabled;
    private double _dragOriginX;
    private double _dragOriginY;
    private double _dragStartX;
    private double _dragStartY;

    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(
        nameof(ConnectionStatus), typeof(string), typeof(RestaurantTablesView), "Mother online",
        propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(
        nameof(IsLoading), typeof(bool), typeof(RestaurantTablesView), false,
        propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty FloorBackgroundProperty = BindableProperty.Create(
        nameof(FloorBackground), typeof(ImageSource), typeof(RestaurantTablesView),
        propertyChanged: (b, _, v) => ((RestaurantTablesView)b).ApplyFloorBackground((ImageSource?)v));
    public static readonly BindableProperty AdminToolsContentProperty = BindableProperty.Create(
        nameof(AdminToolsContent), typeof(View), typeof(RestaurantTablesView),
        propertyChanged: (b, _, v) => ((RestaurantTablesView)b).ApplyAdminTools((View?)v));
    public static readonly BindableProperty LayoutEditEnabledProperty = BindableProperty.Create(
        nameof(LayoutEditEnabled), typeof(bool), typeof(RestaurantTablesView), false,
        propertyChanged: (b, _, v) =>
        {
            var view = (RestaurantTablesView)b;
            view._layoutEditEnabled = v is true;
            view.RebuildPanGestures();
        });
    public static readonly BindableProperty EmptyActionTextProperty = BindableProperty.Create(
        nameof(EmptyActionText), typeof(string), typeof(RestaurantTablesView), string.Empty,
        propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyEmptyAction());

    public RestaurantTablesView()
    {
        _connection.TextColor = Color.FromArgb("#B45309");

        _emptyAction = new Button
        {
            Text = "Go to Table Management",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            CornerRadius = 8,
            Padding = new Thickness(20, 12),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        _emptyAction.Clicked += (_, _) => EmptyActionRequested?.Invoke(this, EventArgs.Empty);

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
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#9CA3AF"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Add tables from Table Management",
                    FontSize = 14,
                    FontFamily = "OpenSansRegular",
                    TextColor = Color.FromArgb("#D1D5DB"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                _emptyAction
            }
        };

        _canvas = new AbsoluteLayout { BackgroundColor = Colors.White };
        AbsoluteLayout.SetLayoutBounds(_floorBackground, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_floorBackground, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_floorBackgroundBlur, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_floorBackgroundBlur, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_empty, new Rect(0.5, 0.5, -1, -1));
        AbsoluteLayout.SetLayoutFlags(_empty, AbsoluteLayoutFlags.PositionProportional);
        _canvas.Children.Add(_floorBackground);
        _canvas.Children.Add(_floorBackgroundBlur);
        _canvas.Children.Add(_empty);

        var floorScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Center,
            MaximumHeightRequest = 36,
            Content = _floorTabs
        };

        var syncChip = new Border
        {
            BackgroundColor = Color.FromArgb("#F0FDFA"),
            Stroke = Color.FromArgb("#CCFBF1"),
            StrokeThickness = 1,
            Padding = new Thickness(8, 4),
            MinimumHeightRequest = 28,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _lastSync
        };

        var actionsScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            MaximumHeightRequest = 36,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Children = { _connection, _loading, syncChip, _adminToolsHost }
            }
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
        toolbarGrid.Add(actionsScroll, 1);

        var toolbar = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            Padding = new Thickness(12, 4),
            MinimumHeightRequest = 36,
            MaximumHeightRequest = 40,
            Content = toolbarGrid
        };

        var separator = new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E5E7EB") };
        var canvasHost = new Grid
        {
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            Padding = new Thickness(12, 10, 12, 12),
            Children = { _canvas }
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        root.Add(toolbar);
        root.Add(separator, 0, 1);
        root.Add(canvasHost, 0, 2);
        Content = root;
    }

    public event EventHandler<TableSelectedEventArgs>? TableSelected;
    public event EventHandler<string>? FloorSelected;
    public event EventHandler<TableMovedEventArgs>? TableMoved;
    public event EventHandler? EmptyActionRequested;

    public FloorSnapshotDto? FloorSnapshot { get; private set; }
    public TableSnapshotDto? TableSnapshot { get; private set; }
    public string? SelectedFloorId => _selectedFloorId;
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public ImageSource? FloorBackground { get => (ImageSource?)GetValue(FloorBackgroundProperty); set => SetValue(FloorBackgroundProperty, value); }
    public View? AdminToolsContent { get => (View?)GetValue(AdminToolsContentProperty); set => SetValue(AdminToolsContentProperty, value); }
    public bool LayoutEditEnabled { get => (bool)GetValue(LayoutEditEnabledProperty); set => SetValue(LayoutEditEnabledProperty, value); }
    public string EmptyActionText { get => (string)GetValue(EmptyActionTextProperty); set => SetValue(EmptyActionTextProperty, value); }

    public void Bind(FloorSnapshotDto floors, TableSnapshotDto tables, string? preferredFloorId = null)
    {
        FloorSnapshot = floors;
        TableSnapshot = tables;
        _selectedFloorId = preferredFloorId
            ?? _selectedFloorId
            ?? floors.Floors.OrderBy(f => f.SortOrder).Select(f => f.Id).FirstOrDefault();
        ApplyPresentationState();
        RebuildFloorTabsIfNeeded();
        SyncTables();
    }

    public void SetSyncState(RestaurantSyncMode mode, DateTime? at = null)
    {
        switch (mode)
        {
            case RestaurantSyncMode.Updating:
                _lastSync.Text = "Updating…";
                _lastSync.TextColor = Color.FromArgb("#0F766E");
                break;
            case RestaurantSyncMode.Fallback when at.HasValue:
                _lastSync.Text = $"Fallback {at.Value:HH:mm:ss}";
                _lastSync.TextColor = Color.FromArgb("#B45309");
                break;
            case RestaurantSyncMode.Live when at.HasValue:
                _lastSync.Text = $"Live · {at.Value:HH:mm:ss}";
                _lastSync.TextColor = Color.FromArgb("#047857");
                break;
            default:
                _lastSync.Text = "Not synced yet";
                _lastSync.TextColor = Color.FromArgb("#6B7280");
                break;
        }
    }

    /// <summary>Highlights a table in Mother blue while the guest picker is open (text color stays status-based).</summary>
    public void SetHighlightedTable(string? tableId)
    {
        _highlightedTableId = string.IsNullOrWhiteSpace(tableId) ? null : tableId.Trim();
        foreach (var host in _tableHosts.Values)
        {
            ApplyTableAppearance(host);
            host.AppearanceKey = AppearanceKey(host.Table);
        }
    }

    public void ClearHighlightedTable() => SetHighlightedTable(null);

    public IReadOnlyDictionary<string, (double X, double Y)> GetTablePositions()
    {
        var map = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, host) in _tableHosts)
        {
            var bounds = AbsoluteLayout.GetLayoutBounds(host.Card);
            map[id] = (bounds.X, bounds.Y);
        }

        return map;
    }

    private void ApplyFloorBackground(ImageSource? source)
    {
        _floorBackground.Source = source;
        _floorBackgroundBlur.Source = source;
        var visible = source is not null;
        _floorBackground.IsVisible = visible;
        _floorBackgroundBlur.IsVisible = visible;
    }

    private void ApplyAdminTools(View? content)
    {
        _adminToolsHost.Children.Clear();
        if (content != null)
        {
            _adminToolsHost.Children.Add(content);
        }
    }

    private void ApplyEmptyAction()
    {
        var text = EmptyActionText?.Trim() ?? string.Empty;
        _emptyAction.Text = string.IsNullOrWhiteSpace(text) ? "Go to Table Management" : text;
        _emptyAction.IsVisible = !string.IsNullOrWhiteSpace(EmptyActionText);
    }

    private void RebuildFloorTabsIfNeeded()
    {
        var fingerprint = string.Join("|",
            (FloorSnapshot?.Floors ?? [])
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Name)
                .Select(f =>
                {
                    var count = FloorTableCount(f);
                    var selected = string.Equals(f.Id, _selectedFloorId, StringComparison.OrdinalIgnoreCase) ? "1" : "0";
                    return $"{f.Id}:{f.Name}:{count}:{selected}";
                }));

        if (string.Equals(fingerprint, _floorTabsFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _floorTabsFingerprint = fingerprint;
        RebuildFloorTabs();
    }

    private int FloorTableCount(FloorDto floor)
    {
        if (floor.TableCount.HasValue)
        {
            return Math.Max(0, floor.TableCount.Value);
        }

        return TableSnapshot?.Tables.Count(t =>
            string.Equals(t.FloorId, floor.Id, StringComparison.OrdinalIgnoreCase)) ?? 0;
    }

    private void RebuildFloorTabs()
    {
        _floorTabs.Children.Clear();
        foreach (var floor in (FloorSnapshot?.Floors ?? []).OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
        {
            var count = FloorTableCount(floor);
            var selected = string.Equals(floor.Id, _selectedFloorId, StringComparison.OrdinalIgnoreCase);
            var tab = new Border
            {
                BackgroundColor = Color.FromArgb(selected ? "#3B82F6" : "#F3F4F6"),
                Stroke = selected ? Colors.Transparent : Color.FromArgb("#E5E7EB"),
                StrokeThickness = 1,
                Padding = new Thickness(12, 4),
                MinimumHeightRequest = 30,
                VerticalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new Label
                {
                    Text = $"{floor.Name} ({count})",
                    FontSize = 13,
                    FontFamily = "OpenSansSemibold",
                    TextColor = selected ? Colors.White : Color.FromArgb("#374151"),
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var id = floor.Id;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (string.Equals(_selectedFloorId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _selectedFloorId = id;
                _floorTabsFingerprint = null;
                RebuildFloorTabsIfNeeded();
                SyncTables();
                FloorSelected?.Invoke(this, id);
            };
            tab.GestureRecognizers.Add(tap);
            _floorTabs.Children.Add(tab);
        }
    }

    private void SyncTables()
    {
        var tables = TableSnapshot?.Tables
            .Where(t => string.Equals(t.FloorId, _selectedFloorId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name)
            .ToList() ?? [];

        _empty.IsVisible = tables.Count == 0;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var table in tables)
        {
            keep.Add(table.Id);
            var x = table.X > 0 ? table.X : 40 + (index % 6) * 140;
            var y = table.Y > 0 ? table.Y : 40 + (index / 6) * 140;
            if (_tableHosts.TryGetValue(table.Id, out var host))
            {
                UpdateTableCard(host, table, x, y);
            }
            else
            {
                host = CreateTableHost(table);
                AbsoluteLayout.SetLayoutBounds(host.Card, new Rect(x, y, 120, 120));
                AbsoluteLayout.SetLayoutFlags(host.Card, AbsoluteLayoutFlags.None);
                _canvas.Children.Add(host.Card);
                _tableHosts[table.Id] = host;
            }

            index++;
        }

        foreach (var id in _tableHosts.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            if (_tableHosts.Remove(id, out var host))
            {
                _canvas.Children.Remove(host.Card);
            }
        }
    }

    private TableCardHost CreateTableHost(RestaurantTableDto table)
    {
        var nameLabel = new Label
        {
            Text = table.Name,
            FontSize = 16,
            FontFamily = "OpenSansSemibold",
            HorizontalOptions = LayoutOptions.Center
        };
        var icon = new Image
        {
            Source = string.IsNullOrWhiteSpace(table.Icon) ? "table_1.png" : table.Icon,
            WidthRequest = 50,
            HeightRequest = 50,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        };
        var statusDot = new Ellipse
        {
            WidthRequest = 10,
            HeightRequest = 10,
            HorizontalOptions = LayoutOptions.Center
        };

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
                Children = { icon, nameLabel, statusDot }
            }
        };

        var host = new TableCardHost
        {
            Card = card,
            Table = table,
            NameLabel = nameLabel,
            Icon = icon,
            StatusDot = statusDot,
            AppearanceKey = string.Empty
        };

        ApplyTableAppearance(host);
        host.AppearanceKey = AppearanceKey(table);
        AttachGestures(host);
        return host;
    }

    private void AttachGestures(TableCardHost host)
    {
        host.Card.GestureRecognizers.Clear();

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => TableSelected?.Invoke(this, new TableSelectedEventArgs(host.Table));
        host.Card.GestureRecognizers.Add(tap);

        if (!_layoutEditEnabled)
        {
            return;
        }

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) => OnTablePan(host, e);
        host.Card.GestureRecognizers.Add(pan);
    }

    private void RebuildPanGestures()
    {
        foreach (var host in _tableHosts.Values)
        {
            AttachGestures(host);
        }
    }

    private void OnTablePan(TableCardHost host, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
            {
                var bounds = AbsoluteLayout.GetLayoutBounds(host.Card);
                _dragOriginX = bounds.X;
                _dragOriginY = bounds.Y;
                _dragStartX = bounds.X;
                _dragStartY = bounds.Y;
                host.Card.Scale = 1.05;
                host.Card.Opacity = 0.8;
                break;
            }
            case GestureStatus.Running:
            {
                var canvasWidth = _canvas.Width > 0 ? _canvas.Width : 1200;
                var canvasHeight = _canvas.Height > 0 ? _canvas.Height : 800;
                var newX = Math.Max(0, Math.Min(_dragOriginX + e.TotalX, canvasWidth - 120));
                var newY = Math.Max(0, Math.Min(_dragOriginY + e.TotalY, canvasHeight - 120));
                AbsoluteLayout.SetLayoutBounds(host.Card, new Rect(newX, newY, 120, 120));
                break;
            }
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                host.Card.Scale = 1.0;
                host.Card.Opacity = 1.0;
                var finalBounds = AbsoluteLayout.GetLayoutBounds(host.Card);
                var snappedX = Math.Round(finalBounds.X / GridSnap) * GridSnap;
                var snappedY = Math.Round(finalBounds.Y / GridSnap) * GridSnap;
                AbsoluteLayout.SetLayoutBounds(host.Card, new Rect(snappedX, snappedY, 120, 120));
                if (Math.Abs(_dragStartX - snappedX) > 1 || Math.Abs(_dragStartY - snappedY) > 1)
                {
                    TableMoved?.Invoke(this, new TableMovedEventArgs(host.Table.Id, snappedX, snappedY));
                }

                break;
            }
        }
    }

    private void UpdateTableCard(TableCardHost host, RestaurantTableDto table, double x, double y)
    {
        host.Table = table;
        if (!string.Equals(host.NameLabel.Text, table.Name, StringComparison.Ordinal))
        {
            host.NameLabel.Text = table.Name;
        }

        var icon = string.IsNullOrWhiteSpace(table.Icon) ? "table_1.png" : table.Icon;
        if (host.Icon.Source is not FileImageSource file ||
            !string.Equals(file.File, icon, StringComparison.OrdinalIgnoreCase))
        {
            host.Icon.Source = icon;
        }

        var appearanceKey = AppearanceKey(table);
        if (!string.Equals(host.AppearanceKey, appearanceKey, StringComparison.Ordinal))
        {
            ApplyTableAppearance(host);
            host.AppearanceKey = appearanceKey;
        }

        var bounds = AbsoluteLayout.GetLayoutBounds(host.Card);
        if (Math.Abs(bounds.X - x) > 0.5 || Math.Abs(bounds.Y - y) > 0.5)
        {
            AbsoluteLayout.SetLayoutBounds(host.Card, new Rect(x, y, 120, 120));
        }
    }

    private string AppearanceKey(RestaurantTableDto table)
    {
        var highlighted = !string.IsNullOrWhiteSpace(_highlightedTableId) &&
                          string.Equals(table.Id, _highlightedTableId, StringComparison.OrdinalIgnoreCase);
        return $"{table.IsProblem}|{table.HasActiveSession}|{table.OpenOrderId}|{table.SessionStatus}|{table.Status}|{highlighted}";
    }

    private void ApplyTableAppearance(TableCardHost host)
    {
        var table = host.Table;
        var highlighted = !string.IsNullOrWhiteSpace(_highlightedTableId) &&
                          string.Equals(table.Id, _highlightedTableId, StringComparison.OrdinalIgnoreCase);
        var (bg, border, text) = TableColors(table);
        if (highlighted)
        {
            bg = Color.FromArgb("#3B82F6");
            border = Color.FromArgb("#1D4ED8");
            // Mother keeps status text color while cover popup is open.
        }

        host.Card.BackgroundColor = bg;
        host.Card.Stroke = border;
        host.NameLabel.TextColor = text;
        host.StatusDot.Fill = new SolidColorBrush(border);
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

    /// <summary>Mother semantics: green idle, amber active session, red problem.</summary>
    private static (Color Bg, Color Border, Color Text) TableColors(RestaurantTableDto table)
    {
        var session = table.SessionStatus?.Trim() ?? string.Empty;
        var status = table.Status?.Trim() ?? string.Empty;
        var hasProblem =
            table.IsProblem ||
            string.Equals(status, "Reserved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Cleaning", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Needs attention", StringComparison.OrdinalIgnoreCase);

        if (hasProblem)
        {
            return (Color.FromArgb("#FEE2E2"), Color.FromArgb("#EF4444"), Color.FromArgb("#991B1B"));
        }

        var hasActiveSession =
            table.HasActiveSession ||
            !string.IsNullOrWhiteSpace(table.OpenOrderId) ||
            string.Equals(status, "Occupied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Occupied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Payment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Open", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session, "Active", StringComparison.OrdinalIgnoreCase);

        if (hasActiveSession)
        {
            return (Color.FromArgb("#FEF3C7"), Color.FromArgb("#F59E0B"), Color.FromArgb("#92400E"));
        }

        return (Color.FromArgb("#D1FAE5"), Color.FromArgb("#10B981"), Color.FromArgb("#065F46"));
    }

    private sealed class TableCardHost
    {
        public required Border Card { get; init; }
        public required RestaurantTableDto Table { get; set; }
        public required Label NameLabel { get; init; }
        public required Image Icon { get; init; }
        public required Ellipse StatusDot { get; init; }
        public required string AppearanceKey { get; set; }
    }
}
