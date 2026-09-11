using System.Globalization;
using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

/// <summary>
/// Thin Live Order host for push-navigation (SharedClientDashboard / sidebar).
/// Board chrome is SharedUI — same as MainPage ShowLiveOrders.
/// </summary>
public partial class LiveOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private readonly IDispatcherTimer _clockTimer;
    private string _selectedFilter = "All";
    private IReadOnlyList<MotherOrderState> _cachedOrders = Array.Empty<MotherOrderState>();
    private bool _isVisible;
    private bool _refreshInFlight;
    private bool _refreshQueued;
    private bool _suppressFilterEvent;
    private CancellationTokenSource? _wsRefreshCts;
    private string? _cardsFingerprint;

    public LiveOrderPage()
    {
        InitializeComponent();
        Sidebar.MenuItemSelected += async (_, label) => await SelectSidebarItemAsync(label);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllFromMotherAsync();

        Board.FilterChanged += OnBoardFilterChanged;
        Board.CardTapped += OnBoardCardTapped;

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        UpdateClock();
        ApplyBoardCards();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
        _clockTimer.Start();
        await RefreshOrdersAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
        _clockTimer.Stop();
    }

    private async void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible ||
            string.IsNullOrWhiteSpace(e.EventType) ||
            !e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _wsRefreshCts?.Cancel();
        _wsRefreshCts = new CancellationTokenSource();
        var token = _wsRefreshCts.Token;
        try
        {
            await Task.Delay(400, token);
            await MainThread.InvokeOnMainThreadAsync(() => RefreshOrdersAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshOrdersAsync()
    {
        if (_refreshInFlight)
        {
            _refreshQueued = true;
            return;
        }

        _refreshInFlight = true;
        try
        {
            do
            {
                _refreshQueued = false;

                // Paint cache first so the board never freezes waiting on Mother.
                _cachedOrders = ClientLiveOrderPresentation.OpenOrdersOnly(await _cache.GetOpenOrderStatesAsync());
                ApplyBoardCards();

                var motherOrders = await _orderClient.GetOpenOrdersAsync();
                if (motherOrders != null)
                {
                    await _cache.ReplaceOperationalOrdersAsync(motherOrders);
                    _cachedOrders = ClientLiveOrderPresentation.OpenOrdersOnly(await _cache.GetOpenOrderStatesAsync());
                    ApplyBoardCards();
                }
            }
            while (_refreshQueued);
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private void OnBoardFilterChanged(object? sender, LiveOrderFilterChangedEventArgs e)
    {
        if (_suppressFilterEvent)
        {
            return;
        }

        _selectedFilter = ClientLiveOrderPresentation.FromFilter(e.Filter);
        _cardsFingerprint = null;
        ApplyBoardCards();
    }

    private async void OnBoardCardTapped(object? sender, LiveOrderCardTappedEventArgs e)
    {
        try
        {
            var order = _cachedOrders.FirstOrDefault(o =>
                string.Equals(o.OrderId, e.Card.Key, StringComparison.OrdinalIgnoreCase));
            if (order is null)
            {
                return;
            }

            await OpenLiveOrderAsync(order);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Open order failed", ex.Message, "OK");
        }
    }

    private void ApplyBoardCards()
    {
        var filter = ClientLiveOrderPresentation.ToFilter(_selectedFilter);
        var fingerprint = ClientLiveOrderPresentation.Fingerprint(_cachedOrders, _selectedFilter);
        if (string.Equals(fingerprint, _cardsFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _cardsFingerprint = fingerprint;

        if (Board.SelectedFilter != filter)
        {
            _suppressFilterEvent = true;
            try
            {
                Board.SelectedFilter = filter;
            }
            finally
            {
                _suppressFilterEvent = false;
            }
        }

        Board.SetCards(
            ClientLiveOrderPresentation.Map(_cachedOrders, filter),
            ClientLiveOrderPresentation.EmptyText(filter));
    }

    private async Task OpenLiveOrderAsync(MotherOrderState order)
    {
        var type = ClientLiveOrderPresentation.NormalizeType(order.OrderType);
        if (type is not ("Collection" or "Delivery" or "Table"))
        {
            await DisplayAlertAsync(
                ClientLiveOrderPresentation.FormatNumber(order.OrderNumber, order.OrderId),
                $"{type}\nStatus: {order.Status}\nGuests: {order.Guests}\nTotal: £{order.Total:F2}",
                "OK");
            return;
        }

        var decision = _offlinePolicy.Evaluate(
            ClientOperation.OpenCollectionOrder,
            await _offlinePolicy.IsMotherOnlineAsync());
        if (!decision.Allowed)
        {
            await DisplayAlertAsync($"Open {type} blocked", decision.Message, "OK");
            return;
        }

        try
        {
            var result = await _orderClient.OpenOrderForEditAsync(order.OrderId);
            var page = new OrderPage(result.State, result.State.CustomerName, result.State.CustomerPhone);
            ClientPageChrome.HideSystemBackChrome(page);
            await Navigation.PushAsync(page, false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync($"Open {type} failed", ex.Message, "OK");
        }
    }

    private async Task UpdateAllFromMotherAsync()
    {
        if (!await _offlinePolicy.IsMotherOnlineAsync())
        {
            await DisplayAlertAsync("Update All", "Mother POS is offline. Connect to Mother to sync this terminal.", "OK");
            return;
        }

        try
        {
            var sync = new MotherOperationalSyncClient(_cache);
            var result = await sync.PullAllAsync();
            await RefreshOrdersAsync();
            await DisplayAlertAsync("Update All", result.SummaryMessage(), "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Update All failed", ex.Message, "OK");
        }
    }

    private async void OnMenuClicked(object sender, EventArgs e) => await OpenSidebarAsync();

    private async void OnLogoutClicked(object sender, EventArgs e) => await Navigation.PopToRootAsync(false);

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task SelectSidebarItemAsync(string label)
    {
        await CloseSidebarAsync();
        if (ClientSidebarNavigation.IsDashboard(label))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (await ClientSidebarNavigation.TryHandleMotherOnlyAsync(this, label))
        {
            return;
        }

        if (ClientHostAccess.IsMenuRoute(label, "liveorder") ||
            !ClientHostAccess.CanOpenMenu(label))
        {
            return;
        }

        if (ClientSidebarNavigation.IsCustomerSurface(label))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        var page = ClientSidebarNavigation.CreatePage(label);
        if (page is not null)
        {
            await Navigation.PushAsync(page, false);
        }
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
}
