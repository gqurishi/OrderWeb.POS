using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client → Mother staff clock. Same shift ledger as Mother Staff Clock. No local clock rows.
/// </summary>
public sealed class MotherTimeClockClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherTimeClockClient()
        : this(new ClientCacheService())
    {
    }

    public MotherTimeClockClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public Task<ClientTimeClockResponseDto> StatusAsync(string pin, CancellationToken cancellationToken = default) =>
        PostAsync("/api/client/time-clock/status", pin, cancellationToken);

    public Task<ClientTimeClockResponseDto> ClockInAsync(string pin, CancellationToken cancellationToken = default) =>
        PostAsync("/api/client/time-clock/clock-in", pin, cancellationToken);

    public Task<ClientTimeClockResponseDto> ClockOutAsync(string pin, CancellationToken cancellationToken = default) =>
        PostAsync("/api/client/time-clock/clock-out", pin, cancellationToken);

    private async Task<ClientTimeClockResponseDto> PostAsync(string path, string pin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return Fail("Enter a 4-digit PIN.", TimeClockErrorCodes.Validation);
        }

        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            return Fail("Client POS is not paired with Mother POS.", TimeClockErrorCodes.OfflineMother);
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            ClientCompatibilityHeaders.Apply(client);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", settings.TerminalToken);
            using var response = await client.PostAsJsonAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}{path}",
                new ClientTimeClockRequestDto(pin.Trim(), settings.TerminalToken),
                JsonOptions,
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<ClientTimeClockResponseDto>(JsonOptions, cancellationToken);
            if (body != null)
            {
                return body;
            }

            return Fail(
                response.IsSuccessStatusCode
                    ? "Mother POS did not return a clock result."
                    : "Could not reach Mother POS. No clock time was saved.",
                TimeClockErrorCodes.OfflineMother);
        }
        catch (Exception)
        {
            return Fail("Mother POS is offline. No clock time was saved.", TimeClockErrorCodes.OfflineMother);
        }
    }

    private static ClientTimeClockResponseDto Fail(string message, string errorCode) =>
        new(false, message, message, errorCode);
}
