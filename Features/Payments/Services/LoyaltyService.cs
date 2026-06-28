using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Service for managing customer loyalty points and gift cards via OrderWeb.net API
/// </summary>
public class LoyaltyService
{
    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private string? _apiKey;
    private string? _baseUrl;
    private string? _tenantId;

    public LoyaltyService(DatabaseService databaseService)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _databaseService = databaseService;
    }

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            await InitializeAsync();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    /// <summary>
    /// Reinitialize service with updated settings (call after settings change)
    /// </summary>
    public async Task ReinitializeAsync()
    {
        _initialized = false;
        await EnsureInitializedAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            _apiKey = config.GetValueOrDefault("api_key", "");
            _tenantId = config.GetValueOrDefault("tenant_slug", "");
            _baseUrl = NormalizeApiBaseUrl(
                config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", "")));

            AppDiagnostics.Log("LoyaltyService configuration loaded");
            AppDiagnostics.Log($"  API Base: {_baseUrl}");
            AppDiagnostics.Log($"  Tenant: {_tenantId}");
            AppDiagnostics.Log($"  API Key configured: {!string.IsNullOrWhiteSpace(_apiKey)}");

            // Configure HttpClient headers - POS API uses Bearer token
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                
                AppDiagnostics.Log("LoyaltyService initialized successfully");
            }
            else
            {
                AppDiagnostics.Log("LoyaltyService: API key is missing — configure in Cloud Settings.");
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("LoyaltyServiceInit", ex);
        }
    }

    #region Customer Loyalty Methods

    /// <summary>
    /// Search for customer by phone number using POS API endpoint
    /// GET /api/pos/loyalty-lookup?tenant={tenant}&phone={phone}
    /// </summary>
    public async Task<LoyaltyLookupResponse> SearchCustomerAsync(string phone)
    {
        await EnsureInitializedAsync();
        try
        {
            // Remove any formatting from phone number
            phone = CleanPhoneNumber(phone);

            // Use POS API endpoint as per documentation
            var url = $"{_baseUrl}/pos/loyalty-lookup?tenant={_tenantId}&phone={phone}";
            AppDiagnostics.Log($"Searching customer (POS API): {url}");

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            AppDiagnostics.Log($"  Response Status: {response.StatusCode}");
#if DEBUG
            AppDiagnostics.Log($"  Response Body: {content}");
#endif

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Customer found: {result?.Customer?.CustomerName} - {result?.Customer?.PointsBalance} pts");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                System.Diagnostics.Debug.WriteLine($" Customer not found: {phone}");
                return new LoyaltyLookupResponse { Success = false, Error = "Customer not found" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" API Error: {response.StatusCode} - {content}");
                return new LoyaltyLookupResponse { Success = false, Error = $"API Error: {response.StatusCode}" };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in SearchCustomerAsync: {ex.Message}");
            return new LoyaltyLookupResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Create new customer account
    /// </summary>
    public async Task<LoyaltyLookupResponse> CreateCustomerAsync(string phone, string name, string? email = null)
    {
        await EnsureInitializedAsync();
        try
        {
            phone = CleanPhoneNumber(phone);

            var request = new CreateCustomerRequest
            {
                Phone = phone,
                Name = name,
                Email = email
            };

            // Use proper tenant-based endpoint
            var url = $"{_baseUrl}/tenant/{_tenantId}/admin/loyalty/customers";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            System.Diagnostics.Debug.WriteLine($" Creating customer: {name} - {phone}");
            System.Diagnostics.Debug.WriteLine($"   URL: {url}");
            System.Diagnostics.Debug.WriteLine($"   Request: {json}");

            var response = await _httpClient.PostAsync(url, httpContent);
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"   Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"   Response Body: {content}");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Customer created: {result?.Customer?.LoyaltyCardNumber}");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Failed to create customer: {response.StatusCode} - {content}");
                return new LoyaltyLookupResponse { Success = false, Error = $"Failed to create customer: {response.StatusCode}" };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in CreateCustomerAsync: {ex.Message}");
            return new LoyaltyLookupResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Add loyalty points to customer account
    /// POST /api/{tenant}/loyalty/{phone}/add
    /// </summary>
    public async Task<LoyaltyLookupResponse> AddPointsAsync(string phone, int points, string reason)
    {
        await EnsureInitializedAsync();
        try
        {
            phone = CleanPhoneNumber(phone);

            var request = new
            {
                points = points,
                orderId = $"POS-{DateTime.Now.Ticks}",
                orderAmount = 0.0,
                reason = reason
            };

            // Use POS API endpoint: POST /api/{tenant}/loyalty/{phone}/add
            var url = $"{_baseUrl}/{_tenantId}/loyalty/{phone}/add";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            System.Diagnostics.Debug.WriteLine($" ADD {points} points for {phone}");
            System.Diagnostics.Debug.WriteLine($"   URL: {url}");
            System.Diagnostics.Debug.WriteLine($"   Request: {json}");

            var response = await _httpClient.PostAsync(url, httpContent);
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"   Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"   Response Body: {content}");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Points added: New balance = {result?.Customer?.PointsBalance} pts");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Failed to add points: {response.StatusCode} - {content}");
                return new LoyaltyLookupResponse { Success = false, Error = $"Failed to add points: {content}" };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in AddPointsAsync: {ex.Message}");
            return new LoyaltyLookupResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Redeem loyalty points from customer account
    /// POST /api/{tenant}/loyalty/{phone}/redeem
    /// </summary>
    public async Task<LoyaltyLookupResponse> RedeemPointsAsync(string phone, int points, string reason)
    {
        await EnsureInitializedAsync();
        try
        {
            phone = CleanPhoneNumber(phone);

            var request = new
            {
                points = points,
                orderId = $"POS-{DateTime.Now.Ticks}",
                orderAmount = 0.0,
                reason = reason
            };

            // Use POS API endpoint: POST /api/{tenant}/loyalty/{phone}/redeem
            var url = $"{_baseUrl}/{_tenantId}/loyalty/{phone}/redeem";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            System.Diagnostics.Debug.WriteLine($" REDEEM {points} points for {phone}");
            System.Diagnostics.Debug.WriteLine($"   URL: {url}");
            System.Diagnostics.Debug.WriteLine($"   Request: {json}");

            var response = await _httpClient.PostAsync(url, httpContent);
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"   Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"   Response Body: {content}");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Points redeemed: New balance = {result?.Customer?.PointsBalance} pts");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Failed to redeem points: {response.StatusCode} - {content}");
                return new LoyaltyLookupResponse { Success = false, Error = $"Failed to redeem points: {content}" };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in RedeemPointsAsync: {ex.Message}");
            return new LoyaltyLookupResponse { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Gift Card Methods

    /// <summary>
    /// Check gift card balance
    /// </summary>
    public async Task<GiftCardLookupResponse> CheckGiftCardBalanceAsync(string cardNumber)
    {
        await EnsureInitializedAsync();
        try
        {
            await InitializeAsync();

            // Verify configuration
            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_tenantId))
            {
                System.Diagnostics.Debug.WriteLine(" Gift card check failed: Missing API configuration");
                return new GiftCardLookupResponse 
                { 
                    Success = false, 
                    Error = "API not configured. Please check Cloud Settings." 
                };
            }

            var request = new GiftCardLookupRequest { CardNumber = cardNumber };
            var url = $"{_baseUrl}/tenant/{_tenantId}/admin/gift-cards/lookup";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            AppDiagnostics.Log($"Checking gift card: {cardNumber}");
#if DEBUG
            AppDiagnostics.Log($"  URL: {url}");
            AppDiagnostics.Log($"  Request: {json}");
#endif

            var response = await _httpClient.PostAsync(url, httpContent);
            var content = await response.Content.ReadAsStringAsync();

            AppDiagnostics.Log($"  Response Status: {response.StatusCode}");
#if DEBUG
            AppDiagnostics.Log($"  Response Body: {content}");
#endif

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<GiftCardLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Gift card balance: £{result?.GiftCard?.Balance:F2}");
                return result ?? new GiftCardLookupResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Gift card lookup failed: {response.StatusCode}");
                
                // Try to parse error from response
                string errorMessage = "Gift card not found";
                try
                {
                    var errorResult = JsonSerializer.Deserialize<GiftCardLookupResponse>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (!string.IsNullOrEmpty(errorResult?.Error))
                    {
                        errorMessage = errorResult.Error;
                    }
                }
                catch
                {
                    // If JSON parsing fails, use raw content
                    if (!string.IsNullOrEmpty(content))
                    {
                        errorMessage = $"{response.StatusCode}: {content}";
                    }
                }
                
                return new GiftCardLookupResponse { Success = false, Error = errorMessage };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in CheckGiftCardBalanceAsync: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   Stack Trace: {ex.StackTrace}");
            return new GiftCardLookupResponse { Success = false, Error = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Redeem amount from gift card
    /// </summary>
    public async Task<GiftCardRedeemResponse> RedeemGiftCardAsync(string cardNumber, decimal amount, string description)
    {
        await EnsureInitializedAsync();
        try
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_tenantId))
            {
                return new GiftCardRedeemResponse
                {
                    Success = false,
                    Error = "API not configured. Please check Cloud Settings."
                };
            }

            var request = new GiftCardRedeemRequest
            {
                CardNumber = cardNumber,
                Amount = amount,
                Description = description
            };

            var url = $"{_baseUrl}/tenant/{_tenantId}/admin/gift-cards/redeem";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            System.Diagnostics.Debug.WriteLine($" Redeeming £{amount:F2} from gift card: {cardNumber}");

            var response = await _httpClient.PostAsync(url, httpContent);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<GiftCardRedeemResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Gift card redeemed: Remaining balance = £{result?.RemainingBalance:F2}");
                return result ?? new GiftCardRedeemResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                var errorResponse = JsonSerializer.Deserialize<GiftCardRedeemResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                System.Diagnostics.Debug.WriteLine($" Failed to redeem gift card: {errorResponse?.Error}");
                return errorResponse ?? new GiftCardRedeemResponse { Success = false, Error = "Failed to redeem gift card" };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in RedeemGiftCardAsync: {ex.Message}");
            return new GiftCardRedeemResponse { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Clean phone number and normalize to UK format (07xxxxxxxxx)
    /// Handles: +447306506797, 447306506797, 07306506797
    /// </summary>
    private string CleanPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        // Remove all non-digit characters (spaces, dashes, parentheses, etc.)
        var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());

        // Normalize UK phone numbers:
        // +447306506797 or 447306506797 → 07306506797
        if (digitsOnly.StartsWith("44") && digitsOnly.Length == 12)
        {
            // Remove '44' and add '0' prefix
            digitsOnly = "0" + digitsOnly.Substring(2);
            System.Diagnostics.Debug.WriteLine($" Normalized UK phone: +44 → 0 format: {digitsOnly}");
        }

        return digitsOnly;
    }

    private static string NormalizeApiBaseUrl(string? configuredUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredUrl)
            ? "https://orderweb.net/api"
            : configuredUrl.Trim();

        baseUrl = baseUrl.TrimEnd('/');

        if (baseUrl.EndsWith("/pos", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^4];
        }

        return string.IsNullOrWhiteSpace(baseUrl)
            ? "https://orderweb.net/api"
            : baseUrl;
    }

    /// <summary>
    /// Format phone number for display (e.g., 07123456789 -> 07123 456 789)
    /// </summary>
    public string FormatPhoneNumber(string phone)
    {
        phone = CleanPhoneNumber(phone);
        
        if (phone.Length == 11 && phone.StartsWith("0"))
        {
            return $"{phone.Substring(0, 5)} {phone.Substring(5, 3)} {phone.Substring(8)}";
        }
        
        return phone;
    }

    #endregion
}
