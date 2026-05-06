using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mapbox Geocoding API implementation for UK address lookup
/// Uses Mapbox's free tier: 100,000 requests/month
/// </summary>
public class MapboxAddressService : IAddressLookupService
{
    private readonly string _apiToken;
    private readonly HttpClient _httpClient;
    private const string MAPBOX_API_BASE = "https://api.mapbox.com/search/geocode/v6/forward";

    public string ProviderName => "Mapbox";

    public MapboxAddressService(string apiToken)
    {
        _apiToken = apiToken;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<List<AddressResult>> LookupPostcodeAsync(string postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return new List<AddressResult>();

        if (string.IsNullOrWhiteSpace(_apiToken))
            throw new InvalidOperationException("Mapbox API token not configured");

        try
        {
            // Clean postcode (remove extra spaces)
            postcode = postcode.Trim().ToUpper();

            // Mapbox Geocoding v6 API endpoint
            // country=gb restricts to UK only
            // types=address prioritizes full addresses
            // language=en ensures English results
            var url = $"{MAPBOX_API_BASE}" +
                      $"?q={Uri.EscapeDataString(postcode)}" +
                      $"&access_token={_apiToken}" +
                      $"&country=gb" +
                      $"&types=address,place,postcode" +
                      $"&language=en" +
                      $"&limit=10";

            System.Diagnostics.Debug.WriteLine($"[Mapbox] Calling API v6: {MAPBOX_API_BASE}");
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Searching for postcode: {postcode}");
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Full URL: {url.Replace(_apiToken, "***TOKEN***")}");
            
            var response = await _httpClient.GetAsync(url);
            System.Diagnostics.Debug.WriteLine($"[Mapbox] HTTP Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[Mapbox] API Error ({response.StatusCode}): {errorContent}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception("Invalid API token. Please check your Mapbox access token.");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new Exception("Token doesn't have geocoding permissions. Please create a new secret token.");
                }
                else
                {
                    throw new Exception($"API returned error {response.StatusCode}: {errorContent}");
                }
            }

            var json = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Response length: {json.Length} chars");
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Response preview: {json.Substring(0, Math.Min(200, json.Length))}...");
            
            var result = JsonSerializer.Deserialize<MapboxResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            System.Diagnostics.Debug.WriteLine($"[Mapbox] Deserialized - Features: {result?.Features?.Count ?? 0}");

            if (result?.Features == null || result.Features.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[Mapbox] No features returned from API");
                return new List<AddressResult>();
            }

            // Convert Mapbox features to AddressResult
            var addresses = new List<AddressResult>();
            foreach (var feature in result.Features)
            {
                System.Diagnostics.Debug.WriteLine($"[Mapbox] Processing feature: Type={feature.Properties?.FeatureType}, Name={feature.Properties?.Name}");
                var address = ParseMapboxFeature(feature);
                if (address != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Mapbox] Parsed address: {address.AddressLine1}, {address.Postcode}");
                    addresses.Add(address);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Mapbox] Failed to parse feature");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Mapbox] Total addresses parsed: {addresses.Count}");
            return addresses;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Network error: {ex.Message}");
            throw new Exception($"Network error: {ex.Message}. Check your internet connection.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Error: {ex.Message}");
            throw new Exception($"Address lookup failed: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("===============================================");
            System.Diagnostics.Debug.WriteLine("[Mapbox] TestConnectionAsync() called");
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Token length: {_apiToken?.Length ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Token starts with: {_apiToken?.Substring(0, Math.Min(15, _apiToken?.Length ?? 0))}...");
            
            // Test with a known UK postcode
            System.Diagnostics.Debug.WriteLine("[Mapbox] Testing with postcode: SW1A 1AA (Buckingham Palace)");
            var results = await LookupPostcodeAsync("SW1A 1AA");
            
            System.Diagnostics.Debug.WriteLine($"[Mapbox] Results count: {results.Count}");
            
            if (results.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Mapbox] ✅ Test SUCCESSFUL - First result: {results[0].AddressLine1}");
                System.Diagnostics.Debug.WriteLine("===============================================");
                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Mapbox] ❌ Test FAILED - No results returned");
                System.Diagnostics.Debug.WriteLine("===============================================");
                throw new Exception("No results returned from Mapbox. Token may be invalid.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Mapbox] ❌ Test connection exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine("===============================================");
            throw new Exception($"Connection test failed: {ex.Message}", ex);
        }
    }

    private AddressResult? ParseMapboxFeature(MapboxFeature feature)
    {
        if (feature?.Properties == null)
        {
            System.Diagnostics.Debug.WriteLine("[Mapbox Parse] Feature or Properties is null");
            return null;
        }

        var props = feature.Properties;
        var context = props.Context;

        System.Diagnostics.Debug.WriteLine($"[Mapbox Parse] Props - Name: {props.Name}, FullAddress: {props.FullAddress}, PlaceFormatted: {props.PlaceFormatted}");
        System.Diagnostics.Debug.WriteLine($"[Mapbox Parse] Context exists: {context != null}");
        
        if (context != null)
        {
            System.Diagnostics.Debug.WriteLine($"[Mapbox Parse] Context - Postcode: {context.Postcode?.Name}, Place: {context.Place?.Name}, Address: {context.Address?.Name}");
        }

        var address = new AddressResult
        {
            FullAddress = props.FullAddress ?? props.Name ?? "",
            AddressLine1 = context?.Address?.Name ?? props.Name ?? "",
            City = context?.Place?.Name ?? "",
            County = context?.Region?.Name ?? "",
            Postcode = context?.Postcode?.Name ?? "",
            Country = context?.Country?.Name ?? "United Kingdom"
        };

        // Extract coordinates from geometry
        if (feature.Geometry?.Coordinates?.Count >= 2)
        {
            address.Longitude = feature.Geometry.Coordinates[0];
            address.Latitude = feature.Geometry.Coordinates[1];
        }

        // Build address line 2 from place_formatted or city
        if (!string.IsNullOrEmpty(props.PlaceFormatted))
        {
            address.AddressLine2 = props.PlaceFormatted;
        }

        System.Diagnostics.Debug.WriteLine($"[Mapbox Parse] Result - AddressLine1: {address.AddressLine1}, Postcode: {address.Postcode}, City: {address.City}");
        
        return address;
    }

    #region Mapbox API Response Models (v6)

    private class MapboxResponse
    {
        public string? Type { get; set; }
        public List<MapboxFeature>? Features { get; set; }
    }

    private class MapboxFeature
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public MapboxGeometry? Geometry { get; set; }
        public MapboxProperties? Properties { get; set; }
    }

    private class MapboxGeometry
    {
        public string? Type { get; set; }
        public List<double>? Coordinates { get; set; } // [longitude, latitude]
    }

    private class MapboxProperties
    {
        public string? MapboxId { get; set; }
        public string? FeatureType { get; set; }
        public string? Name { get; set; }
        public string? FullAddress { get; set; }
        public string? PlaceFormatted { get; set; }
        public MapboxContext? Context { get; set; }
    }

    private class MapboxContext
    {
        public MapboxAddress? Address { get; set; }
        public MapboxPlace? Place { get; set; }
        public MapboxRegion? Region { get; set; }
        public MapboxPostcode? Postcode { get; set; }
        public MapboxCountry? Country { get; set; }
    }

    private class MapboxAddress
    {
        public string? Name { get; set; }
        public string? AddressNumber { get; set; }
        public string? StreetName { get; set; }
    }

    private class MapboxPlace
    {
        public string? Name { get; set; }
    }

    private class MapboxRegion
    {
        public string? Name { get; set; }
    }

    private class MapboxPostcode
    {
        public string? Name { get; set; }
    }

    private class MapboxCountry
    {
        public string? Name { get; set; }
    }

    #endregion
}
