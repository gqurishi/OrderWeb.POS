namespace OrderWeb.Client.Services;

/// <summary>
/// Red header button. Signs the till out and shows login — never just returns to the previous page.
/// </summary>
public static class ClientSignOut
{
    private static WeakReference<MainPage>? _main;
    private static bool _busy;
    private static bool _forceLogin;

    public static void Register(MainPage page) => _main = new WeakReference<MainPage>(page);

    /// <summary>True once after a header logout. MainPage must show login and skip sidebar resume.</summary>
    public static bool ConsumeForcedLogin()
    {
        if (!_forceLogin)
        {
            return false;
        }

        _forceLogin = false;
        return true;
    }

    public static async Task RequestAsync(Page host)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _forceLogin = true;
        ClientSidebarNavigation.ClearPendingRootRoute();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var navigation = host.Navigation ?? Shell.Current?.Navigation;
                var main = FindOnStack(navigation) ?? FindMainPage(host);
                main?.SignOut();
                await LeaveHostAsync(host, navigation);

                // Pop can reveal the till still on the dashboard. Paint login on the page that is actually visible.
                var visible = FindOnStack(navigation)
                    ?? Shell.Current?.CurrentPage as MainPage
                    ?? main
                    ?? FindMainPage(host);
                visible?.SignOut();
                // Login is on screen. Don't leave the flag set or a later appear would kick a new login.
                _forceLogin = false;
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task LeaveHostAsync(Page host, INavigation? navigation)
    {
        if (host is MainPage)
        {
            return;
        }

        navigation ??= host.Navigation ?? Shell.Current?.Navigation;
        if (navigation == null)
        {
            return;
        }

        try
        {
            if (navigation.ModalStack.Contains(host))
            {
                await navigation.PopModalAsync(false);
                return;
            }

            // Loyalty / Recent Customers are pushed on MainPage. PopToRoot shows the till that already signed out.
            // Do not GoToAsync("//MainPage") — that route is a DataTemplate and builds a new till.
            if (navigation.NavigationStack.Count > 1)
            {
                await navigation.PopToRootAsync(false);
                return;
            }

            if (navigation.NavigationStack.Count > 0 && navigation.NavigationStack[^1] == host)
            {
                await navigation.PopAsync(false);
            }
        }
        catch
        {
            // Login is painted on the till even if this page cannot leave the stack.
        }
    }

    private static MainPage? FindOnStack(INavigation? navigation)
    {
        if (navigation == null)
        {
            return null;
        }

        foreach (var page in navigation.NavigationStack)
        {
            if (page is MainPage stacked)
            {
                return stacked;
            }
        }

        return null;
    }

    private static MainPage? FindMainPage(Page host)
    {
        if (_main != null && _main.TryGetTarget(out var registered))
        {
            return registered;
        }

        var stacked = FindOnStack(host.Navigation);
        if (stacked != null)
        {
            return stacked;
        }

        if (Shell.Current?.CurrentPage is MainPage current)
        {
            return current;
        }

        stacked = FindOnStack(Shell.Current?.Navigation);
        if (stacked != null)
        {
            return stacked;
        }

        var windows = Application.Current?.Windows;
        if (windows == null)
        {
            return null;
        }

        foreach (var window in windows)
        {
            if (window.Page is MainPage windowMain)
            {
                return windowMain;
            }

            if (window.Page is Shell shell)
            {
                stacked = FindOnStack(shell.Navigation);
                if (stacked != null)
                {
                    return stacked;
                }
            }
        }

        return null;
    }
}
