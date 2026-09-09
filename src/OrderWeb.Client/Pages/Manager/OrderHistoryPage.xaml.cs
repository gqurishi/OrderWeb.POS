namespace OrderWeb.Client.Pages.Manager;

using System.Globalization;
using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;

public partial class OrderHistoryPage : ContentPage
{
    private const int PageSize = 50;

    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherOrderHistoryClient _history;

    private string _selectedOrderType = "ALL";
    private string _activeSearch = string.Empty;
    private int _pageNumber = 1;
    private bool _hasNextPage;
    private bool _busy;
    private bool _isVisible;
    private bool _wsRefreshPending;
    private bool _suppressDateEvent;
    private CancellationTokenSource? _loadCts;

    public OrderHistoryPage()
    {
        InitializeComponent();
        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _history = new MotherOrderHistoryClient(_cache, _offlinePolicy);

        _suppressDateEvent = true;
        HistoryDatePicker.Date = DateTime.Today;
        _suppressDateEvent = false;
        UpdateTabStyles();
        UpdatePagingControls();

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!HasHistoryAccess())
        {
            await DisplayAlertAsync(
                "Order History",
                "This Client terminal is not allowed to use Order History. Ask Mother to grant Payments / Order History access.",
                "OK");
            await Navigation.PopAsync(false);
            return;
        }

        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
        await LoadHistoryAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
        _loadCts?.Cancel();
    }

    private static bool HasHistoryAccess() =>
        ClientHostAccess.Features.Contains(PosFeatureKeys.Payments) ||
        ClientHostAccess.CanOpenMenu("Order History");

    /// <summary>
    /// Light live refresh: WS carries no order list — only a notify. Re-fetch current date/filters
    /// while this page is visible (same pattern as Live Order).
    /// </summary>
    private async void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible || !IsHistoryRefreshEvent(e.EventType))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!_isVisible)
            {
                return;
            }

            if (_busy)
            {
                _wsRefreshPending = true;
                return;
            }

            await LoadHistoryAsync(fromLiveNotify: true);
        });
    }

    private static bool IsHistoryRefreshEvent(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return string.Equals(eventType, "order.updated", StringComparison.OrdinalIgnoreCase)
               || string.Equals(eventType, "history.updated", StringComparison.OrdinalIgnoreCase)
               || eventType.Contains("order.updated", StringComparison.OrdinalIgnoreCase)
               || eventType.Contains("history.updated", StringComparison.OrdinalIgnoreCase);
    }

    private async void OnHistoryDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (_suppressDateEvent)
        {
            return;
        }

        _pageNumber = 1;
        await LoadHistoryAsync();
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await ApplySearchAsync();
    private async void OnSearchCompleted(object? sender, EventArgs e) => await ApplySearchAsync();

    private async void OnClearSearchClicked(object? sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        _activeSearch = string.Empty;
        _pageNumber = 1;
        await LoadHistoryAsync();
    }

    private async void OnAllTabClicked(object? sender, TappedEventArgs e) => await SelectOrderTypeAsync("ALL");
    private async void OnCollectionTabClicked(object? sender, TappedEventArgs e) => await SelectOrderTypeAsync("COL");
    private async void OnDeliveryTabClicked(object? sender, TappedEventArgs e) => await SelectOrderTypeAsync("DEL");
    private async void OnTableTabClicked(object? sender, TappedEventArgs e) => await SelectOrderTypeAsync("TBL");
    private async void OnWebTabClicked(object? sender, TappedEventArgs e) => await SelectOrderTypeAsync("WEB");

    private async void OnPreviousPageClicked(object? sender, EventArgs e)
    {
        if (_pageNumber <= 1 || _busy)
        {
            return;
        }

        _pageNumber--;
        await LoadHistoryAsync();
    }

    private async void OnNextPageClicked(object? sender, EventArgs e)
    {
        if (!_hasNextPage || _busy)
        {
            return;
        }

        _pageNumber++;
        await LoadHistoryAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync(false);
    private async void OnBackdropTapped(object? sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async void OnViewOrderClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: OrderHistoryRowModel row })
        {
            return;
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        SetLoading(true);
        try
        {
            var detail = await _history.GetDetailAsync(row.OrderId, row.DatabaseId);
            if (!detail.Success || detail.Order is null)
            {
                await DisplayAlertAsync(
                    "Order History",
                    detail.Error ?? detail.Message ?? "Could not load this order.",
                    "OK");
                return;
            }

            var dialog = new OrderHistoryDetailDialogPage(detail.Order);
            await dialog.ShowAsync(Navigation);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Order History", $"Could not open order details: {ex.Message}", "OK");
        }
        finally
        {
            SetLoading(false);
            _busy = false;
        }
    }

    private async Task ApplySearchAsync()
    {
        _activeSearch = (SearchEntry.Text ?? string.Empty).Trim().TrimStart('#');
        _pageNumber = 1;
        await LoadHistoryAsync();
    }

    private async Task SelectOrderTypeAsync(string orderType)
    {
        if (string.Equals(_selectedOrderType, orderType, StringComparison.OrdinalIgnoreCase) || _busy)
        {
            return;
        }

        _selectedOrderType = orderType;
        _pageNumber = 1;
        UpdateTabStyles();
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync(bool fromLiveNotify = false)
    {
        if (!HasHistoryAccess())
        {
            ShowForbiddenState();
            return;
        }

        if (_busy)
        {
            if (fromLiveNotify)
            {
                _wsRefreshPending = true;
            }

            return;
        }

        _busy = true;
        _wsRefreshPending = false;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        // Soft refresh from WS: keep the list visible; avoid flashing the spinner on every notify.
        if (!fromLiveNotify)
        {
            SetLoading(true);
        }

        try
        {
            var result = await _history.SearchAsync(
                HistoryDatePicker.Date,
                _selectedOrderType,
                _activeSearch,
                _pageNumber,
                PageSize,
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success)
            {
                if (string.Equals(result.ErrorCode, OrderHistoryErrorCodes.AccessDenied, StringComparison.OrdinalIgnoreCase))
                {
                    ShowForbiddenState(result.Error ?? result.Message);
                    return;
                }

                // Keep prior rows on a failed live notify; only hard-fail interactive loads.
                if (!fromLiveNotify)
                {
                    ShowErrorState(result.Error ?? result.Message ?? "Could not load order history.");
                }

                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!fromLiveNotify)
            {
                ShowErrorState($"Could not load order history: {ex.Message}");
            }
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            if (_wsRefreshPending && _isVisible)
            {
                _wsRefreshPending = false;
                await LoadHistoryAsync(fromLiveNotify: true);
            }
        }
    }

    private void ApplyResult(ClientOrderHistoryResponseDto result)
    {
        var completed = (result.Completed ?? Array.Empty<ClientOrderHistoryItemDto>())
            .Select(OrderHistoryRowModel.FromDto)
            .ToList();
        var voided = (result.Voided ?? Array.Empty<ClientOrderHistoryItemDto>())
            .Select(OrderHistoryRowModel.FromDto)
            .ToList();

        CompletedOrdersCollection.ItemsSource = completed;
        VoidedOrdersCollection.ItemsSource = voided;
        CompletedEmptyLabel.IsVisible = completed.Count == 0 && voided.Count == 0;
        VoidedOrdersLayout.IsVisible = voided.Count > 0;

        _hasNextPage = result.HasNextPage && !result.FromCache;
        if (result.Page > 0)
        {
            _pageNumber = result.Page;
        }

        UpdatePagingControls();

        var hasSearch = !string.IsNullOrWhiteSpace(_activeSearch);
        StatusBanner.IsVisible = true;
        ClearSearchButton.IsVisible = hasSearch;
        StatusTitleLabel.Text = result.FromCache
            ? "Offline snapshot"
            : hasSearch
                ? $"Search: {_activeSearch}"
                : string.Format(CultureInfo.CurrentCulture, "{0:D}", HistoryDatePicker.Date);
        StatusDetailLabel.Text = result.Message
            ?? (result.FromCache
                ? "Showing last saved day from this terminal."
                : "Loaded from Mother POS.");
        StatusBanner.BackgroundColor = result.FromCache
            ? Color.FromArgb("#FFFBEB")
            : Color.FromArgb("#EFF6FF");
        StatusBanner.Stroke = result.FromCache
            ? Color.FromArgb("#FCD34D")
            : Color.FromArgb("#BFDBFE");
        StatusTitleLabel.TextColor = result.FromCache
            ? Color.FromArgb("#92400E")
            : Color.FromArgb("#1E3A8A");
        StatusDetailLabel.TextColor = result.FromCache
            ? Color.FromArgb("#B45309")
            : Color.FromArgb("#2563EB");
    }

    private void ShowErrorState(string message)
    {
        CompletedOrdersCollection.ItemsSource = null;
        VoidedOrdersCollection.ItemsSource = null;
        VoidedOrdersLayout.IsVisible = false;
        CompletedEmptyLabel.IsVisible = true;
        CompletedEmptyLabel.Text = message;
        _hasNextPage = false;
        UpdatePagingControls();

        StatusBanner.IsVisible = true;
        ClearSearchButton.IsVisible = !string.IsNullOrWhiteSpace(_activeSearch);
        StatusTitleLabel.Text = "Could not load history";
        StatusDetailLabel.Text = message;
        StatusBanner.BackgroundColor = Color.FromArgb("#FEF2F2");
        StatusBanner.Stroke = Color.FromArgb("#FECACA");
        StatusTitleLabel.TextColor = Color.FromArgb("#991B1B");
        StatusDetailLabel.TextColor = Color.FromArgb("#B91C1C");
    }

    private void ShowForbiddenState(string? message = null)
    {
        ShowErrorState(message
            ?? "Order History is not enabled for this Client terminal. Ask Mother to grant Payments / Order History access.");
        StatusTitleLabel.Text = "Access denied";
    }

    private void SetLoading(bool loading)
    {
        LoadingIndicator.IsLoading = loading;
        PreviousPageButton.IsEnabled = !loading && _pageNumber > 1;
        NextPageButton.IsEnabled = !loading && _hasNextPage;
    }

    private void UpdatePagingControls()
    {
        PageNumberLabel.Text = $"Page {_pageNumber}";
        PreviousPageButton.IsEnabled = !_busy && _pageNumber > 1;
        NextPageButton.IsEnabled = !_busy && _hasNextPage;
    }

    private void UpdateTabStyles()
    {
        StyleTab(AllTabBorder, AllTabLabel, "ALL");
        StyleTab(CollectionTabBorder, CollectionTabLabel, "COL");
        StyleTab(DeliveryTabBorder, DeliveryTabLabel, "DEL");
        StyleTab(TableTabBorder, TableTabLabel, "TBL");
        StyleTab(WebTabBorder, WebTabLabel, "WEB");
    }

    private void StyleTab(Border border, Label label, string orderType)
    {
        var selected = string.Equals(_selectedOrderType, orderType, StringComparison.OrdinalIgnoreCase);
        border.BackgroundColor = selected ? Color.FromArgb("#10B981") : Color.FromArgb("#F5F5F5");
        label.TextColor = selected ? Colors.White : Color.FromArgb("#6B7280");
    }

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (string.Equals(menu, "Dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (!ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        await Navigation.PushAsync(menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            _ => new OrderHistoryPage()
        }, false);
    }
}

public sealed class OrderHistoryRowModel
{
    public int DatabaseId { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string OrderDateTime { get; init; } = string.Empty;
    public string CustomerDisplay { get; init; } = string.Empty;
    public string OrderTypeDisplay { get; init; } = string.Empty;
    public string PaymentDisplay { get; init; } = string.Empty;
    public string StatusDisplay { get; init; } = string.Empty;
    public string TotalDisplay { get; init; } = string.Empty;

    public static OrderHistoryRowModel FromDto(ClientOrderHistoryItemDto dto) =>
        new()
        {
            DatabaseId = dto.Id,
            OrderId = dto.OrderId ?? string.Empty,
            OrderNumber = string.IsNullOrWhiteSpace(dto.OrderNumber) ? "#—" : dto.OrderNumber,
            OrderDateTime = dto.OrderDateTime ?? string.Empty,
            CustomerDisplay = string.IsNullOrWhiteSpace(dto.CustomerDisplay) ? "Customer not supplied" : dto.CustomerDisplay,
            OrderTypeDisplay = string.IsNullOrWhiteSpace(dto.OrderTypeDisplay) ? "Order" : dto.OrderTypeDisplay,
            PaymentDisplay = string.IsNullOrWhiteSpace(dto.PaymentDisplay) ? "—" : dto.PaymentDisplay,
            StatusDisplay = string.IsNullOrWhiteSpace(dto.StatusDisplay) ? "—" : dto.StatusDisplay,
            TotalDisplay = $"£{dto.TotalAmount:F2}"
        };
}
