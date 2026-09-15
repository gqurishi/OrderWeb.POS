using Microsoft.Maui.Controls;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class RestaurantPage : ContentPage
{
    private enum LayoutTab
    {
        Floor,
        Tables,
        Visual
    }

    private LayoutTab _selectedTab = LayoutTab.Floor;
    private FloorPage? _floorPage;
    private TablePage? _tablePage;
    private VisualTablePage? _visualPage;
    private View? _floorBody;
    private View? _tableBody;
    private View? _visualBody;
    private bool _isSwitchingTab;

    public RestaurantPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Layout");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var auth = AuthenticationService.Instance;
        var roleAccess = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        if (!roleAccess.IsAdmin(auth.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only an Administrator can open Layout setup.");
            await NavigationCoordinator.Shared.NavigateShellAsync(
                roleAccess.ResolveDashboardRoute(auth.CurrentUser?.Role),
                animated: false);
            return;
        }

        await ShowTabAsync(LayoutTab.Floor);
    }

    protected override void OnDisappearing()
    {
        DeactivateAll();
        base.OnDisappearing();
    }

    private async void OnFloorTabTapped(object? sender, TappedEventArgs e) =>
        await ShowTabAsync(LayoutTab.Floor);

    private async void OnTableTabTapped(object? sender, TappedEventArgs e) =>
        await ShowTabAsync(LayoutTab.Tables);

    private async void OnVisualTabTapped(object? sender, TappedEventArgs e) =>
        await ShowTabAsync(LayoutTab.Visual);

    private async Task ShowTabAsync(LayoutTab tab)
    {
        if (_isSwitchingTab)
        {
            return;
        }

        _isSwitchingTab = true;
        try
        {
            DeactivateAll();
            _selectedTab = tab;
            StyleTabs();
            DetachHostContent();

            switch (tab)
            {
                case LayoutTab.Tables:
                    TopBar.SetPageTitle("Table Management");
                    EnsureTableHost();
                    TabContentHost.Content = _tableBody;
                    await _tablePage!.ActivateEmbeddedAsync();
                    break;
                case LayoutTab.Visual:
                    TopBar.SetPageTitle("Visual Layout");
                    EnsureVisualHost();
                    TabContentHost.Content = _visualBody;
                    await _visualPage!.ActivateEmbeddedAsync();
                    break;
                default:
                    TopBar.SetPageTitle("Floor Management");
                    EnsureFloorHost();
                    TabContentHost.Content = _floorBody;
                    await _floorPage!.ActivateEmbeddedAsync();
                    break;
            }
        }
        finally
        {
            _isSwitchingTab = false;
        }
    }

    private void EnsureFloorHost()
    {
        if (_floorPage is not null && _floorBody is not null)
        {
            return;
        }

        _floorPage = new FloorPage();
        _floorPage.PrepareForEmbed();
        _floorBody = _floorPage.Content;
    }

    private void EnsureTableHost()
    {
        if (_tablePage is not null && _tableBody is not null)
        {
            return;
        }

        _tablePage = new TablePage();
        _tablePage.PrepareForEmbed();
        _tableBody = _tablePage.Content;
    }

    private void EnsureVisualHost()
    {
        if (_visualPage is not null && _visualBody is not null)
        {
            return;
        }

        _visualPage = new VisualTablePage();
        _visualPage.PrepareForEmbed();
        _visualBody = _visualPage.Content;
    }

    private void DetachHostContent()
    {
        TabContentHost.Content = null;
    }

    private void DeactivateAll()
    {
        _floorPage?.DeactivateEmbedded();
        _tablePage?.DeactivateEmbedded();
        _visualPage?.DeactivateEmbedded();
    }

    private void StyleTabs()
    {
        StyleTab(FloorTab, FloorTabLabel, _selectedTab == LayoutTab.Floor);
        StyleTab(TableTab, TableTabLabel, _selectedTab == LayoutTab.Tables);
        StyleTab(VisualTab, VisualTabLabel, _selectedTab == LayoutTab.Visual);
    }

    private static void StyleTab(Border tab, Label label, bool selected)
    {
        if (selected)
        {
            tab.BackgroundColor = Color.FromArgb("#F1F5F9");
            tab.Stroke = Color.FromArgb("#94A3B8");
            label.TextColor = Color.FromArgb("#0F172A");
        }
        else
        {
            tab.BackgroundColor = Colors.White;
            tab.Stroke = Color.FromArgb("#E2E8F0");
            label.TextColor = Color.FromArgb("#64748B");
        }
    }
}
