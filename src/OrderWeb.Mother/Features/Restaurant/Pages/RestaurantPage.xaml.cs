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

        await ShowTabAsync(_selectedTab == default ? LayoutTab.Floor : _selectedTab);
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
            TabContentHost.Children.Clear();

            switch (tab)
            {
                case LayoutTab.Tables:
                    TopBar.SetPageTitle("Table Management");
                    EnsureTableHost();
                    AttachBody(_tableBody);
                    await _tablePage!.ActivateEmbeddedAsync();
                    break;
                case LayoutTab.Visual:
                    TopBar.SetPageTitle("Visual Layout");
                    EnsureVisualHost();
                    AttachBody(_visualBody);
                    await _visualPage!.ActivateEmbeddedAsync();
                    break;
                default:
                    TopBar.SetPageTitle("Floor Management");
                    EnsureFloorHost();
                    AttachBody(_floorBody);
                    await _floorPage!.ActivateEmbeddedAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Layout] ShowTab failed: {ex}");
            await AppAlertService.ShowAlertAsync("Layout", $"Could not open this tab: {ex.Message}");
        }
        finally
        {
            _isSwitchingTab = false;
        }
    }

    private void AttachBody(View? body)
    {
        if (body is null)
        {
            return;
        }

        if (body.Parent is Layout parentLayout)
        {
            parentLayout.Children.Remove(body);
        }
        else if (body.Parent is ContentView parentContent)
        {
            parentContent.Content = null;
        }

        body.VerticalOptions = LayoutOptions.Fill;
        body.HorizontalOptions = LayoutOptions.Fill;
        TabContentHost.Children.Add(body);
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
        _floorPage.Content = null;
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
        _tablePage.Content = null;
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
        _visualPage.Content = null;
        if (_visualBody is not null)
        {
            _visualBody.VerticalOptions = LayoutOptions.Fill;
            _visualBody.HorizontalOptions = LayoutOptions.Fill;
        }
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
