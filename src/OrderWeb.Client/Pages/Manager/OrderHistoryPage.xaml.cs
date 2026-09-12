namespace OrderWeb.Client.Pages.Manager;

using System.Globalization;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

public partial class OrderHistoryPage : ContentPage
{
    private const int PageSize = 20;

    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherOrderHistoryClient _history;

    private DateTime _selectedDate = DateTime.Today;
    private OrderHistoryFilter _filter = OrderHistoryFilter.All;
    private string _activeSearch = string.Empty;
    private int _pageNumber = 1;
    private bool _hasNextPage;
    private bool _busy;
    private bool _isVisible;
    private bool _wsRefreshPending;
    private CancellationTokenSource? _loadCts;

    public OrderHistoryPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Order History");

        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _history = new MotherOrderHistoryClient(_cache, _offlinePolicy);

        Board.SetDateDisplay(_selectedDate);
        Board.SetFilter(_filter);
        Board.SetPaging(1, false, false);

        Board.DateFilterTapped += async (_, _) => await PickDateAsync();
        Board.SearchTapped += async (_, _) => await PickSearchAsync();
        Board.ClearSearchRequested += async (_, _) =>
        {
            _activeSearch = string.Empty;
            Board.SetSearchDisplay(null);
            _pageNumber = 1;
            await LoadHistoryAsync();
        };
        Board.FilterChanged += async (_, e) =>
        {
            _filter = e.Filter;
            _pageNumber = 1;
            await LoadHistoryAsync();
        };
        Board.PreviousPageRequested += async (_, _) =>
        {
            if (_pageNumber <= 1 || _busy)
            {
                return;
            }

            _pageNumber--;
            await LoadHistoryAsync();
        };
        Board.NextPageRequested += async (_, _) =>
        {
            if (!_hasNextPage || _busy)
            {
                return;
            }

            _pageNumber++;
            await LoadHistoryAsync();
        };
        Board.BackRequested += async (_, _) => await Navigation.PopAsync(false);
        Board.ViewOrderRequested += async (_, e) => await OpenDetailAsync(e.Row);

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Order History");

        if (!HasHistoryAccess())
        {
            await DisplayAlert(
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

    private async Task PickDateAsync()
    {
        var picked = await OrderHistoryPickers.PickDateAsync(Navigation, _selectedDate);
        if (picked is null)
        {
            return;
        }

        _selectedDate = picked.Value.Date;
        Board.SetDateDisplay(_selectedDate);
        _pageNumber = 1;
        await LoadHistoryAsync();
    }

    private async Task PickSearchAsync()
    {
        var value = await OrderHistoryPickers.PickSearchAsync(this, _activeSearch);
        if (value is null)
        {
            return;
        }

        _activeSearch = value;
        Board.SetSearchDisplay(string.IsNullOrWhiteSpace(_activeSearch) ? null : _activeSearch);
        _pageNumber = 1;
        await LoadHistoryAsync();
    }

    private async Task OpenDetailAsync(OrderHistoryRowPresentation row)
    {
        if (_busy || row.Tag is not HistoryRowTag tag)
        {
            return;
        }

        _busy = true;
        Board.SetLoading(true);
        try
        {
            var detail = await _history.GetDetailAsync(tag.OrderId, tag.DatabaseId);
            if (!detail.Success || detail.Order is null)
            {
                await DisplayAlertAsync(
                    "Order History",
                    detail.Error ?? detail.Message ?? "Could not load this order.",
                    "OK");
                return;
            }

            var presentation = MapDetail(detail.Order);
            var dialog = new OrderHistoryDetailDialog(presentation);
            dialog.PrintRequested += OnSharedReprintAsync;
            await dialog.ShowAsync(Navigation);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Order History", $"Could not open order details: {ex.Message}", "OK");
        }
        finally
        {
            Board.SetLoading(false);
            _busy = false;
        }
    }

    private async Task OnSharedReprintAsync(OrderHistoryDetailPresentation presentation)
    {
        if (presentation.Tag is not ClientOrderHistoryDetailDto order)
        {
            return;
        }

        var printOrderId = !string.IsNullOrWhiteSpace(order.OrderId)
            ? order.OrderId!.Trim()
            : (order.Id > 0 ? order.Id.ToString(CultureInfo.InvariantCulture) : string.Empty);
        if (string.IsNullOrWhiteSpace(printOrderId))
        {
            await DisplayAlertAsync("Reprint", "This order has no Mother print id.", "OK");
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var gate = _offlinePolicy.Evaluate(ClientOperation.OrderHistory, online);
        if (!gate.Allowed)
        {
            await DisplayAlertAsync("Reprint", gate.Message, "OK");
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            await DisplayAlertAsync("Reprint", "Sign in again before reprinting.", "OK");
            return;
        }

        var print = new MotherPrintClient();
        var result = await print.RequestPrintAsync("reprint", printOrderId, session);
        var ok = string.Equals(result.Status, "queued", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(result.Status, "printed", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(result.Status, "partial", StringComparison.OrdinalIgnoreCase);
        await DisplayAlertAsync(
            ok ? "Reprint queued" : "Reprint failed",
            result.Message ?? (ok
                ? "Mother POS accepted the reprint on its receipt printer."
                : "Mother POS could not reprint this receipt."),
            "OK");
    }

    private static OrderHistoryDetailPresentation MapDetail(ClientOrderHistoryDetailDto order)
    {
        static string Money(decimal value) => $"£{value:F2}";
        static string Text(string? value, string fallback = "—") =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        var lines = (order.Lines ?? Array.Empty<ClientOrderHistoryDetailLineDto>())
            .Select(line => new OrderHistoryDetailLinePresentation(
                QuantityDisplay: $"{line.Quantity}x",
                Name: Text(line.Name, "Item"),
                Details: line.Details,
                TotalDisplay: Money(line.TotalPrice)))
            .ToList();

        return new OrderHistoryDetailPresentation(
            OrderNumber: Text(order.OrderNumber, "#—"),
            OrderDateTime: Text(order.OrderDateTime),
            OrderTypeDisplay: Text(order.OrderTypeDisplay, "Order"),
            StatusDisplay: Text(order.StatusDisplay),
            CustomerName: Text(order.CustomerName),
            CustomerPhone: Text(order.CustomerPhone),
            CustomerAddress: Text(order.CustomerAddress),
            PaymentMethod: Text(order.PaymentDisplay ?? order.PaymentMethod),
            PaymentStatus: Text(order.PaymentStatusDisplay),
            AmountPaid: order.AmountPaid.HasValue ? Money(order.AmountPaid.Value) : "Not supplied",
            PaymentProvider: Text(order.PaymentProvider),
            PaymentReference: Text(order.PaymentReference),
            Scheduled: Text(order.ScheduledDisplay),
            Instructions: Text(order.SpecialInstructions, "None"),
            Promo: Text(order.PromoCode, "None"),
            GiftCard: Text(order.GiftCardDisplay, "None"),
            Loyalty: Text(order.LoyaltyDisplay, "None"),
            Subtotal: Money(order.SubtotalAmount),
            Vat: Money(order.TaxAmount),
            Discount: order.DiscountAmount > 0 ? $"-{Money(order.DiscountAmount)}" : Money(0),
            DeliveryFee: Money(order.DeliveryFee),
            ServiceCharge: Money(order.ServiceChargeAmount),
            Tips: Money(order.TipsAmount),
            Total: Money(order.TotalAmount),
            CanPrint: order.CanReprint && (!string.IsNullOrWhiteSpace(order.OrderId) || order.Id > 0),
            PrintButtonText: "Print",
            Lines: lines,
            Tag: order);
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

        if (!fromLiveNotify)
        {
            // Mother paints without ChefLoader flash for normal date/tab browse.
            Board.SetLoading(false);
        }

        try
        {
            var result = await _history.SearchAsync(
                _selectedDate,
                OrderHistoryFilterCodes.ToApiCode(_filter),
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
            Board.SetLoading(false);
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
            .Select(MapRow)
            .ToList();
        var voided = (result.Voided ?? Array.Empty<ClientOrderHistoryItemDto>())
            .Select(MapRow)
            .ToList();

        Board.SetRows(
            completed,
            voided,
            string.IsNullOrWhiteSpace(_activeSearch)
                ? "No orders found for this date"
                : "No matching orders found");

        _hasNextPage = result.HasNextPage && !result.FromCache;
        if (result.Page > 0)
        {
            _pageNumber = result.Page;
        }

        Board.SetPaging(_pageNumber, !_busy && _pageNumber > 1, !_busy && _hasNextPage);

        // Mother parity: status banner only while searching (idle offline stays quiet).
        var hasSearch = !string.IsNullOrWhiteSpace(_activeSearch);
        if (hasSearch && result.FromCache)
        {
            Board.SetStatusBanner(
                visible: true,
                title: $"Search: {_activeSearch} • {completed.Count + voided.Count} result(s)",
                detail: result.Message ?? "Showing last saved day from this terminal.",
                showClear: true,
                warning: true);
        }
        else if (hasSearch)
        {
            var count = completed.Count + voided.Count;
            Board.SetStatusBanner(
                visible: true,
                title: $"Search: {_activeSearch} • {count} result(s)",
                detail: result.Message ?? "Loaded from Mother POS.",
                showClear: true);
        }
        else
        {
            Board.SetStatusBanner(false, string.Empty, string.Empty, showClear: false);
        }
    }

    private static OrderHistoryRowPresentation MapRow(ClientOrderHistoryItemDto dto) =>
        new(
            OrderNumber: string.IsNullOrWhiteSpace(dto.OrderNumber) ? "#—" : dto.OrderNumber!,
            OrderDateTime: dto.OrderDateTime ?? string.Empty,
            CustomerDisplay: string.IsNullOrWhiteSpace(dto.CustomerDisplay) ? "Customer not supplied" : dto.CustomerDisplay!,
            OrderTypeDisplay: string.IsNullOrWhiteSpace(dto.OrderTypeDisplay) ? "Order" : dto.OrderTypeDisplay!,
            PaymentDisplay: string.IsNullOrWhiteSpace(dto.PaymentDisplay) ? "—" : dto.PaymentDisplay!,
            StatusDisplay: string.IsNullOrWhiteSpace(dto.StatusDisplay) ? "—" : dto.StatusDisplay!,
            TotalDisplay: $"£{dto.TotalAmount:F2}",
            IsVoided: string.Equals(dto.HistoryGroup, "voided", StringComparison.OrdinalIgnoreCase),
            Tag: new HistoryRowTag(dto.Id, dto.OrderId ?? string.Empty));

    private void ShowErrorState(string message)
    {
        Board.SetRows([], [], message);
        _hasNextPage = false;
        Board.SetPaging(_pageNumber, false, false);
        Board.SetStatusBanner(
            visible: true,
            title: "Could not load history",
            detail: message,
            showClear: !string.IsNullOrWhiteSpace(_activeSearch),
            error: true);
    }

    private void ShowForbiddenState(string? message = null)
    {
        ShowErrorState(message
            ?? "Order History is not enabled for this Client terminal. Ask Mother to grant Payments / Order History access.");
        Board.SetStatusBanner(
            visible: true,
            title: "Access denied",
            detail: message
                ?? "Order History is not enabled for this Client terminal. Ask Mother to grant Payments / Order History access.",
            showClear: false,
            error: true);
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

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
        await ClientSidebarNavigation.SwitchAsync(this, menu, currentRoute: "orderhistory");
    }

    private sealed record HistoryRowTag(int DatabaseId, string OrderId);
}
