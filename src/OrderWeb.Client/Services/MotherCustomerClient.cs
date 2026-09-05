using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Client.Models;
using OrderWeb.Client.Services.Customer;
using OrderWeb.Contracts.Customers;

namespace OrderWeb.Client.Services;

public sealed class MotherCustomerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientCacheService _cache;

    public MotherCustomerClient()
        : this(new ClientCacheService())
    {
    }

    public MotherCustomerClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<CustomerSummaryDto>> SearchCustomersAsync(
        CustomerSearchRequest request,
        CustomerFieldAccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return Array.Empty<CustomerSummaryDto>();
        }

        var query = new Dictionary<string, string?>
        {
            ["orderType"] = request.OrderType,
            ["name"] = request.Name,
            ["phone"] = request.Phone,
            ["addressOrPostcode"] = request.AddressOrPostcode
        };
        var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/customers/search", query);

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<CustomerSummaryDto>();
            }

            var envelope = JsonSerializer.Deserialize<CustomerApiEnvelope>(json, JsonOptions);
            return (envelope?.Customers ?? envelope?.Payload?.Customers ?? Array.Empty<CustomerState>())
                .Select(customer => ProjectSummary(ToCachedCustomer(customer), policy))
                .ToList();
        }
        catch
        {
            return Array.Empty<CustomerSummaryDto>();
        }
    }

    public async Task<CustomerFieldAccessPolicy?> FetchFieldAccessPolicyAsync(CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return null;
        }

        var endpoint = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/customers/field-access-policy";

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<FieldPolicyEnvelope>(json, JsonOptions);
            var payload = dto?.Policy ?? dto?.Payload?.Policy;
            return payload?.ToPolicy();
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AddressSuggestion>> LookupAddressesAsync(
        string addressOrPostcode,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null || string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            return Array.Empty<AddressSuggestion>();
        }

        var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/delivery-zones/lookup", new Dictionary<string, string?>
        {
            ["postcode"] = addressOrPostcode
        });

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<AddressSuggestion>();
            }

            var envelope = JsonSerializer.Deserialize<DeliveryApiEnvelope>(json, JsonOptions);
            return (envelope?.AddressSuggestions ?? envelope?.Payload?.AddressSuggestions ?? Array.Empty<AddressSuggestionState>())
                .Select(ToAddressSuggestion)
                .ToList();
        }
        catch
        {
            return Array.Empty<AddressSuggestion>();
        }
    }

    public async Task<CachedCustomer> SaveCustomerAsync(
        CustomerOrderDraft draft,
        CustomerFieldAccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var auth = await GetAuthAsync();
        if (auth is null)
        {
            return ProjectCached(LocalCustomer(draft), policy);
        }

        var endpoint = $"{auth.Settings.ApiBaseUrl.TrimEnd('/')}/api/client/customers/upsert";
        var body = new CustomerUpsertHttpRequest(
            Auth: ToAuthBody(auth),
            Name: draft.Customer.Name,
            PhoneNumber: draft.Customer.Phone,
            OrderTypes: draft.OrderType,
            FullAddress: draft.Address ?? draft.Customer.Address,
            City: null,
            County: null,
            Postcode: draft.Postcode ?? draft.Customer.Postcode,
            Email: draft.Customer.Email);

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.PostAsJsonAsync(endpoint, body, JsonOptions, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<CustomerApiEnvelope>(json, JsonOptions);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return ProjectCached(LocalCustomer(draft), policy);
                }

                throw new InvalidOperationException(envelope?.Message ?? $"Mother customer save failed with status {(int)response.StatusCode}.");
            }

            var customer = envelope.Customer ?? envelope.Payload?.Customer;
            var cached = customer is null ? LocalCustomer(draft) : ToCachedCustomer(customer);
            return ProjectCached(cached, policy);
        }
        catch (HttpRequestException)
        {
            return ProjectCached(LocalCustomer(draft), policy);
        }
        catch (TaskCanceledException)
        {
            return ProjectCached(LocalCustomer(draft), policy);
        }
        catch (JsonException)
        {
            return ProjectCached(LocalCustomer(draft), policy);
        }
    }

    public async Task<DeliveryZoneQuote> QuoteDeliveryZoneAsync(
        string postcode,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePostcode(postcode);
        var auth = await GetAuthAsync();
        if (auth is null || string.IsNullOrWhiteSpace(normalized))
        {
            return new DeliveryZoneQuote(null, normalized, 0m, false);
        }

        var endpoint = BuildUrl(auth.Settings.ApiBaseUrl, "/api/client/delivery-zones/quote", new Dictionary<string, string?>
        {
            ["postcode"] = normalized
        });

        try
        {
            using var client = CreateClient(auth);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new DeliveryZoneQuote(null, normalized, 0m, false);
            }

            var envelope = JsonSerializer.Deserialize<DeliveryApiEnvelope>(json, JsonOptions);
            var quote = envelope?.Quote ?? envelope?.Payload?.Quote;
            return quote is null
                ? new DeliveryZoneQuote(null, normalized, 0m, false)
                : new DeliveryZoneQuote(quote.DeliveryZoneName, quote.Postcode, quote.DeliveryFee, quote.IsDeliverable);
        }
        catch
        {
            return new DeliveryZoneQuote(null, normalized, 0m, false);
        }
    }

    public static string NormalizePostcode(string? postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return string.Empty;
        }

        var compact = new string(postcode.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Length <= 3 ? compact : $"{compact[..^3]} {compact[^3..]}";
    }

    private async Task<MotherClientAuth?> GetAuthAsync()
    {
        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            return null;
        }

        return new MotherClientAuth(settings, await _cache.GetCurrentLoginSessionAsync());
    }

    private static HttpClient CreateClient(MotherClientAuth auth)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", auth.Settings.TerminalId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Token", auth.Settings.TerminalToken);
        if (!string.IsNullOrWhiteSpace(auth.Session?.SessionToken))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Session-Token", auth.Session.SessionToken);
        }

        client.DefaultRequestHeaders.TryAddWithoutValidation("X-App-Version", AppInfo.VersionString);
        return client;
    }

    private static ClientAuthHttpBody ToAuthBody(MotherClientAuth auth) =>
        new(
            auth.Settings.TerminalId,
            auth.Settings.TerminalToken,
            auth.Session?.SessionToken ?? string.Empty,
            string.Empty,
            string.Empty,
            AppInfo.VersionString);

    private static string BuildUrl(string apiBaseUrl, string path, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var queryString = string.Join("&", parts);
        return $"{apiBaseUrl.TrimEnd('/')}{path}{(queryString.Length == 0 ? string.Empty : $"?{queryString}")}";
    }

    private static CachedCustomer LocalCustomer(CustomerOrderDraft draft) =>
        draft.Customer with
        {
            Id = draft.Customer.Id <= 0 ? Math.Abs(Guid.NewGuid().GetHashCode()) : draft.Customer.Id,
            MotherId = string.IsNullOrWhiteSpace(draft.Customer.MotherId) ? $"local-customer-{Guid.NewGuid():N}" : draft.Customer.MotherId,
            Address = draft.Address ?? draft.Customer.Address,
            Postcode = draft.Postcode ?? draft.Customer.Postcode
        };

    private static CachedCustomer ToCachedCustomer(CustomerState? customer)
    {
        if (customer is null)
        {
            return new CachedCustomer(0, string.Empty, string.Empty, string.Empty, null, string.Empty, null, 0);
        }

        return new CachedCustomer(
            customer.Id,
            customer.Id <= 0 ? customer.MotherId ?? string.Empty : customer.Id.ToString(),
            customer.Name ?? string.Empty,
            customer.PhoneNumber ?? customer.Phone ?? string.Empty,
            customer.Email,
            customer.FullAddress ?? customer.Address ?? string.Empty,
            customer.Postcode,
            customer.LoyaltyPoints);
    }

    private static AddressSuggestion ToAddressSuggestion(AddressSuggestionState address) =>
        new(
            address.DisplayText ?? string.Join(", ", new[] { address.AddressLine1, address.City, address.Postcode }.Where(part => !string.IsNullOrWhiteSpace(part))),
            address.AddressLine1 ?? string.Empty,
            address.AddressLine2 ?? string.Empty,
            address.AddressLine3 ?? string.Empty,
            address.City ?? string.Empty,
            address.County ?? string.Empty,
            address.Postcode ?? string.Empty,
            address.Country ?? "United Kingdom");

    private sealed record MotherClientAuth(MotherConnectionSettings Settings, LoginSession? Session);

    private sealed record ClientAuthHttpBody(
        [property: JsonPropertyName("terminalId")] string TerminalId,
        [property: JsonPropertyName("terminalToken")] string TerminalToken,
        [property: JsonPropertyName("sessionToken")] string SessionToken,
        [property: JsonPropertyName("restaurantSlug")] string RestaurantSlug,
        [property: JsonPropertyName("lastIpAddress")] string LastIpAddress,
        [property: JsonPropertyName("appVersion")] string AppVersion);

    private sealed record CustomerUpsertHttpRequest(
        [property: JsonPropertyName("auth")] ClientAuthHttpBody Auth,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
        [property: JsonPropertyName("orderTypes")] string? OrderTypes,
        [property: JsonPropertyName("fullAddress")] string? FullAddress,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("postcode")] string? Postcode,
        [property: JsonPropertyName("email")] string? Email);

    private static CustomerSummaryDto ProjectSummary(CachedCustomer customer, CustomerFieldAccessPolicy policy) =>
        CustomerFieldProjector.Project(
            ClientCustomerMapping.ToSummary(customer, ClientCustomerMapping.ParseOrderKind(null)),
            policy,
            CustomerFieldAccessScope.Search);

    private static CachedCustomer ProjectCached(CachedCustomer customer, CustomerFieldAccessPolicy policy)
    {
        var summary = CustomerFieldProjector.Project(
            ClientCustomerMapping.ToSummary(customer),
            policy,
            CustomerFieldAccessScope.Cache);

        return customer with
        {
            Name = summary.Name ?? string.Empty,
            Phone = summary.Phone ?? string.Empty,
            Email = summary.Email,
            Address = summary.Address ?? string.Empty,
            Postcode = summary.Postcode,
            LoyaltyPoints = summary.LoyaltyPoints ?? 0
        };
    }

    private sealed record FieldPolicyEnvelope(FieldPolicyState? Policy, FieldPolicyPayload? Payload);

    private sealed record FieldPolicyPayload(FieldPolicyState? Policy);

    private sealed record FieldPolicyState(
        IReadOnlyList<string>? SearchFields,
        IReadOnlyList<string>? CacheFields,
        IReadOnlyList<string>? DetailFields,
        bool AllowOrderHistory,
        bool AllowCustomerDirectory,
        bool AllowAssignCustomer)
    {
        public CustomerFieldAccessPolicy ToPolicy() =>
            new(
                ParseFields(SearchFields),
                ParseFields(CacheFields),
                ParseFields(DetailFields),
                AllowOrderHistory,
                AllowCustomerDirectory,
                AllowAssignCustomer);

        private static IReadOnlyList<CustomerFieldKind> ParseFields(IReadOnlyList<string>? values)
        {
            if (values is null || values.Count == 0)
            {
                return Array.Empty<CustomerFieldKind>();
            }

            var fields = new List<CustomerFieldKind>();
            foreach (var value in values)
            {
                if (Enum.TryParse<CustomerFieldKind>(value, true, out var field))
                {
                    fields.Add(field);
                }
            }

            return fields;
        }
    }

    private sealed record CustomerApiEnvelope(
        bool Success,
        string? Message,
        CustomerState? Customer,
        IReadOnlyList<CustomerState>? Customers,
        CustomerApiPayload? Payload);

    private sealed record CustomerApiPayload(CustomerState? Customer, IReadOnlyList<CustomerState>? Customers);

    private sealed record CustomerState(
        int Id,
        string? MotherId,
        string? Name,
        string? Phone,
        string? PhoneNumber,
        string? Email,
        string? FullAddress,
        string? Address,
        string? City,
        string? County,
        string? Postcode,
        int LoyaltyPoints);

    private sealed record DeliveryApiEnvelope(
        bool Success,
        string? Message,
        DeliveryQuoteState? Quote,
        IReadOnlyList<AddressSuggestionState>? AddressSuggestions,
        DeliveryApiPayload? Payload);

    private sealed record DeliveryApiPayload(DeliveryQuoteState? Quote, IReadOnlyList<AddressSuggestionState>? AddressSuggestions);

    private sealed record DeliveryQuoteState(
        string Postcode,
        bool IsDeliverable,
        string? DeliveryZoneName,
        decimal DeliveryFee);

    private sealed record AddressSuggestionState(
        string? DisplayText,
        string? AddressLine1,
        string? AddressLine2,
        string? AddressLine3,
        string? City,
        string? County,
        string? Postcode,
        string? Country);
}
