namespace OrderWeb.Client.Services;

/// <summary>
/// Mother opens Collection/Delivery as temporary routes that slide in from the right.
/// Client uses PushAsync (no platform transition) plus page-owned TranslationX for the same side slide.
/// </summary>
public static class ClientSideNavigation
{
    public static async Task PushFromSideAsync(INavigation navigation, Page page)
    {
        if (page is Pages.Orders.CollectionOrderPage or Pages.Orders.DeliveryOrderPage)
        {
            if (page is Pages.Orders.CollectionOrderPage &&
                IsPageAlreadyVisible<Pages.Orders.CollectionOrderPage>())
            {
                return;
            }

            if (page is Pages.Orders.DeliveryOrderPage &&
                IsPageAlreadyVisible<Pages.Orders.DeliveryOrderPage>())
            {
                return;
            }

            // animated:false — the page runs its own right-to-left TranslationX enter.
            await navigation.PushAsync(page, animated: false);
            return;
        }

        await navigation.PushAsync(page, animated: true);
    }

    public static async Task PopFromSideAsync(INavigation navigation)
    {
        // Page already ran exit TranslationX; pop without a second platform animation.
        await navigation.PopAsync(animated: false);
    }

    private static bool IsPageAlreadyVisible<TPage>() where TPage : Page
    {
        var current = Shell.Current?.CurrentPage;
        if (current is TPage)
        {
            return true;
        }

        if (current?.Navigation?.NavigationStack.LastOrDefault() is TPage)
        {
            return true;
        }

        var nav = current?.Navigation ?? Shell.Current?.Navigation;
        return nav?.NavigationStack.LastOrDefault() is TPage
            || nav?.ModalStack.LastOrDefault() is TPage;
    }
}
