namespace OrderWeb.Client.Services;

/// <summary>
/// Red header button. Signs the till out and shows login — never just returns to the dashboard.
/// </summary>
public static class ClientSignOut
{
    public static async Task RequestAsync(Page host)
    {
        var navigation = host.Navigation;
        if (navigation.NavigationStack.OfType<MainPage>().FirstOrDefault() is MainPage main)
        {
            main.SignOut();
            if (navigation.NavigationStack.Count > 1)
            {
                await navigation.PopToRootAsync(false);
            }

            return;
        }

        await new ClientCacheService().ClearLoginSessionAsync();
        ClientHostAccess.Clear();
        if (navigation.NavigationStack.Count > 1)
        {
            await navigation.PopToRootAsync(false);
        }
    }
}
