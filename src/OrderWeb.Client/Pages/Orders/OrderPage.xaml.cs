using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Payments;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Access;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;
using OrderWeb.SharedUI.Hosting;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

/// <summary>
/// Client Order Place — hosts SharedUI shell; Mother HTTP via <see cref="ClientOrderPlaceHost"/>.
/// Entry: Table layout / Collection modal / Delivery modal / Live Order reopen (unchanged).
/// </summary>
public partial class OrderPage : ContentPage, IClientOrderPlaceUi
{
    private const int ToastAutoHideMs = 2200;
    private const double SidebarWidth = 280;

    private readonly ClientOrderPlaceHost _host;
    private readonly OrderPlaceShellView _shell;
    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private readonly PosLoadingOverlay _loader;
    private readonly PosToast _toast;
    private readonly Grid _toastLayer;
    private CancellationTokenSource? _toastHideCts;
    private bool _loaded;
    private bool _isVisible;
    private bool _liveReloadInFlight;
    private bool _sidebarOpen;

    public OrderPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);

        _host = new ClientOrderPlaceHost(this);
        _shell = new OrderPlaceShellView();
        _shell.BindHost(_host);
        _shell.SetFlyoutMenuOverlayMode(true);
        _shell.FlyoutMenuRequested += (_, _) => _ = OpenSidebarAsync();
        ShellHost.Content = _shell;

        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllFromMotherAsync();

        _loader = new PosLoadingOverlay
        {
            IsLoading = false,
            ZIndex = 50,
            Message = "Cooking up your data…"
        };
        _toast = new PosToast();
        _toast.DismissRequested += (_, _) => HideToast();
        _toastLayer = new Grid
        {
            IsVisible = false,
            InputTransparent = true,
            ZIndex = 55,
            Padding = 20,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.End,
            MaximumWidthRequest = 460,
            Children = { _toast }
        };

        if (Content is Grid root)
        {
            root.Children.Add(_loader);
            root.Children.Add(_toastLayer);
        }

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await OnLogoutClickedAsync();
        TopBar.IsVisible = false;
        _ = ApplyChromeAsync("Order");
    }

    public OrderPage(CachedTable table, int covers)
        : this()
    {
        _host.ConfigureTable(table, covers);
        _ = ApplyChromeAsync("Table Order");
    }

    public OrderPage(MotherOrderState order, string? customerName = null, string? customerPhone = null)
        : this()
    {
        _host.ConfigureExistingOrder(order, customerName, customerPhone);
        var title = CustomerOrderHubRules.IsDeliveryOrderType(order.OrderType)
            ? "Delivery Order"
            : CustomerOrderHubRules.IsCollectionOrderType(order.OrderType)
                ? "Collection Order"
                : "Table Order";
        _ = ApplyChromeAsync(title);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherOrderUpdated;

        if (!_loaded)
        {
            _loaded = true;
            await _host.InitializeAsync();
            await ApplyChromeAsync(TopBarTitleFromKind());
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherOrderUpdated;
        CancelToastAutoHide();
    }

    protected override bool OnBackButtonPressed() => true;

    public Task ShowAlertAsync(string title, string message) =>
        DisplayAlert(title, message, "OK");

    public Task ShowToastAsync(string title, string message, StatusKind kind = StatusKind.Info) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            CancelToastAutoHide();
            _toast.Title = title;
            _toast.Message = message;
            _toast.Kind = kind;
            _toast.IsRetryVisible = false;
            _toastLayer.IsVisible = true;
            _toastLayer.InputTransparent = false;
            _toastHideCts = new CancellationTokenSource();
            _ = AutoHideToastAsync(_toastHideCts.Token);
        });

    public Task SetLoadingAsync(bool isLoading, string? message = null) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _loader.Message = message;
            }

            _loader.IsLoading = isLoading;
        });

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        new OrderPlaceConfirmDialog().ShowAsync(this, title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string placeholder)
    {
        var numericOnly =
            title.Contains("PIN", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Contains("PIN", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Merge", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Redeem", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("table number", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("points to redeem", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Contains("07123", StringComparison.OrdinalIgnoreCase) ||
            placeholder.Contains("e.g. 12", StringComparison.OrdinalIgnoreCase);
        return new OrderPlacePromptDialog().ShowAsync(this, title, message, accept, cancel, placeholder, numericOnly: numericOnly);
    }

    public Task<string?> PickActionAsync(string title, params string[] options) =>
        new OrderPlaceActionSheetDialog().ShowAsync(this, title, options);

    public Task<string?> ShowMoreOptionsAsync(IReadOnlyList<OrderPlaceMoreOption> options) =>
        new OrderPlaceMoreOptionsDialog().ShowAsync(this, options);

    public Task<OrderPlaceDiscountResult?> ShowDiscountAsync(decimal subtotal) =>
        new OrderPlaceDiscountDialog().ShowAsync(this, subtotal);

    public Task<OrderPlaceTableOption?> ShowTableTransferAsync(string currentTableLabel, IReadOnlyList<OrderPlaceTableOption> availableTables) =>
        new OrderPlaceTableTransferDialog().ShowAsync(this, currentTableLabel, availableTables);

    public Task<string?> ShowFireCourseAsync(bool includeDrinks = false) =>
        new OrderPlaceFireCourseDialog().ShowAsync(this, includeDrinks);

    public Task<OrderPlaceVariantChoice?> PickVariantAsync(
        string itemName,
        IReadOnlyList<OrderPlaceVariantChoice> variants) =>
        new OrderPlaceVariantDialog().ShowAsync(this, itemName, variants);

    public Task<OrderPlaceQuickNoteResult> PickQuickNoteAsync(
        string itemName,
        IReadOnlyList<string> notes) =>
        new OrderPlaceQuickNoteDialog().ShowAsync(this, itemName, notes);

    public Task<IReadOnlyList<OrderPlaceAddonChoice>?> PickAddonsAsync(
        string itemName,
        IReadOnlyList<OrderPlaceAddonChoice> addons) =>
        new OrderPlaceAddonDialog().ShowAsync(this, itemName, addons);

    public Task<IReadOnlyList<string>?> PickMealDealChoicesAsync(
        string dealName,
        int pickCount,
        IReadOnlyList<string> choices) =>
        new OrderPlaceMealDealDialog().ShowAsync(this, dealName, pickCount, choices);

    public Task NavigateToPaymentAsync(decimal total, string orderId, int version, bool allowSplit = true) =>
        Navigation.PushAsync(new PaymentPage(total, orderId, version, allowSplit), false);

    public async Task CloseOrderPageAsync()
    {
        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync(false);
        }
    }

    private async void OnMotherOrderUpdated(object? sender, MotherDataChangedEventArgs e)
    {
        if (!_isVisible ||
            _host.CurrentOrder is null ||
            string.IsNullOrWhiteSpace(e.EventType) ||
            !e.EventType.Contains("order", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Version) &&
            !string.Equals(e.Version.Trim(), _host.CurrentOrder.OrderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(ReloadOpenOrderFromMotherAsync);
    }

    private async Task ReloadOpenOrderFromMotherAsync()
    {
        if (_host.CurrentOrder is null || _liveReloadInFlight)
        {
            return;
        }

        // Dirty / active edits: warn only — never silently overwrite local basket.
        if (_host.Session.Lines.Count > 0 ||
            !string.IsNullOrWhiteSpace(_host.Session.StatusMessage) ||
            _host.Session.ActionsBusy)
        {
            _host.MarkRemoteConflict();
            await ShowToastAsync(
                "Changed elsewhere",
                "This order changed on another terminal. Finish or discard local edits, then reopen.",
                StatusKind.Warning);
            return;
        }

        _liveReloadInFlight = true;
        try
        {
            var refreshed = await _host.ReloadFromMotherIfIdleAsync();
            if (refreshed)
            {
                await ShowToastAsync("Order refreshed", "Latest copy loaded from Mother POS.", StatusKind.Info);
            }
        }
        finally
        {
            _liveReloadInFlight = false;
        }
    }

    private async Task AutoHideToastAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ToastAutoHideMs, token);
            if (!token.IsCancellationRequested)
            {
                await MainThread.InvokeOnMainThreadAsync(HideToast);
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer toast.
        }
    }

    private void HideToast()
    {
        CancelToastAutoHide();
        _toastLayer.IsVisible = false;
        _toastLayer.InputTransparent = true;
    }

    private void CancelToastAutoHide()
    {
        try
        {
            _toastHideCts?.Cancel();
            _toastHideCts?.Dispose();
        }
        catch
        {
            // Ignore dispose races.
        }

        _toastHideCts = null;
    }

    private string TopBarTitleFromKind() =>
        _host.Session.Kind switch
        {
            OrderPlaceOrderKind.Delivery => "DELIVERY",
            OrderPlaceOrderKind.Collection => "COLLECTION",
            _ => _host.Session.HeaderTitle.Contains("TABLE", StringComparison.OrdinalIgnoreCase)
                ? _host.Session.HeaderTitle
                : "TABLE ORDER"
        };

    private async Task ApplyChromeAsync(string title)
    {
        try
        {
            var session = await _cache.GetCurrentLoginSessionAsync();
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TopBar.IsVisible = false;
                TopBar.HeightRequest = 0;
                TopBar.ConfigurePosChrome(
                    string.Empty,
                    session?.UserName,
                    session?.Role ?? "Client",
                    online ? "Connected" : "Mother Offline");
            });
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TopBar.IsVisible = false;
                TopBar.SetPageTitle(string.Empty);
            });
        }
    }

    private void OnOrderPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        // Match Mother: tight page padding — flyout control is overlayed, not a left rail.
        var tablet = Width <= 1280 || Height <= 800;
        MainContentGrid.Padding = tablet ? new Thickness(2) : new Thickness(4);
    }

    private void OnSidebarBackdropTapped(object? sender, TappedEventArgs e) =>
        _ = CloseSidebarAsync();

    private async Task OpenSidebarAsync()
    {
        if (_sidebarOpen)
        {
            return;
        }

        _sidebarOpen = true;
        SidebarLayer.InputTransparent = false;
        SidebarLayer.IsVisible = true;
        Sidebar.InputTransparent = false;
        SidebarBackdrop.InputTransparent = false;
        Sidebar.TranslationX = -SidebarWidth;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        if (!_sidebarOpen && !SidebarLayer.IsVisible)
        {
            return;
        }

        // Drop hit-testing immediately so the next Order Place tap is never eaten.
        SidebarLayer.InputTransparent = true;
        Sidebar.InputTransparent = true;
        SidebarBackdrop.InputTransparent = true;
        _sidebarOpen = false;

        try
        {
            await Sidebar.TranslateTo(-SidebarWidth, 0, 100, Easing.CubicIn);
        }
        catch
        {
            // Ignore animation failures during teardown.
        }

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

        Page page = menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new RestaurantPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new LiveOrderPage()
        };

        if (menu is "Collection" or "Delivery")
        {
            await ClientSideNavigation.PushFromSideAsync(Navigation, page);
            return;
        }

        await Navigation.PushAsync(page, false);
    }

    private async Task UpdateAllFromMotherAsync()
    {
        await CloseSidebarAsync();
        if (!await _offlinePolicy.IsMotherOnlineAsync())
        {
            await DisplayAlert("Update All", "Mother POS is offline. Connect to Mother to sync this terminal.", "OK");
            return;
        }

        try
        {
            var sync = new MotherOperationalSyncClient(_cache);
            var result = await sync.PullAllAsync();
            await DisplayAlert("Update All", result.SummaryMessage(), "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Update All failed", ex.Message, "OK");
        }
    }

    private async Task OnLogoutClickedAsync()
    {
        await CloseSidebarAsync();
        await _cache.ClearLoginSessionAsync();
        await Navigation.PopToRootAsync(false);
    }
}
