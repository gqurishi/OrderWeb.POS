using OrderWeb.SharedUI.Views;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

/// <summary>
/// Mother Manager/Admin Advance Orders host — local <see cref="AdvanceOrderService"/> only (no Client HTTP).
/// </summary>
public partial class AdvanceOrdersPage : ContentPage
{
    private const int PendingSoftNoteThreshold = 5;

    private readonly AdvanceOrderService _advance;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private bool _busy;

    public AdvanceOrdersPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Advance Orders");

        _advance = ServiceHelper.GetService<AdvanceOrderService>()
            ?? throw new InvalidOperationException("AdvanceOrderService not found");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;

        Board.RangeChanged += async (_, e) => await LoadAsync(e.Range);
        Board.RowTapped += async (_, e) => await OpenOrderAsync(e.Row.OrderId);
        Board.PrintKitchenRequested += async (_, e) => await PrintKitchenAsync(e.Row);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
        {
            return;
        }

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Advance Orders.");
            await _navigationCoordinator.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        await LoadAsync(Board.SelectedRange);
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await _navigationCoordinator.NavigateShellAsync(
            _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role),
            animated: false);
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
            var apiRange = AdvanceOrderSampleData.ApiRangeKey(range);
            var (from, to, _) = await _advance.ResolveRangeAsync(apiRange);
            var rows = await _advance.ListUpcomingAsync(from, to);
            var presentation = rows.Select(ToRow).ToList();
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

        try
        {
            var page = new OrderPlacementPageSimple(existingOrderId: orderId.Trim());
            await Navigation.PushAsync(page, false);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Advance Orders", ex.Message);
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
            var result = await _advance.PrintKitchenManualAsync(row.OrderId);
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

    private static AdvanceOrderRowPresentation ToRow(AdvanceOrderSummary row) =>
        new(
            row.OrderId,
            string.IsNullOrWhiteSpace(row.OrderNumber) ? "#" : row.OrderNumber!,
            AdvanceOrderService.DisplayOrderType(row.OrderType),
            AdvanceOrderService.FormatScheduledDisplay(row.ScheduledTime),
            string.IsNullOrWhiteSpace(row.CustomerName) ? "Customer" : row.CustomerName,
            row.CustomerPhone,
            $"£{row.TotalAmount:0.00}",
            row.AdvanceKitchenPrintedAt.HasValue,
            AdvanceOrderService.StatusLabel(row),
            row.IsFromWeb);
}
