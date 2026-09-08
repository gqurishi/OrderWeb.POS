namespace OrderWeb.Client.Services;

/// <summary>
/// Mother temporary routes have no system back chrome. Client pages must hide the
/// Shell/Windows title-bar back arrow that appears after PushAsync.
/// </summary>
public static class ClientPageChrome
{
    public static void HideSystemBackChrome(Page page)
    {
        Shell.SetNavBarIsVisible(page, false);
        Shell.SetFlyoutBehavior(page, FlyoutBehavior.Disabled);
        NavigationPage.SetHasNavigationBar(page, false);
        NavigationPage.SetHasBackButton(page, false);
        Shell.SetBackButtonBehavior(page, new BackButtonBehavior
        {
            IsVisible = false,
            IsEnabled = false
        });

#if WINDOWS
        ClientWindowService.SuppressWindowsTitleBackArrow();
#endif
    }
}
