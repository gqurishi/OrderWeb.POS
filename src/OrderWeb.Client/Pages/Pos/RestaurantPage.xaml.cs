using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Pos;

public partial class RestaurantPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherLayoutClient _layoutClient;
    private readonly RestaurantTablesView _tablesView = new();
    private RestaurantTableDto? _pendingTable;
    private Grid? _guestOverlay;
    private bool _isVisible;

    public RestaurantPage()
    {
        InitializeComponent();
        Shell.SetNavBarIsVisible(this, false);
        _layoutClient = new MotherLayoutClient(_cache);
        _tablesView.TableSelected += OnTableSelected;
        Root.Children.Add(_tablesView);
        _ = LoadAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
    }

    private async void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible ||
            string.IsNullOrWhiteSpace(e.EventType) ||
            !(e.EventType.Contains("table", StringComparison.OrdinalIgnoreCase) ||
              e.EventType.Contains("layout", StringComparison.OrdinalIgnoreCase) ||
              e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(LoadAsync);
    }

    private async Task LoadAsync()
    {
        await _cache.InitializeAsync();
        try
        {
            var layout = await _layoutClient.GetLayoutAsync();
            if (layout is not null)
            {
                await _cache.ReplaceLayoutAsync(
                    new FloorSnapshotDto(layout.Version, layout.Floors),
                    new TableSnapshotDto(layout.Version, layout.Tables));
            }
        }
        catch
        {
            // Fall back to cached floor below.
        }

        var floors = await _cache.GetFloorsWithTablesAsync();
        if (floors.Count == 0)
        {
            floors =
            [
                new CachedFloor(1, "Main Floor", 1,
                [
                    new CachedTable(10, 1, "10", 4, "Available", 0m, null, 0, null, null, 0, 1, 52, 64),
                    new CachedTable(11, 1, "11", 4, "Available", 0m, null, 0, null, null, 0, 1, 264, 64),
                    new CachedTable(12, 1, "12", 4, "Occupied", 0m, "demo-12", 4, null, "Ordering", 0, 1, 476, 64)
                ])
            ];
        }

        var floorVersion = floors.SelectMany(f => f.Tables).Select(t => t.Version).DefaultIfEmpty(1).Max().ToString();
        var floorDto = new FloorSnapshotDto(
            floorVersion,
            floors.Select(f => new FloorDto(f.Id.ToString(), f.Name, f.SortOrder)).ToList());

        var tableDto = new TableSnapshotDto(
            floorVersion,
            floors.SelectMany(f => f.Tables.Select(t => new RestaurantTableDto(
                t.Id.ToString(),
                t.FloorId.ToString(),
                t.TableNumber,
                t.Seats,
                t.Status,
                t.PositionX,
                t.PositionY,
                t.CurrentOrderId,
                t.Version))).ToList());

        _tablesView.Bind(floorDto, tableDto);
    }

    private void OnTableSelected(object? sender, TableSelectedEventArgs e)
    {
        _pendingTable = e.Table;
        if (!string.Equals(e.Table.Status, "Available", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Table.OpenOrderId))
        {
            _ = OpenOrderAsync(e.Table, Math.Max(1, e.Table.Capacity));
            return;
        }

        ShowGuestOverlay();
    }

    private void ShowGuestOverlay()
    {
        if (_pendingTable is null)
        {
            return;
        }

        if (_guestOverlay is not null)
        {
            Root.Children.Remove(_guestOverlay);
        }

        _tablesView.SetHighlightedTable(_pendingTable.Id);

        var picker = new GuestCountControl
        {
            TableTitle = $"Table {_pendingTable.Name}",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        picker.ResetCustomEntry();
        picker.Cancelled += (_, _) => HideGuestOverlay();
        picker.CoverConfirmed += async (_, covers) =>
        {
            if (_pendingTable is null) return;
            var table = _pendingTable;
            HideGuestOverlay();
            await OpenOrderAsync(table, covers);
        };

        _guestOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Children = { picker }
        };
        Root.Children.Add(_guestOverlay);
    }

    private void HideGuestOverlay()
    {
        _tablesView.ClearHighlightedTable();
        if (_guestOverlay is null) return;
        Root.Children.Remove(_guestOverlay);
        _guestOverlay = null;
    }

    private async Task OpenOrderAsync(RestaurantTableDto table, int covers)
    {
        var cached = new CachedTable(
            int.TryParse(table.Id, out var id) ? id : 0,
            int.TryParse(table.FloorId, out var floorId) ? floorId : 0,
            table.Name,
            table.Capacity,
            table.Status,
            0m,
            table.OpenOrderId,
            covers,
            null,
            null,
            0,
            (int)table.Revision,
            (int)table.X,
            (int)table.Y);
        await Navigation.PushAsync(new OrderPage(cached, covers), false);
    }
}
