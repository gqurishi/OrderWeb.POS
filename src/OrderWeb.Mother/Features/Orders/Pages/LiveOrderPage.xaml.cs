using Microsoft.Maui.Controls;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System.Threading;
using System.Threading.Tasks;

namespace POS_in_NET.Pages
{
    public partial class LiveOrderPage : ContentPage
    {
        private readonly MotherOpenOrderListService _openOrderListService;
        private readonly NavigationCoordinator _navigationCoordinator;
        private readonly OpenOrderListView _openOrderView = new();
        private bool _isSubscribedToLiveUpdates;
        private bool _isPageActive;
        private bool _isNavigatingAway;
        private int _pageGeneration;
        private CancellationTokenSource? _pageRefreshCts;
        private OpenOrderChannelKind _selectedChannel = OpenOrderChannelKind.All;
        private readonly SemaphoreSlim _ordersReloadGate = new(1, 1);
        private bool _pendingReload;

        public LiveOrderPage()
        {
            InitializeComponent();

            _openOrderListService = ServiceHelper.GetService<MotherOpenOrderListService>()
                ?? new MotherOpenOrderListService(
                    ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                    ServiceHelper.GetService<OrderLifecycleRolloutService>() ?? new OrderLifecycleRolloutService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService()),
                    ServiceHelper.GetService<TableSessionService>() ?? new TableSessionService(),
                    ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService());
            _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;

            TopBar.SetPageTitle("Live Order");
            OpenOrderHost.Content = _openOrderView;

            _openOrderView.ChannelChanged += OnChannelChanged;
            _openOrderView.OrderSelected += OnOrderSelected;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _isPageActive = true;
            _isNavigatingAway = false;
            _pageGeneration++;
            _pageRefreshCts?.Cancel();
            _pageRefreshCts?.Dispose();
            _pageRefreshCts = new CancellationTokenSource();
            SubscribeToLiveUpdates();
            RequestOrdersReload();
        }

        protected override void OnDisappearing()
        {
            _isPageActive = false;
            _pendingReload = false;
            _pageRefreshCts?.Cancel();
            base.OnDisappearing();
            UnsubscribeFromLiveUpdates();
        }

        private void SubscribeToLiveUpdates()
        {
            if (_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged += OnAppDataChanged;
            _isSubscribedToLiveUpdates = true;
        }

        private void UnsubscribeFromLiveUpdates()
        {
            if (!_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged -= OnAppDataChanged;
            _isSubscribedToLiveUpdates = false;
        }

        private void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (!_isPageActive || _isNavigatingAway
                || !e.HasAny(AppDataChangeKind.Orders, AppDataChangeKind.TableLayout))
            {
                return;
            }

            if (e.HasKind(AppDataChangeKind.Orders) && !e.IsFromCurrentTerminal)
            {
                var generation = _pageGeneration;
                var cancellationToken = _pageRefreshCts?.Token ?? CancellationToken.None;
                _ = MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (CanRenderOrders(generation, cancellationToken))
                    {
                        await ToastNotification.ShowAsync("Live update", e.ToastMessage, NotificationType.Info, 1400);
                    }
                });
            }

            RequestOrdersReload();
        }

        private void OnChannelChanged(object? sender, OpenOrderChannelKind channel)
        {
            _selectedChannel = channel;
        }

        private async void OnOrderSelected(object? sender, OpenOrderCardDto card)
        {
            if (_isNavigatingAway)
            {
                return;
            }

            if (card.Channel == OpenOrderChannelKind.Table
                && _openOrderListService.TryResolveTableSession(card, out var session)
                && session != null)
            {
                await NavigateToTableAsync(session);
                return;
            }

            if (_openOrderListService.TryResolveOrder(card, out var order) && order != null)
            {
                if (IsTableOrderType(order.OrderType) && order.TableSessionId is > 0
                    && _openOrderListService.TryResolveTableSession(card, out var linkedSession)
                    && linkedSession != null)
                {
                    await NavigateToTableAsync(linkedSession);
                    return;
                }

                await NavigateToOrderAsync(order);
            }
            else if (!string.IsNullOrWhiteSpace(card.OrderId))
            {
                await PushOrderPageAsync(new OrderPlacementPageSimple(existingOrderId: card.OrderId));
            }
        }

        private void RequestOrdersReload()
        {
            var refreshCts = _pageRefreshCts;
            if (!_isPageActive || _isNavigatingAway || refreshCts == null || refreshCts.IsCancellationRequested)
            {
                return;
            }

            _ = LoadAllOrdersAsync(_pageGeneration, refreshCts.Token);
        }

        private bool CanRenderOrders(int generation, CancellationToken cancellationToken) =>
            _isPageActive
            && !_isNavigatingAway
            && generation == _pageGeneration
            && !cancellationToken.IsCancellationRequested
            && ReferenceEquals(Shell.Current?.CurrentPage, this);

        private async Task LoadAllOrdersAsync(int generation, CancellationToken cancellationToken)
        {
            if (!CanRenderOrders(generation, cancellationToken))
            {
                return;
            }

            if (!await _ordersReloadGate.WaitAsync(0))
            {
                _pendingReload = true;
                return;
            }

            var scheduleTrailingReload = false;

            try
            {
                var reloadPasses = 0;
                do
                {
                    _pendingReload = false;
                    reloadPasses++;
                    cancellationToken.ThrowIfCancellationRequested();

                    _openOrderView.Apply(new OpenOrderListDto(
                        _selectedChannel,
                        Array.Empty<OpenOrderCardDto>(),
                        new OrderWeb.Contracts.Customers.CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"),
                        IsLoading: true,
                        LoadingMessage: "Loading open orders…"));

                    var result = await _openOrderListService.GetOpenOrdersAsync(_selectedChannel, cancellationToken);
                    if (!CanRenderOrders(generation, cancellationToken))
                    {
                        return;
                    }

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!CanRenderOrders(generation, cancellationToken))
                        {
                            return;
                        }

                        if (result.IsSuccess && result.Value != null)
                        {
                            _openOrderView.Apply(result.Value with { SelectedChannel = _selectedChannel });
                        }
                        else
                        {
                            _openOrderView.Apply(new OpenOrderListDto(
                                _selectedChannel,
                                Array.Empty<OpenOrderCardDto>(),
                                new OrderWeb.Contracts.Customers.CustomerSyncStatusDto(true, true, null, result.Error?.Message ?? "Failed to load open orders"),
                                StatusBanner: result.Error?.Message,
                                StatusBannerTone: "error"));
                        }
                    });
                }
                while (_pendingReload && reloadPasses < 2 && CanRenderOrders(generation, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                // Page closed while loading.
            }
            finally
            {
                scheduleTrailingReload = _pendingReload && _isPageActive && !_isNavigatingAway;
                _pendingReload = false;
                _ordersReloadGate.Release();
            }

            if (scheduleTrailingReload)
            {
                await Task.Delay(750);
                RequestOrdersReload();
            }
        }

        private static bool IsTableOrderType(string? orderType) =>
            (orderType ?? string.Empty).Trim().ToLowerInvariant() is "table" or "tbl" or "dine_in" or "dine-in";

        private async Task NavigateToOrderAsync(Order order)
        {
            await PushOrderPageAsync(new OrderPlacementPageSimple(existingOrderId: order.OrderId));
        }

        private async Task NavigateToTableAsync(TableSession session)
        {
            var tableName = session.Table?.TableNumber ?? session.TableId.ToString();
            var existingOrderId = session.LinkedOrderId ?? session.CurrentOrderId;
            if (session.Id < 0 && !string.IsNullOrWhiteSpace(existingOrderId))
            {
                await PushOrderPageAsync(new OrderPlacementPageSimple(existingOrderId));
                return;
            }

            var orderPage = new OrderPlacementPageSimple(
                tableName,
                session.PartySize,
                "Staff",
                1,
                session.Id,
                existingOrderId);

            await PushOrderPageAsync(orderPage);
        }

        private async Task PushOrderPageAsync(Page page)
        {
            _isNavigatingAway = true;
            _pageRefreshCts?.Cancel();

            try
            {
                await _navigationCoordinator.PushTemporaryPageAsync(page, animated: false);
            }
            catch
            {
                _isNavigatingAway = false;
                if (_isPageActive)
                {
                    _pageRefreshCts?.Dispose();
                    _pageRefreshCts = new CancellationTokenSource();
                    RequestOrdersReload();
                }

                throw;
            }
        }
    }
}
