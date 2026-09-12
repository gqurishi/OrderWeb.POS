namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

public partial class CashDrawerPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private LoginSession? _session;

    public CashDrawerPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
        Sidebar.UpdateAllClicked += async (_, _) => await UpdateAllAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _session = await _cache.GetCurrentLoginSessionAsync();
        await RefreshActivityAsync();
    }

    private async void OnOpenDrawerClicked(object sender, EventArgs e)
    {
        if (_session?.HasPermission("client.cash_drawer.open") != true)
        {
            SetStatus("No Permission", "#FEE2E2", "#DC2626");
            await DisplayAlert("Cash Drawer", "This user cannot open the cash drawer from Client POS.", "OK");
            return;
        }

        OpenDrawerButton.IsEnabled = false;
        OpenDrawerButton.Text = "Opening...";

        try
        {
            var choice = await CashDrawerDialogFlow.CollectAsync(this);
            if (choice is null)
            {
                return;
            }

            if (choice.Kind == CashDrawerUiKind.ShoppingSettle)
            {
                var settled = await ClientCashDrawerOpen.SettleShoppingAsync(this, _cache);
                if (settled is null)
                {
                    return;
                }

                await CashDrawerDialogFlow.ShowNoticeAsync(
                    this,
                    settled.Success ? "Cash Drawer" : "Cash Drawer Failed",
                    settled.Message,
                    settled.Success ? "OK" : "!",
                    settled.Success ? "#10B981" : "#EF4444");
                return;
            }

            var online = await new ClientOfflinePolicy().IsMotherOnlineAsync();
            if (!online)
            {
                await CashDrawerDialogFlow.ShowNoticeAsync(this, "Cash Drawer Failed", "Cash drawer opening requires a live Mother POS connection.", "!", "#EF4444");
                return;
            }

            var open = ClientCashDrawerOpen.From(choice);
            SetStatus("Syncing", "#EFF6FF", "#2563EB");
            if (string.Equals(_session?.Role, "Cashier", StringComparison.OrdinalIgnoreCase))
            {
                var cashier = await new MotherCashierClient(_cache).OpenCashDrawerAsync(open.Reason, open.Amount, open.Details);
                await CashDrawerDialogFlow.ShowNoticeAsync(
                    this,
                    cashier.Success ? "Cash Drawer" : "Cash Drawer Failed",
                    cashier.Message,
                    cashier.Success ? "OK" : "!",
                    cashier.Success ? "#10B981" : "#EF4444");
                SetStatus(cashier.Success ? "printed" : "failed", BadgeBackground(cashier.Success ? "printed" : "failed"), BadgeText(cashier.Success ? "printed" : "failed"));
            }
            else
            {
                var drawer = await new MotherOrderClient().OpenOrderPlaceCashDrawerAsync(null, open.DrawerReason);
                await CashDrawerDialogFlow.ShowNoticeAsync(
                    this,
                    drawer.Success ? "Cash Drawer" : "Cash Drawer Failed",
                    drawer.Message,
                    drawer.Success ? "OK" : "!",
                    drawer.Success ? "#10B981" : "#EF4444");
                SetStatus(drawer.Success ? "printed" : "failed", BadgeBackground(drawer.Success ? "printed" : "failed"), BadgeText(drawer.Success ? "printed" : "failed"));
            }
        }
        catch (Exception ex)
        {
            SetStatus("Failed", "#FEE2E2", "#DC2626");
            await CashDrawerDialogFlow.ShowNoticeAsync(this, "Cash Drawer Failed", ex.Message, "!", "#EF4444");
        }
        finally
        {
            OpenDrawerButton.Text = "Open Cash Drawer";
            OpenDrawerButton.IsEnabled = true;
            await RefreshActivityAsync();
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await RefreshActivityAsync();
    }

    private async Task RefreshActivityAsync()
    {
        _session ??= await _cache.GetCurrentLoginSessionAsync();
        var canOpen = _session?.HasPermission("client.cash_drawer.open") == true;
        OpenDrawerButton.IsEnabled = canOpen;
        OpenDrawerButton.BackgroundColor = Color.FromArgb(canOpen ? "#10B981" : "#94A3B8");

        var rows = (await _cache.GetRecentPrintRequestsAsync())
            .Where(request => request.PrintType.Contains("cash drawer", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(ActivityRow.From)
            .ToList();

        ActivityCollection.ItemsSource = rows;
        EmptyActivityLabel.IsVisible = rows.Count == 0;

        var latest = rows.FirstOrDefault();
        if (!canOpen)
        {
            SetStatus("No Permission", "#FEE2E2", "#DC2626");
        }
        else if (latest is null)
        {
            SetStatus("Ready", "#ECFDF5", "#059669");
        }
        else
        {
            SetStatus(latest.Status, latest.BadgeBackground, latest.BadgeText);
        }
    }

    private void SetStatus(string status, string backgroundColor, string textColor)
    {
        StatusLabel.Text = status;
        StatusLabel.TextColor = Color.FromArgb(textColor);
        StatusPill.BackgroundColor = Color.FromArgb(backgroundColor);
        ConnectionLabel.Text = status is "Ready" or "printed" ? "Connected" : "Mother POS request status";
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
        await ClientSidebarNavigation.SwitchAsync(this, menu, currentRoute: "cashdrawer");
    }

    private async Task UpdateAllAsync()
    {
        await CloseSidebarAsync();
        await RefreshActivityAsync();
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private static string BadgeBackground(string status)
    {
        return status switch
        {
            "printed" => "#ECFDF5",
            "printing" => "#EFF6FF",
            "queued" => "#FEF3C7",
            "printer offline" or "failed" => "#FEE2E2",
            _ => "#F1F5F9"
        };
    }

    private static string BadgeText(string status)
    {
        return status switch
        {
            "printed" => "#059669",
            "printing" => "#2563EB",
            "queued" => "#B45309",
            "printer offline" or "failed" => "#DC2626",
            _ => "#64748B"
        };
    }

    private sealed record ActivityRow(
        string TimeText,
        string Message,
        string Status,
        string BadgeBackground,
        string BadgeText)
    {
        public static ActivityRow From(PrintRequestState request)
        {
            var timeText = DateTimeOffset.TryParse(request.UpdatedUtc, out var timestamp)
                ? timestamp.ToLocalTime().ToString("HH:mm:ss · dd MMM")
                : "Pending";

            return new ActivityRow(
                timeText,
                request.Message,
                request.Status,
                CashDrawerPage.BadgeBackground(request.Status),
                CashDrawerPage.BadgeText(request.Status));
        }
    }
}
