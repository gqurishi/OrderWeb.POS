namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

public partial class AdvanceOrdersPage : ContentPage
{
    private const int PendingSoftNoteThreshold = 5;

    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherAdvanceOrderClient _advance;
    private readonly MotherOrderClient _orders;
    private bool _busy;

    public AdvanceOrdersPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Advance Orders");

        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _advance = new MotherAdvanceOrderClient(_cache, _offlinePolicy);
        _orders = new MotherOrderClient(_cache);

        Board.RangeChanged += async (_, e) => await LoadAsync(e.Range);
        Board.RowTapped += async (_, e) => await OpenOrderAsync(e.Row.OrderId);
        Board.PrintKitchenRequested += async (_, e) => await PrintKitchenAsync(e.Row);

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        TopBar.SetPageTitle("Advance Orders");

        if (!HasAdvanceAccess())
        {
            await DisplayAlert(
                "Advance Orders",
                "Advance Orders is available to Manager accounts when Collection, Delivery, or Live Orders is enabled.",
                "OK");
            await Navigation.PopAsync(false);
            return;
        }

        await LoadAsync(Board.SelectedRange);
    }

    private static bool HasAdvanceAccess()
    {
        if (!string.Equals(ClientHostAccess.SessionRole, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ClientHostAccess.CanOpenMenu("Advance Orders")
               || ClientHostAccess.Features.Contains(PosFeatureKeys.Collection)
               || ClientHostAccess.Features.Contains(PosFeatureKeys.Delivery)
               || ClientHostAccess.Features.Contains(PosFeatureKeys.LiveOrders);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync(false);
            }
            else
            {
                await Navigation.PopToRootAsync(false);
            }
        }
        catch
        {
            await Navigation.PopToRootAsync(false);
        }
    }

    private async Task LoadAsync(AdvanceOrderRange range)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Board.SetBusy(true);
        Board.SetStatus(null);
        try
        {
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            var gate = _offlinePolicy.Evaluate(ClientOperation.AdvanceOrders, online);
            if (!gate.Allowed)
            {
                Board.SetRows(Array.Empty<AdvanceOrderRowPresentation>());
                Board.SetStatus(gate.Message ?? "Mother offline", isError: true);
                return;
            }

            var response = await _advance.ListAsync(AdvanceOrderSampleData.ApiRangeKey(range));
            if (!response.Success)
            {
                Board.SetRows(Array.Empty<AdvanceOrderRowPresentation>());
                Board.SetStatus(response.Message ?? "Could not load advance orders.", isError: true);
                return;
            }

            var presentation = (response.Orders ?? Array.Empty<AdvanceOrderDto>())
                .Select(ToRow)
                .ToList();
            Board.SetRows(presentation);

            if (range == AdvanceOrderRange.Today)
            {
                var pending = presentation.Count(r => !r.KitchenPrinted);
                if (pending >= PendingSoftNoteThreshold)
                {
                    Board.SetStatus(
                        $"{pending} pending advance orders today — kitchen auto-prints at T−3h.",
                        isError: false);
                }
            }
        }
        catch (Exception ex)
        {
            Board.SetRows(Array.Empty<AdvanceOrderRowPresentation>());
            Board.SetStatus(ex.Message, isError: true);
        }
        finally
        {
            Board.SetBusy(false);
            _busy = false;
        }
    }

    private async Task OpenOrderAsync(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId) || _busy)
        {
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var gate = _offlinePolicy.Evaluate(ClientOperation.OpenCollectionOrder, online);
        if (!gate.Allowed)
        {
            await DisplayAlert("Advance Orders", gate.Message ?? "Mother offline", "OK");
            return;
        }

        try
        {
            var opened = await _orders.OpenOrderForEditAsync(orderId.Trim());
            await _cache.SaveOrderStateAsync(opened.State);
            var page = new OrderPage(opened.State, opened.State.CustomerName, opened.State.CustomerPhone);
            ClientPageChrome.HideSystemBackChrome(page);
            await Navigation.PushAsync(page, false);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Advance Orders", ex.Message, "OK");
        }
    }

    private async Task PrintKitchenAsync(AdvanceOrderRowPresentation row)
    {
        if (_busy || string.IsNullOrWhiteSpace(row.OrderId))
        {
            return;
        }

        _busy = true;
        Board.SetBusy(true);
        try
        {
            var online = await _offlinePolicy.IsMotherOnlineAsync();
            var gate = _offlinePolicy.Evaluate(ClientOperation.AdvanceOrders, online);
            if (!gate.Allowed)
            {
                Board.SetStatus(gate.Message ?? "Mother offline", isError: true);
                return;
            }

            var result = await _advance.PrintKitchenAsync(row.OrderId);
            if (!result.Success)
            {
                Board.SetStatus(result.Message ?? "Print failed", isError: true);
                return;
            }

            Board.SetStatus("Kitchen ticket sent.", isError: false);
            await LoadAsync(Board.SelectedRange);
        }
        catch (Exception ex)
        {
            Board.SetStatus(ex.Message, isError: true);
        }
        finally
        {
            Board.SetBusy(false);
            _busy = false;
        }
    }

    private static AdvanceOrderRowPresentation ToRow(AdvanceOrderDto dto) =>
        new(
            dto.OrderId,
            string.IsNullOrWhiteSpace(dto.OrderNumber) ? "#" : dto.OrderNumber!,
            string.IsNullOrWhiteSpace(dto.OrderType) ? "Collection" : dto.OrderType,
            string.IsNullOrWhiteSpace(dto.ScheduledDisplay) ? "—" : dto.ScheduledDisplay!,
            string.IsNullOrWhiteSpace(dto.CustomerName) ? "Customer" : dto.CustomerName,
            dto.CustomerPhone,
            $"£{dto.TotalAmount:0.00}",
            dto.KitchenPrinted,
            string.IsNullOrWhiteSpace(dto.Status)
                ? (dto.KitchenPrinted ? "Printed" : "Pending")
                : dto.Status);

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

    private async void OnBackdropTapped(object? sender, TappedEventArgs e) =>
        await CloseSidebarAsync();

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        await ClientSidebarNavigation.SwitchAsync(this, menu, currentRoute: "advanceorders");
    }
}
