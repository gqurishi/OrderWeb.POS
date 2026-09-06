using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Responsive;
using Microsoft.Maui.Layouts;

namespace OrderWeb.SharedUI.Views;

public sealed class TableSelectedEventArgs(RestaurantTableDto table) : EventArgs
{
    public RestaurantTableDto Table { get; } = table;
}

/// <summary>Shared floor + table card surface driven by contract DTOs.</summary>
public class RestaurantTablesView : ContentView
{
    private readonly FloorSelector _floors = new();
    private readonly FlexLayout _tables = new()
    {
        Direction = FlexDirection.Row,
        Wrap = FlexWrap.Wrap,
        JustifyContent = FlexJustify.Start,
        AlignItems = FlexAlignItems.Start
    };
    private readonly Label _empty = new()
    {
        Text = "No tables on this floor.",
        FontSize = 16,
        HorizontalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 40, 0, 0)
    };
    private readonly Label _version = new() { FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Label _connection = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly ActivityIndicator _loading = new() { IsRunning = true, IsVisible = false, WidthRequest = 24, HeightRequest = 24 };
    private readonly Image _floorBackground = new() { Aspect = Aspect.AspectFill, Opacity = .18, IsVisible = false };
    private readonly Grid _tableSurface;
    private string? _selectedFloorId;

    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(RestaurantTablesView), "Mother online", propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(RestaurantTablesView), false, propertyChanged: (b, _, _) => ((RestaurantTablesView)b).ApplyPresentationState());
    public static readonly BindableProperty FloorBackgroundProperty = BindableProperty.Create(nameof(FloorBackground), typeof(ImageSource), typeof(RestaurantTablesView), propertyChanged: (b, _, v) => { var view = (RestaurantTablesView)b; view._floorBackground.Source = (ImageSource?)v; view._floorBackground.IsVisible = v is ImageSource; });

    public RestaurantTablesView()
    {
        _empty.Use(Label.TextColorProperty, "OwTextMuted");
        _version.Use(Label.TextColorProperty, "OwTextMuted");
        _connection.Use(Label.TextColorProperty, "OwWarningText");
        _floors.FloorSelected += (_, id) =>
        {
            _selectedFloorId = id;
            RebuildTables();
            FloorSelected?.Invoke(this, id);
        };

        _tableSurface = new Grid { Children = { _floorBackground, _tables, _empty } };

        Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 16,
            Children =
            {
                _floors,
                new HorizontalStackLayout { Spacing = 8, Children = { _connection, _loading } },
                _version,
                new ScrollView { Content = _tableSurface }
            }
        };

        SizeChanged += (_, _) => ApplyResponsive();
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
        _floors.Floors = floors.Floors;
        _selectedFloorId = preferredFloorId
            ?? _selectedFloorId
            ?? floors.Floors.OrderBy(f => f.SortOrder).Select(f => f.Id).FirstOrDefault();
        _floors.SelectedFloorId = _selectedFloorId ?? string.Empty;
        _version.Text = $"Floor v{floors.Version} · Tables v{tables.Version}";
        ApplyPresentationState();
        RebuildTables();
    }

    private void RebuildTables()
    {
        _tables.Children.Clear();
        var tables = TableSnapshot?.Tables
            .Where(t => string.Equals(t.FloorId, _selectedFloorId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name)
            .ToList() ?? [];

        _empty.IsVisible = tables.Count == 0;
        foreach (var table in tables)
        {
            var kind = TableStatusKind(table);
            var status = TableStatusText(table);
            var details = table.GuestCount > 0
                ? $"{table.GuestCount} guests" + (table.CurrentTotal > 0 ? $" · {table.CurrentTotal:C}" : string.Empty)
                : $"{table.Capacity} seats";
            if (!string.IsNullOrWhiteSpace(table.OpenOrderId)) details += " · Open order";
            var card = new TableCard
            {
                Title = table.Name,
                Details = details,
                Status = status,
                StatusKind = kind,
                WidthRequest = 180,
                Margin = new Thickness(0, 0, 12, 12)
            };
            var tap = new TapGestureRecognizer();
            var captured = table;
            tap.Tapped += (_, _) => TableSelected?.Invoke(this, new TableSelectedEventArgs(captured));
            card.GestureRecognizers.Add(tap);
            _tables.Children.Add(card);
        }
    }

    private void ApplyResponsive()
    {
        var profile = PosResponsiveLayout.ForSize(Width, Height);
        Padding = new Thickness(profile.PagePadding);
    }

    private void ApplyPresentationState()
    {
        _loading.IsVisible = IsLoading;
        var stale = ConnectionStatus.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                    ConnectionStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
                    ConnectionStatus.Contains("outdated", StringComparison.OrdinalIgnoreCase);
        _connection.Text = stale ? "Mother offline — cached tables may be outdated" : ConnectionStatus;
        _connection.IsVisible = stale || IsLoading;
    }

    private static StatusKind TableStatusKind(RestaurantTableDto table) => TableStatusText(table).ToLowerInvariant() switch
    {
        "occupied" or "payment" => StatusKind.Error,
        "reserved" => StatusKind.Warning,
        "needs attention" or "cleaning" => StatusKind.Info,
        _ => StatusKind.Success
    };

    private static string TableStatusText(RestaurantTableDto table)
    {
        if (string.Equals(table.SessionStatus, "Payment", StringComparison.OrdinalIgnoreCase)) return "Payment";
        if (string.Equals(table.SessionStatus, "Cleaning", StringComparison.OrdinalIgnoreCase)) return "Needs attention";
        if (!string.IsNullOrWhiteSpace(table.OpenOrderId) || string.Equals(table.Status, "Occupied", StringComparison.OrdinalIgnoreCase)) return "Occupied";
        return string.Equals(table.Status, "Reserved", StringComparison.OrdinalIgnoreCase) ? "Reserved" : "Available";
    }
}
