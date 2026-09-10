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
    private readonly MotherImageCacheService _imageCache;
    private readonly RestaurantTablesView _tablesView = new();
    private RestaurantTableDto? _pendingTable;
    private Grid? _guestOverlay;
    private bool _isVisible;
    private IReadOnlyList<CachedFloor> _cachedFloors = Array.Empty<CachedFloor>();

    public RestaurantPage()
    {
        InitializeComponent();
        Shell.SetNavBarIsVisible(this, false);
        _layoutClient = new MotherLayoutClient(_cache);
        _imageCache = new MotherImageCacheService(_cache);
        _tablesView.TableSelected += OnTableSelected;
        _tablesView.FloorSelected += async (_, floorId) =>
        {
            var floor = _cachedFloors.FirstOrDefault(f =>
                string.Equals(f.Id.ToString(), floorId, StringComparison.OrdinalIgnoreCase));
            await ApplyFloorBackgroundAsync(floor);
        };
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
              e.EventType.Contains("floor", StringComparison.OrdinalIgnoreCase) ||
              e.EventType.Contains("layout", StringComparison.OrdinalIgnoreCase) ||
              e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Debounce WS floods; refresh from cache+Mother without blocking taps.
        await ScheduleRestaurantRefreshAsync();
    }

    private CancellationTokenSource? _restaurantWsCts;

    private async Task ScheduleRestaurantRefreshAsync()
    {
        _restaurantWsCts?.Cancel();
        _restaurantWsCts = new CancellationTokenSource();
        var token = _restaurantWsCts.Token;
        try
        {
            await Task.Delay(400, token);
            await MainThread.InvokeOnMainThreadAsync(LoadAsync);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadAsync()
    {
        await _cache.InitializeAsync();

        // Cache-first: show last-good floors/tables immediately.
        await BindFromCacheAsync();

        try
        {
            var result = await new MotherOperationalSyncClient(_cache).PullAllAsync();
            if (!result.LayoutOk)
            {
                System.Diagnostics.Debug.WriteLine($"Restaurant layout sync: {result.LayoutError}");
            }

            await BindFromCacheAsync();

            if (_cachedFloors.Count == 0 && !result.LayoutOk)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Restaurant empty after sync: {result.LayoutError ?? "Mother returned no tables."}");
            }
        }
        catch (Exception ex)
        {
            if (_cachedFloors.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"Restaurant load failed with empty cache: {ex.Message}");
            }
        }
    }

    private async Task BindFromCacheAsync()
    {
        var floors = await _cache.GetFloorsWithTablesAsync();
        if (floors.Count == 0)
        {
            floors = Array.Empty<CachedFloor>();
        }

        _cachedFloors = floors;
        var floorVersion = floors.SelectMany(f => f.Tables).Select(t => t.Version).DefaultIfEmpty(1).Max().ToString();
        var floorDto = new FloorSnapshotDto(
            floorVersion,
            floors.Select(f => new FloorDto(f.Id.ToString(), f.Name, f.SortOrder, f.BackgroundImageId)).ToList());

        var tableDto = new TableSnapshotDto(
            floorVersion,
            floors.SelectMany(f => f.Tables.Select(t =>
            {
                var isProblem =
                    string.Equals(t.Status, "Reserved", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.SessionStatus, "Cleaning", StringComparison.OrdinalIgnoreCase);
                var hasActiveSession =
                    !string.IsNullOrWhiteSpace(t.CurrentOrderId) ||
                    string.Equals(t.Status, "Occupied", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.SessionStatus, "Occupied", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.SessionStatus, "Payment", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.SessionStatus, "Open", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.SessionStatus, "Active", StringComparison.OrdinalIgnoreCase);
                return new RestaurantTableDto(
                    t.Id.ToString(),
                    t.FloorId.ToString(),
                    t.TableNumber,
                    t.Seats,
                    t.Status,
                    t.PositionX,
                    t.PositionY,
                    t.CurrentOrderId,
                    t.Version,
                    t.Covers,
                    t.CurrentTotal,
                    t.SessionStatus,
                    t.DesignIcon,
                    IsProblem: isProblem,
                    HasActiveSession: hasActiveSession);
            })).ToList());

        _tablesView.IsLoading = false;
        _tablesView.Bind(floorDto, tableDto);
        _tablesView.SetSyncState(RestaurantSyncMode.Live, DateTime.Now);
        var selected = floors.FirstOrDefault(f => f.Tables.Count > 0) ?? floors.FirstOrDefault();
        await ApplyFloorBackgroundAsync(selected);
    }

    private async Task ApplyFloorBackgroundAsync(CachedFloor? floor)
    {
        if (string.IsNullOrWhiteSpace(floor?.BackgroundImageId))
        {
            _tablesView.FloorBackground = null;
            return;
        }

        var imageId = floor.BackgroundImageId.Trim();
        _tablesView.FloorBackground = await _imageCache.GetOrRefreshAsync(
            new MotherImageDescriptor(imageId, $"/api/client/images/{Uri.EscapeDataString(imageId)}", string.Empty));
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
            DismissHitOverlay(Root, _guestOverlay);
            _guestOverlay = null;
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
        DismissHitOverlay(Root, _guestOverlay);
        _guestOverlay = null;
    }

    private static void DismissHitOverlay(Layout root, View overlay)
    {
        overlay.InputTransparent = true;
        if (overlay is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is VisualElement element)
                {
                    element.InputTransparent = true;
                }
            }
        }

        if (root.Children.Contains(overlay))
        {
            root.Children.Remove(overlay);
        }
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
