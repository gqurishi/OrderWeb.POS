using OrderWeb.Contracts.Customers;

namespace OrderWeb.Client.Services.Customer;

public sealed class ClientMotherSyncStatusService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HealthCacheDuration = TimeSpan.FromSeconds(30);

    private readonly ClientCacheService _cache;
    private bool? _lastOnline;
    private DateTimeOffset _lastHealthCheckUtc = DateTimeOffset.MinValue;

    public ClientMotherSyncStatusService(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<CustomerSyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId))
        {
            return new CustomerSyncStatusDto(
                IsOnline: false,
                IsStale: true,
                LastSyncedAtUtc: await GetLastSyncedAtUtcAsync(cancellationToken),
                DisplayText: "Offline — not connected to Mother POS");
        }

        var isOnline = await IsMotherReachableAsync(settings.ApiBaseUrl, cancellationToken);
        var lastSynced = await GetLastSyncedAtUtcAsync(cancellationToken);
        var isStale = !isOnline || IsStale(lastSynced);

        return new CustomerSyncStatusDto(
            IsOnline: isOnline,
            IsStale: isStale,
            LastSyncedAtUtc: lastSynced,
            DisplayText: !isOnline
                ? "Offline — showing cached data"
                : isStale
                    ? "Data may be stale — Mother POS unreachable"
                    : "Live");
    }

    public async Task<bool> IsMotherOnlineAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null || string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            return false;
        }

        return await IsMotherReachableAsync(settings.ApiBaseUrl, cancellationToken);
    }

    private async Task<bool> IsMotherReachableAsync(string apiBaseUrl, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _lastHealthCheckUtc < HealthCacheDuration && _lastOnline.HasValue)
        {
            return _lastOnline.Value;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync($"{apiBaseUrl.TrimEnd('/')}/health", cancellationToken);
            _lastOnline = response.IsSuccessStatusCode;
        }
        catch
        {
            _lastOnline = false;
        }

        _lastHealthCheckUtc = DateTimeOffset.UtcNow;
        return _lastOnline ?? false;
    }

    private async Task<DateTimeOffset?> GetLastSyncedAtUtcAsync(CancellationToken cancellationToken)
    {
        var lastSync = await _cache.GetSyncValueAsync("last_sync_time", cancellationToken);
        return DateTimeOffset.TryParse(lastSync, out var parsed) ? parsed : null;
    }

    private static bool IsStale(DateTimeOffset? lastSyncedUtc) =>
        lastSyncedUtc is null || DateTimeOffset.UtcNow - lastSyncedUtc.Value > StaleThreshold;
}
