namespace POS_in_NET.Services;

/// <summary>
/// Bumps Client menu sync version and notifies paired terminals after Mother menu edits.
/// </summary>
public static class ClientMenuChangeNotifier
{
    public static void NotifyMenuChanged()
    {
        _ = NotifyAsync();
    }

    private static async Task NotifyAsync()
    {
        try
        {
            var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
            if (broadcast == null)
            {
                return;
            }

            await broadcast.PublishDataChangedAsync("menu.updated", string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientMenu] menu.updated notify failed: {ex.Message}");
        }
    }
}
