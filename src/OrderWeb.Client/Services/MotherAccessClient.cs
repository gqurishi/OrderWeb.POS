using System.Net.Http.Headers;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

/// <summary>Refreshes Mother Terminal Access features/routes for the signed-in Client session.</summary>
public sealed class MotherAccessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherAccessClient() : this(new ClientCacheService()) { }

    public MotherAccessClient(ClientCacheService cache) => _cache = cache;

    public async Task<MotherAccessRefreshResult> RefreshAccessAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            session is null ||
            string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return MotherAccessRefreshResult.Fail("Sign in and pair with Mother POS to refresh access.");
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", settings.TerminalId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", session.SessionToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.GetAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/api/client/access",
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<AccessEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                return MotherAccessRefreshResult.Fail(
                    envelope?.Message ?? $"Mother could not refresh access ({(int)response.StatusCode}).");
            }

            var features = envelope.Features ?? Array.Empty<string>();
            var routes = envelope.Routes ?? Array.Empty<string>();
            var updated = session with { Features = features, Routes = routes };
            await _cache.SaveLoginSessionAsync(updated);
            ClientHostAccess.ApplyFromSession(updated);
            return MotherAccessRefreshResult.Ok(features, routes);
        }
        catch (Exception ex)
        {
            return MotherAccessRefreshResult.Fail($"Could not refresh access from Mother: {ex.Message}");
        }
    }

    private sealed record AccessEnvelope(
        bool Success,
        string? Message,
        IReadOnlyList<string>? Features,
        IReadOnlyList<string>? Routes);
}

public sealed record MotherAccessRefreshResult(
    bool Success,
    string Message,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Routes)
{
    public static MotherAccessRefreshResult Ok(IReadOnlyList<string> features, IReadOnlyList<string> routes) =>
        new(true, "Client access refreshed from Mother.", features, routes);

    public static MotherAccessRefreshResult Fail(string message) =>
        new(false, message, Array.Empty<string>(), Array.Empty<string>());
}
