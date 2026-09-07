using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherReservationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherReservationClient()
        : this(new ClientCacheService())
    {
    }

    public MotherReservationClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<MotherReservationDto>?> GetReservationsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return null;
        }

        var query = $"?from={Uri.EscapeDataString(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}";
        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync($"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/reservations{query}", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<ReservationListEnvelope>(json, JsonOptions);
            if (envelope is null || !envelope.Success)
            {
                return null;
            }

            var reservations = envelope.Reservations ?? [];
            await _cache.ReplaceReservationsAsync(reservations.Select(ToCached).ToList());
            return reservations;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MotherReservationCommandResult> CreateAsync(MotherReservationDraft draft)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return MotherReservationCommandResult.Fail("Client POS is not connected to Mother POS.");
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/reservations",
                new
                {
                    reservationDate = draft.ReservationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reservationTime = DateTime.Today.Add(draft.ReservationTime).ToString("HH:mm", CultureInfo.InvariantCulture),
                    covers = draft.Covers,
                    customerName = draft.CustomerName,
                    customerPhone = draft.CustomerPhone,
                    customerEmail = draft.CustomerEmail,
                    promoCode = draft.PromoCode,
                    notes = draft.Notes,
                    allergies = draft.Allergies,
                    tableNumber = draft.TableNumber,
                    channel = string.IsNullOrWhiteSpace(draft.Channel) ? "pos" : draft.Channel
                },
                JsonOptions);
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<ReservationMutationEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                return MotherReservationCommandResult.Fail(envelope?.Message ?? "Mother POS could not save this booking.");
            }

            return MotherReservationCommandResult.Ok(envelope.Message ?? "Booking saved on Mother POS.", envelope.Reservation);
        }
        catch (Exception ex)
        {
            return MotherReservationCommandResult.Fail($"Could not reach Mother POS: {ex.Message}");
        }
    }

    public async Task<MotherReservationCommandResult> UpdateStatusAsync(string? cloudId, string? localId, string status)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return MotherReservationCommandResult.Fail("Client POS is not connected to Mother POS.");
        }

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(
                $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/reservations/status",
                new
                {
                    cloudId,
                    localId,
                    status
                },
                JsonOptions);
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<ReservationMutationEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                return MotherReservationCommandResult.Fail(envelope?.Message ?? "Mother POS could not update this booking.");
            }

            return MotherReservationCommandResult.Ok(envelope.Message ?? "Reservation updated.");
        }
        catch (Exception ex)
        {
            return MotherReservationCommandResult.Fail($"Could not reach Mother POS: {ex.Message}");
        }
    }

    public async Task<MotherReservationCommandResult> SyncDateAsync(DateTime date)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return MotherReservationCommandResult.Fail("Client POS is not connected to Mother POS.");
        }

        try
        {
            using var client = CreateClient(auth);
            var query = $"?date={Uri.EscapeDataString(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}";
            using var response = await client.PostAsync($"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/reservations/sync{query}", null);
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<ReservationListEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode || envelope is null)
            {
                return MotherReservationCommandResult.Fail(envelope?.Message ?? "Mother POS could not sync website reservations.");
            }

            var reservations = envelope.Reservations ?? [];
            await _cache.ReplaceReservationsAsync(reservations.Select(ToCached).ToList());
            return new MotherReservationCommandResult(envelope.Success, envelope.Message ?? string.Empty, null, reservations);
        }
        catch (Exception ex)
        {
            return MotherReservationCommandResult.Fail($"Could not reach Mother POS: {ex.Message}");
        }
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        var session = await _cache.GetCurrentLoginSessionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken) ||
            string.IsNullOrWhiteSpace(session?.SessionToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, session);
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ClientCompatibilityHeaders.Apply(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static CachedReservation ToCached(MotherReservationDto reservation)
    {
        var dateTime = CombineDateTime(reservation.Date, reservation.Time);
        return new CachedReservation(
            string.IsNullOrWhiteSpace(reservation.CloudId) ? reservation.LocalId ?? Guid.NewGuid().ToString("N") : reservation.CloudId,
            reservation.CloudId,
            reservation.CustomerName,
            reservation.CustomerPhone,
            null,
            string.IsNullOrWhiteSpace(reservation.TableNumber) ? null : reservation.TableNumber,
            reservation.Covers,
            dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            reservation.Status,
            JsonSerializer.Serialize(reservation, JsonOptions),
            string.IsNullOrWhiteSpace(reservation.UpdatedUtc) ? DateTimeOffset.UtcNow.ToString("O") : reservation.UpdatedUtc);
    }

    public static DateTime CombineDateTime(string? date, string? time)
    {
        if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            parsedDate = DateTime.Today;
        }

        if (!TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var parsedTime) &&
            DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDateTime))
        {
            parsedTime = parsedDateTime.TimeOfDay;
        }

        return parsedDate.Date.Add(parsedTime);
    }

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession Session);

    private sealed record ReservationListEnvelope(
        bool Success,
        string? Message,
        IReadOnlyList<MotherReservationDto>? Reservations);

    private sealed record ReservationMutationEnvelope(
        bool Success,
        string? Message,
        MotherReservationDto? Reservation);
}

public sealed record MotherReservationDto(
    string CloudId,
    string? LocalId,
    string Reference,
    string Date,
    string Time,
    int Covers,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string PromoCode,
    string Notes,
    string Allergies,
    string Status,
    string Source,
    string TableNumber,
    bool PendingUpload,
    string UpdatedUtc);

public sealed class MotherReservationDraft
{
    public DateTime ReservationDate { get; set; }
    public TimeSpan ReservationTime { get; set; }
    public int Covers { get; set; } = 2;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string PromoCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Allergies { get; set; } = string.Empty;
    public string TableNumber { get; set; } = string.Empty;
    public string Channel { get; set; } = "pos";
}

public sealed record MotherReservationCommandResult(
    bool Success,
    string Message,
    MotherReservationDto? Reservation,
    IReadOnlyList<MotherReservationDto>? Reservations = null)
{
    public static MotherReservationCommandResult Ok(string message, MotherReservationDto? reservation = null) =>
        new(true, message, reservation);

    public static MotherReservationCommandResult Fail(string message) =>
        new(false, message, null);
}
