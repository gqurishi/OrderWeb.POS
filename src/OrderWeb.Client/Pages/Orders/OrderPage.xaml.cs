using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Payments;
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

    public OrderPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);

        _host = new ClientOrderPlaceHost(this);
        _shell = new OrderPlaceShellView();
        _shell.BindHost(_host);
        ShellHost.Content = _shell;

        _loader = new PosLoadingOverlay
        {
            IsLoading = false,
            ZIndex = 50,
            Message = "Opening order…"
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

        TopBar.MenuClicked += async (_, _) => await OnMenuClickedAsync();
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
        DisplayAlert(title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string placeholder) =>
        DisplayPromptAsync(title, message, accept, cancel, placeholder);

    public async Task<string?> PickActionAsync(string title, params string[] options)
    {
        var result = await DisplayActionSheet(title, "Cancel", null, options);
        return string.IsNullOrWhiteSpace(result) || result == "Cancel" ? null : result;
    }

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

    private void OnOrderPlaceMenuClicked(object? sender, EventArgs e) =>
        _ = OnMenuClickedAsync();

    private async Task OnMenuClickedAsync()
    {
        if (Shell.Current != null)
        {
            Shell.Current.FlyoutIsPresented = true;
            return;
        }

        await Navigation.PopToRootAsync(false);
    }

    private async Task OnLogoutClickedAsync()
    {
        await _cache.ClearLoginSessionAsync();
        await Navigation.PopToRootAsync(false);
    }
}
