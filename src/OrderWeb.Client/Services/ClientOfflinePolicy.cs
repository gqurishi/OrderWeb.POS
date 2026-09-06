namespace OrderWeb.Client.Services;

public enum ClientOperation
{
    ViewCachedMenu, ViewCachedFloor, CreateUnsentDraft, SubmitFinalOrder,
    CardPayment, Refund, ChangePermissions, UpdateMenu, ViewCachedOpenOrders
}

public sealed record OfflineDecision(bool Allowed, bool RequiresMother, bool DataMayBeStale, string Message);

/// <summary>Single, explicit offline policy for Client POS operations.</summary>
public sealed class ClientOfflinePolicy
{
    private readonly ClientCacheService _cache;
    public ClientOfflinePolicy() : this(new ClientCacheService()) { }
    public ClientOfflinePolicy(ClientCacheService cache) => _cache = cache;

    public OfflineDecision Evaluate(ClientOperation operation, bool motherOnline) => operation switch
    {
        ClientOperation.ViewCachedMenu => new(true, false, !motherOnline, motherOnline ? "Mother online" : "Mother offline — cached menu may be outdated."),
        ClientOperation.ViewCachedFloor => new(true, false, !motherOnline, motherOnline ? "Mother online" : "Mother offline — cached floor may be outdated."),
        ClientOperation.CreateUnsentDraft => new(true, false, !motherOnline, motherOnline ? "Draft available" : "Offline draft only — it has not been sent to Mother."),
        ClientOperation.ViewCachedOpenOrders => new(true, false, !motherOnline, motherOnline ? "Open orders current" : "Mother offline — open orders may be outdated."),
        ClientOperation.SubmitFinalOrder => OnlineOnly(motherOnline, "Final orders require Mother confirmation. No order was submitted."),
        ClientOperation.CardPayment => OnlineOnly(motherOnline, "Card payment requires Mother and the payment provider. No payment was taken."),
        ClientOperation.Refund => OnlineOnly(motherOnline, "Refunds require Mother approval. No refund was created."),
        ClientOperation.ChangePermissions => OnlineOnly(motherOnline, "Permission changes require Mother POS."),
        ClientOperation.UpdateMenu => OnlineOnly(motherOnline, "Menu updates require Mother POS."),
        _ => OnlineOnly(motherOnline, "This action requires Mother POS.")
    };

    public async Task<bool> IsMotherOnlineAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings == null || string.IsNullOrWhiteSpace(settings.ApiBaseUrl) || string.IsNullOrWhiteSpace(settings.TerminalToken)) return false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            using var response = await client.GetAsync($"{settings.ApiBaseUrl.TrimEnd('/')}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private static OfflineDecision OnlineOnly(bool online, string offlineMessage) =>
        online ? new(true, true, false, "Mother online") : new(false, true, true, offlineMessage);
}
