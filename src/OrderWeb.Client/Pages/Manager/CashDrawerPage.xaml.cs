namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

public partial class CashDrawerPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherPrintClient _printClient = new();
    private LoginSession? _session;

    public CashDrawerPage()
    {
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
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
        SetStatus("Syncing", "#EFF6FF", "#2563EB");

        try
        {
            var request = await _printClient.RequestPrintAsync("cash drawer open", null, _session);
            await _cache.SavePrintRequestAsync(request);

            if (request.Status is "queued")
            {
                request = await _printClient.AdvanceStatusAsync(request);
                await _cache.SavePrintRequestAsync(request);
                request = await _printClient.AdvanceStatusAsync(request);
                await _cache.SavePrintRequestAsync(request);
            }
            else if (request.Status is "printer offline")
            {
                request = await _printClient.AdvanceStatusAsync(request);
                await _cache.SavePrintRequestAsync(request);
            }

            SetStatus(request.Status, BadgeBackground(request.Status), BadgeText(request.Status));
        }
        catch (Exception ex)
        {
            SetStatus("Failed", "#FEE2E2", "#DC2626");
            await DisplayAlert("Cash Drawer Failed", ex.Message, "OK");
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
        if (ClientSidebarNavigation.IsDashboard(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (await ClientSidebarNavigation.TryHandleMotherOnlyAsync(this, menu))
        {
            return;
        }

        if (ClientHostAccess.IsMenuRoute(menu, "cashdrawer") ||
            !ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        if (ClientSidebarNavigation.IsCustomerSurface(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        var page = ClientSidebarNavigation.CreatePage(menu);
        if (page is not null)
        {
            await Navigation.PushAsync(page, false);
        }
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
