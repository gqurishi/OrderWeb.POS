namespace POS_in_NET.Services;

/// <summary>
/// Bumps Client layout sync versions and notifies paired terminals after Mother
/// floor / table / floor-art edits. Clients re-fetch HTTP snapshots; events carry no payload.
/// </summary>
public static class ClientLayoutChangeNotifier
{
    public static void NotifyFloorsChanged()
    {
        _ = NotifyAsync("floors.updated");
    }

    public static void NotifyTablesChanged()
    {
        _ = NotifyAsync("tables.updated");
    }

    /// <summary>Floor create/delete/rename or art changes that also affect table layout snapshots.</summary>
    public static void NotifyLayoutChanged()
    {
        NotifyFloorsChanged();
        NotifyTablesChanged();
    }

    private static async Task NotifyAsync(string eventType)
    {
        try
        {
            var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
            if (broadcast == null)
            {
                return;
            }

            await broadcast.PublishDataChangedAsync(eventType, string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientLayout] {eventType} notify failed: {ex.Message}");
        }
    }
}
