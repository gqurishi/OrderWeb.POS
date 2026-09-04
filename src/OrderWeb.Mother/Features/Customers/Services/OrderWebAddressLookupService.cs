using System.Net.Http.Json;
using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// UK postcode address lookup via OrderWeb platform API (shared owp_ key).
/// </summary>
public sealed class OrderWebAddressLookupService : IAddressLookupService
{
    public const string DefaultBaseUrl = "https://orderweb.net";
    public const string ApiKeyHeaderName = "X-OrderWebLTD-Api-Key";

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    public string ProviderName => "OrderWeb";

    public OrderWebAddressLookupService(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<List<AddressResult>> LookupPostcodeAsync(string postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return new List<AddressResult>();
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OrderWeb address API key (owp_...) is not configured.");
        }

        var url = $"{_baseUrl}/api/pos/address/postcode?postcode={Uri.EscapeDataString(postcode.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, _apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AddressLookupException($"Could not reach OrderWeb address service: {ex.Message}");
        }

        var json = await response.Content.ReadAsStringAsync();
        OrderWebAddressLookupResponse? data;
        try
        {
            data = JsonSerializer.Deserialize<OrderWebAddressLookupResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new AddressLookupException("OrderWeb returned an unexpected response.");
        }

        if (data == null)
        {
            throw new AddressLookupException("OrderWeb returned an empty response.");
        }

        if (!response.IsSuccessStatusCode || !data.Success)
        {
            var message = data.Message
                ?? data.Error
                ?? $"Address lookup failed ({(int)response.StatusCode}).";

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                message = "Invalid OrderWeb address API key. Check the owp_ key in Settings.";
            }
            else if (string.Equals(data.Error, "address_lookup_disabled", StringComparison.OrdinalIgnoreCase))
            {
                message = "Address lookup is disabled in OrderWeb admin.";
            }

            throw new AddressLookupException(message, data.Suggestions, data.Error);
        }

        return data.Addresses
            .Select(MapAddress)
            .Where(address => !string.IsNullOrWhiteSpace(address.FormattedAddress))
            .ToList();
    }

    public async Task<bool> TestConnectionAsync()
    {
        var results = await LookupPostcodeAsync("SW1A2AA");
        return results.Count > 0;
    }

    private static AddressResult MapAddress(OrderWebAddressItem item)
    {
        var formatted = string.IsNullOrWhiteSpace(item.Formatted)
            ? BuildFormatted(item)
            : item.Formatted.Trim();

        return new AddressResult
        {
            FormattedAddress = formatted,
            AddressLine1 = item.Line1?.Trim() ?? string.Empty,
            AddressLine2 = item.Line2?.Trim() ?? string.Empty,
            AddressLine3 = item.Line3?.Trim() ?? string.Empty,
            City = item.PostTown?.Trim() ?? string.Empty,
            County = item.County?.Trim() ?? string.Empty,
            Postcode = item.Postcode?.Trim() ?? string.Empty,
            Country = string.IsNullOrWhiteSpace(item.Country) ? "GB" : item.Country.Trim(),
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            Uprn = item.Uprn,
            Udprn = item.Udprn
        };
    }

    private static string BuildFormatted(OrderWebAddressItem item)
    {
        var parts = new[] { item.Line1, item.Line2, item.Line3, item.PostTown, item.Postcode }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());
        return string.Join(", ", parts);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
