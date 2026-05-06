using POS_in_NET.Models;
using System.Text.Json;

namespace POS_in_NET.Services;

/// <summary>
/// Custom PAF (Postcode Address File) implementation
/// For future use when you build your own Royal Mail PAF database
/// </summary>
public class CustomPAFService : IAddressLookupService
{
    private readonly string _apiUrl;
    private readonly string _authToken;
    private readonly HttpClient _httpClient;

    public string ProviderName => "Custom PAF Database";

    public CustomPAFService(string apiUrl, string authToken)
    {
        _apiUrl = apiUrl?.TrimEnd('/') ?? string.Empty;
        _authToken = authToken ?? string.Empty;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (!string.IsNullOrEmpty(_authToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");
        }
    }

    public async Task<List<AddressResult>> LookupPostcodeAsync(string postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return new List<AddressResult>();

        if (string.IsNullOrWhiteSpace(_apiUrl))
            throw new InvalidOperationException("Custom API URL not configured");

        try
        {
            // Your custom API endpoint structure
            // Adjust this when you build your PAF API
            var url = $"{_apiUrl}/api/postcode/lookup?postcode={Uri.EscapeDataString(postcode)}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            // TODO: Parse your custom API response
            // This is a placeholder - implement based on your API structure
            var json = await response.Content.ReadAsStringAsync();
            var addresses = JsonSerializer.Deserialize<List<AddressResult>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return addresses ?? new List<AddressResult>();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Custom PAF] Network error: {ex.Message}");
            throw new Exception($"Failed to connect to custom PAF API: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Custom PAF] Error: {ex.Message}");
            throw new Exception($"Address lookup failed: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_apiUrl))
                return false;

            // Test endpoint health
            var url = $"{_apiUrl}/health";
            var response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
