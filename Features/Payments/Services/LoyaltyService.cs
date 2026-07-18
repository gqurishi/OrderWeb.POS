using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
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
    private readonly OrderWebApiClient? _orderWebApiClient;
    private readonly OrderWebGiftCardApiService? _giftCardApiService;
    private string? _apiKey;
    private string? _baseUrl;
    private string? _tenantId;
    private string? _restaurantSlug;

    public LoyaltyService(
        DatabaseService databaseService,
        OrderWebApiClient? orderWebApiClient = null,
        OrderWebGiftCardApiService? giftCardApiService = null)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _databaseService = databaseService;
        _orderWebApiClient = orderWebApiClient;
        _giftCardApiService = giftCardApiService;
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
            var sharedConfig = _orderWebApiClient != null ? await _orderWebApiClient.GetConfigAsync() : null;
            _apiKey = sharedConfig?.ApiKey ?? config.GetValueOrDefault("api_key", "");
            _tenantId = sharedConfig?.TenantSlug ?? config.GetValueOrDefault("tenant_slug", "");
            _restaurantSlug = config.GetValueOrDefault("restaurant_slug", "");
            _baseUrl = sharedConfig?.ApiBaseUrl ?? OrderWebApiClient.NormalizeApiBaseUrl(
                config.GetValueOrDefault("api_base_url", config.GetValueOrDefault("cloud_url", "")));

            AppDiagnostics.Log("LoyaltyService configuration loaded");
            AppDiagnostics.Log($"  API Base: {_baseUrl}");
            AppDiagnostics.Log($"  Tenant: {_tenantId}");
            AppDiagnostics.Log($"  Restaurant: {_restaurantSlug}");
            AppDiagnostics.Log($"  API Key configured: {!string.IsNullOrWhiteSpace(_apiKey)}");

            // Configure HttpClient headers - POS API uses Bearer token
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _apiKey);
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
            if (!IsConfigured())
            {
                return MissingConfigurationResponse();
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return MotherOnlyLoyaltyResponse();
            }

            var lookupValue = NormalizeCustomerLookupValue(phone);
            if (string.IsNullOrWhiteSpace(lookupValue))
            {
                return new LoyaltyLookupResponse { Success = false, Error = "Enter a phone number or loyalty card number." };
            }

            // Use POS API endpoint as per documentation
            var url = $"{_baseUrl}/pos/loyalty-lookup?tenant={Uri.EscapeDataString(_tenantId!)}&phone={Uri.EscapeDataString(lookupValue)}";
            AppDiagnostics.Log($"Searching customer (POS API): {url}");

            var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await response.Content.ReadAsStringAsync();

            AppDiagnostics.Log($"  Response Status: {response.StatusCode}");
#if DEBUG
            AppDiagnostics.Log($"  Response received ({content.Length} characters)");
#endif

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                result = result?.Normalize();
                System.Diagnostics.Debug.WriteLine(" Customer loyalty record found");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                System.Diagnostics.Debug.WriteLine(" Customer loyalty record not found");
                return new LoyaltyLookupResponse { Success = false, Error = "Customer not found" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" API Error: {response.StatusCode}");
                return new LoyaltyLookupResponse { Success = false, Error = ParseLoyaltyError(content, $"API Error: {response.StatusCode}") };
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
            if (!IsConfigured())
            {
                return MissingConfigurationResponse();
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return MotherOnlyLoyaltyResponse();
            }

            phone = CleanPhoneNumber(phone);
            if (string.IsNullOrWhiteSpace(phone))
            {
                return new LoyaltyLookupResponse { Success = false, Error = "Enter a valid customer phone number." };
            }

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

            System.Diagnostics.Debug.WriteLine(" Creating loyalty customer");
            System.Diagnostics.Debug.WriteLine($"   URL: {url}");
            System.Diagnostics.Debug.WriteLine($"   Request: {json}");

            var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent });
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"   Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"   Response received ({content.Length} characters)");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                result = result?.Normalize();
                System.Diagnostics.Debug.WriteLine(" Loyalty customer created");
                return result ?? new LoyaltyLookupResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($" Failed to create customer: {response.StatusCode}");
                return new LoyaltyLookupResponse { Success = false, Error = ParseLoyaltyError(content, $"Failed to create customer: {response.StatusCode}") };
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
    /// POST /api/pos/loyalty/add
    /// </summary>
    public async Task<LoyaltyLookupResponse> AddPointsAsync(string phone, int points, string reason, string? transactionId = null, int? expectedRemainingPoints = null)
    {
        await EnsureInitializedAsync();
        try
        {
            if (!IsConfigured())
            {
                return MissingConfigurationResponse();
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return MotherOnlyLoyaltyResponse();
            }

            phone = CleanPhoneNumber(phone);
            if (string.IsNullOrWhiteSpace(phone))
            {
                return new LoyaltyLookupResponse { Success = false, Error = "Enter a valid customer phone number." };
            }

            var stableTransactionId = BuildStableTransactionId("loyalty-add", transactionId, phone, points, reason);
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("loyalty-add", stableTransactionId);
            var actionResult = await PostLoyaltyActionAsync(
                "add",
                phone,
                points,
                reason,
                idempotencyKey);

            if (actionResult.Success)
            {
                var refreshed = await SearchCustomerAsync(phone);
                ApplyExpectedPointsBalance(refreshed, expectedRemainingPoints);
                return refreshed;
            }

            var verification = await SearchCustomerAsync(phone);
            if (expectedRemainingPoints.HasValue
                && verification.Success
                && verification.Customer?.PointsBalance == expectedRemainingPoints.Value)
            {
                System.Diagnostics.Debug.WriteLine(" POS loyalty add returned an error, but lookup confirmed the expected balance.");
                return verification;
            }

            return new LoyaltyLookupResponse
            {
                Success = false,
                Error = $"Failed to add points: {actionResult.Error}"
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in AddPointsAsync: {ex.Message}");
            await QueueMoneyOrPointsOperationAsync("loyalty_add", phone, points, reason, transactionId);
            return new LoyaltyLookupResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Redeem loyalty points from customer account
    /// POST /api/pos/loyalty/redeem
    /// </summary>
    public async Task<LoyaltyLookupResponse> RedeemPointsAsync(string phone, int points, string reason, string? transactionId = null, int? expectedRemainingPoints = null)
    {
        await EnsureInitializedAsync();
        try
        {
            if (!IsConfigured())
            {
                return MissingConfigurationResponse();
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return MotherOnlyLoyaltyResponse();
            }

            phone = CleanPhoneNumber(phone);
            if (string.IsNullOrWhiteSpace(phone))
            {
                return new LoyaltyLookupResponse { Success = false, Error = "Enter a valid customer phone number." };
            }

            var stableTransactionId = BuildStableTransactionId("loyalty-redeem", transactionId, phone, points, reason);
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("loyalty-redeem", stableTransactionId);
            var actionResult = await PostLoyaltyActionAsync(
                "redeem",
                phone,
                points,
                reason,
                idempotencyKey);

            if (actionResult.Success)
            {
                var refreshed = await SearchCustomerAsync(phone);
                ApplyExpectedPointsBalance(refreshed, expectedRemainingPoints);
                return refreshed;
            }

            var verification = await SearchCustomerAsync(phone);
            if (expectedRemainingPoints.HasValue
                && verification.Success
                && verification.Customer?.PointsBalance == expectedRemainingPoints.Value)
            {
                System.Diagnostics.Debug.WriteLine(" POS loyalty redeem returned an error, but lookup confirmed the expected balance.");
                return verification;
            }

            return new LoyaltyLookupResponse
            {
                Success = false,
                Error = $"Failed to redeem points: {actionResult.Error}"
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in RedeemPointsAsync: {ex.Message}");
            await QueueMoneyOrPointsOperationAsync("loyalty_redeem", phone, points, reason, transactionId);
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
        if (_giftCardApiService != null)
        {
            return await _giftCardApiService.LookupAsync(cardNumber, GiftCardLookupPurpose.Redeem);
        }

        await EnsureInitializedAsync();
        try
        {
            await InitializeAsync();

            // Verify configuration
            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_baseUrl))
            {
                System.Diagnostics.Debug.WriteLine(" Gift card check failed: Missing API configuration");
                return new GiftCardLookupResponse 
                { 
                    Success = false, 
                    Error = "API not configured. Please check Cloud Settings." 
                };
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return new GiftCardLookupResponse
                {
                    Success = false,
                    Error = "Gift card cloud lookup runs on the mother/master terminal only."
                };
            }

            var trimmedCardNumber = cardNumber.Trim();
            var giftCardTenant = GetGiftCardTenant();
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var lookupUrl = $"{_baseUrl}/pos/gift-card/lookup?tenant={Uri.EscapeDataString(giftCardTenant)}&cardNumber={Uri.EscapeDataString(trimmedCardNumber)}&_={cacheBuster}";
            var result = await SendGiftCardLookupAsync(lookupUrl, trimmedCardNumber);

            if (!result.Success)
            {
                var legacyQueryUrl = $"{_baseUrl}/pos/gift-card/lookup?tenant={Uri.EscapeDataString(giftCardTenant)}&card_number={Uri.EscapeDataString(trimmedCardNumber)}&_={cacheBuster}";
                result = await SendGiftCardLookupAsync(legacyQueryUrl, trimmedCardNumber);
            }

            if (result.Success && result.GiftCard != null)
            {
                System.Diagnostics.Debug.WriteLine($" Gift card balance: £{result.GiftCard.Balance:F2}");
            }

            return result;
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
    public async Task<GiftCardRedeemResponse> RedeemGiftCardAsync(string cardNumber, decimal amount, string description, string? transactionId = null, string? orderId = null)
    {
        if (_giftCardApiService != null)
        {
            return await _giftCardApiService.RedeemAsync(
                new GiftCardRedeemRequest
                {
                    CardNumber = cardNumber,
                    Amount = amount,
                    Description = description,
                    OrderId = orderId
                },
                transactionId);
        }

        await EnsureInitializedAsync();
        try
        {
            await InitializeAsync();

            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_baseUrl))
            {
                return new GiftCardRedeemResponse
                {
                    Success = false,
                    Error = "API not configured. Please check Cloud Settings."
                };
            }

            if (!await CanRunMoneyCloudAsync())
            {
                return new GiftCardRedeemResponse
                {
                    Success = false,
                    Error = "Gift card redemption runs on the mother/master terminal only."
                };
            }

            var stableTransactionId = BuildStableTransactionId("gift-card-redeem", transactionId, cardNumber, amount, description);
            var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("gift-card-redeem", stableTransactionId);
            var redeemOrderId = string.IsNullOrWhiteSpace(orderId) ? BuildFallbackGiftCardOrderId(transactionId) : orderId.Trim();
            var request = new
            {
                tenant = GetGiftCardTenant(),
                cardNumber = cardNumber.Trim().ToUpperInvariant(),
                amount = amount,
                orderId = redeemOrderId,
                description = description,
                idempotency_key = idempotencyKey
            };

            var url = $"{_baseUrl}/pos/gift-card/redeem";
            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent };
            httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            httpRequest.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

            System.Diagnostics.Debug.WriteLine(" Redeeming gift card balance");
            AppDiagnostics.Log("Gift card redeem requested");
            AppDiagnostics.Log($"  Request: {json}");

            var response = await SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();
            AppDiagnostics.Log($"  Response Status: {(int)response.StatusCode} {response.StatusCode}");
#if DEBUG
            AppDiagnostics.Log($"  Response received ({content.Length} characters)");
#endif

            if (response.IsSuccessStatusCode)
            {
                var result = ParseGiftCardRedeemResponse(content);

                System.Diagnostics.Debug.WriteLine($" Gift card redeemed: Remaining balance = £{result?.EffectiveRemainingBalance:F2}");
                return result ?? new GiftCardRedeemResponse { Success = false, Error = "Invalid response" };
            }
            else
            {
                var error = ParseGiftCardError(content, $"Failed to redeem gift card: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($" Failed to redeem gift card: {error}");
                return new GiftCardRedeemResponse { Success = false, Error = error };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in RedeemGiftCardAsync: {ex.Message}");
            await QueueGiftCardOperationAsync(cardNumber, amount, description, transactionId);
            return new GiftCardRedeemResponse { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Helper Methods

    private async Task<(bool Success, string? Error)> PostLoyaltyActionAsync(
        string action,
        string phone,
        int points,
        string reason,
        string idempotencyKey)
    {
        var endpoint = $"{_baseUrl}/pos/loyalty/{action}";
        var payload = new
        {
            tenant = _tenantId,
            phone = phone,
            points = points,
            reason = reason
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

        System.Diagnostics.Debug.WriteLine($" Loyalty {action} requested");
        System.Diagnostics.Debug.WriteLine($"   Request: {json}");
        AppDiagnostics.Log($"Loyalty {action} requested");
        AppDiagnostics.Log($"  Request: {json}");

        var response = await SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        System.Diagnostics.Debug.WriteLine($"   Response Status: {response.StatusCode}");
        System.Diagnostics.Debug.WriteLine($"   Response received ({responseBody.Length} characters)");
        AppDiagnostics.Log($"  Response Status: {(int)response.StatusCode} {response.StatusCode}");
        AppDiagnostics.Log($"  Response received ({responseBody.Length} characters)");

        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }

        return (false, ParseLoyaltyError(responseBody, $"Loyalty {action} failed: {response.StatusCode}"));
    }

    private async Task QueueMoneyOrPointsOperationAsync(string operationType, string phone, int points, string reason, string? transactionId)
    {
        if (_orderWebApiClient == null || string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_tenantId))
        {
            return;
        }

        var operation = operationType == "loyalty_add" ? "loyalty-add" : "loyalty-redeem";
        var stableTransactionId = BuildStableTransactionId(operation, transactionId, phone, points, reason);
        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey(operation, stableTransactionId);
        var endpointAction = operationType == "loyalty_add" ? "add" : "redeem";
        var endpoint = $"{_baseUrl}/pos/loyalty/{endpointAction}";
        var payload = new
        {
            tenant = _tenantId,
            phone = phone,
            points = points,
            reason = reason
        };

        await _orderWebApiClient.EnqueueAsync(operationType, endpoint, payload, _apiKey, idempotencyKey, priority: 2);
    }

    private async Task<GiftCardLookupResponse> SendGiftCardLookupAsync(string url, string cardNumber)
    {
        AppDiagnostics.Log("Checking gift card");
        AppDiagnostics.Log($"  URL: {url}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        var response = await SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        AppDiagnostics.Log($"  Response Status: {(int)response.StatusCode} {response.StatusCode}");
#if DEBUG
        AppDiagnostics.Log($"  Response received ({content.Length} characters)");
#endif

        if (response.IsSuccessStatusCode)
        {
            var result = ParseGiftCardLookupResponse(content);
            return NormalizeGiftCardLookupResult(result) ?? new GiftCardLookupResponse { Success = false, Error = "Invalid gift card response" };
        }

        return new GiftCardLookupResponse
        {
            Success = false,
            Error = ParseGiftCardError(content, $"Gift card lookup failed: {response.StatusCode}")
        };
    }

    private async Task QueueGiftCardOperationAsync(string cardNumber, decimal amount, string description, string? transactionId)
    {
        if (_orderWebApiClient == null || string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_baseUrl))
        {
            return;
        }

        var stableTransactionId = BuildStableTransactionId("gift-card-redeem", transactionId, cardNumber, amount, description);
        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("gift-card-redeem", stableTransactionId);
        var endpoint = $"{_baseUrl}/pos/gift-card/redeem";
        var trimmedCardNumber = cardNumber.Trim();
        var redeemOrderId = BuildFallbackGiftCardOrderId(transactionId);
        var payload = new
        {
            tenant = GetGiftCardTenant(),
            cardNumber = trimmedCardNumber.ToUpperInvariant(),
            amount = amount,
            orderId = redeemOrderId,
            description = description,
            idempotency_key = idempotencyKey
        };

        await _orderWebApiClient.EnqueueAsync("gift_card_redeem", endpoint, payload, _apiKey, idempotencyKey, priority: 1);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
        }

        return _orderWebApiClient != null
            ? await _orderWebApiClient.SendAsync(request)
            : await _httpClient.SendAsync(request);
    }

    private async Task<bool> CanRunMoneyCloudAsync()
    {
        return _orderWebApiClient != null
            ? (await _orderWebApiClient.CanRunCloudJobsAsync()).Allowed
            : TerminalRoleService.CanRunMotherJobs;
    }

    private static LoyaltyLookupResponse MotherOnlyLoyaltyResponse()
    {
        return new LoyaltyLookupResponse
        {
            Success = false,
            Error = "Loyalty cloud operations run on the mother/master terminal only."
        };
    }

    private static string BuildStableTransactionId(string operation, string? explicitTransactionId, params object?[] fallbackParts)
    {
        if (!string.IsNullOrWhiteSpace(explicitTransactionId))
        {
            return explicitTransactionId.Trim();
        }

        return OrderWebApiClient.BuildIdempotencyKey(operation, fallbackParts);
    }

    private string GetGiftCardTenant()
    {
        if (!string.IsNullOrWhiteSpace(_tenantId))
        {
            return _tenantId.Trim();
        }

        return _restaurantSlug?.Trim() ?? string.Empty;
    }

    private static string BuildFallbackGiftCardOrderId(string? transactionId)
    {
        return string.IsNullOrWhiteSpace(transactionId)
            ? $"POS-GIFTCARD-{DateTime.Now:yyyyMMddHHmmss}"
            : transactionId.Trim();
    }

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

    private string NormalizeCustomerLookupValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Any(char.IsLetter)
            ? trimmed
            : CleanPhoneNumber(trimmed);
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_baseUrl)
            && !string.IsNullOrWhiteSpace(_tenantId)
            && !string.IsNullOrWhiteSpace(_apiKey);
    }

    private static LoyaltyLookupResponse MissingConfigurationResponse()
    {
        return new LoyaltyLookupResponse
        {
            Success = false,
            Error = "API not configured. Please check Cloud Settings."
        };
    }

    private static void ApplyExpectedPointsBalance(LoyaltyLookupResponse response, int? expectedPointsBalance)
    {
        if (!expectedPointsBalance.HasValue || !response.Success || response.Customer == null)
        {
            return;
        }

        response.Customer.PointsBalance = Math.Max(0, expectedPointsBalance.Value);
        if (response.Loyalty != null)
        {
            response.Loyalty.PointsBalance = response.Customer.PointsBalance;
        }
    }

    private static GiftCardLookupResponse? ParseGiftCardLookupResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<GiftCardLookupResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var cardElement = FindGiftCardElement(root);
        var found = GetBool(root, "found") ?? GetBool(cardElement, "found");
        var canUse = GetBool(root, "can_use", "canUse") ?? GetBool(cardElement, "can_use", "canUse");
        var isExpired = GetBool(root, "is_expired", "isExpired") ?? GetBool(cardElement, "is_expired", "isExpired");

        if (result?.GiftCard != null)
        {
            result.Found ??= found;
            result.CanUse ??= canUse;
            result.IsExpired ??= isExpired;
            result.GiftCard.CanUse ??= canUse;
            result.GiftCard.IsExpiredFlag ??= isExpired;
            return result;
        }

        if (cardElement.ValueKind == JsonValueKind.Undefined)
        {
            return result;
        }

        var card = new GiftCard
        {
            CardNumber = GetString(cardElement, "card_number", "cardNumber", "number", "code") ?? string.Empty,
            Balance = GetDecimal(cardElement, "balance", "remaining_balance", "remainingBalance", "amount"),
            Status = GetString(cardElement, "status", "state") ?? "active",
            CardType = GetString(cardElement, "card_type", "cardType", "type") ?? string.Empty,
            CreatedAt = GetDateTime(cardElement, "created_at", "createdAt"),
            ExpiryDate = GetDateTime(cardElement, "expiry_date", "expiryDate", "expires_at", "expiresAt"),
            CanUse = canUse,
            IsExpiredFlag = isExpired
        };

        return new GiftCardLookupResponse
        {
            Success = result?.Success ?? found != false,
            Found = result?.Found ?? found,
            CanUse = result?.CanUse ?? canUse,
            IsExpired = result?.IsExpired ?? isExpired,
            Error = result?.Error,
            Message = result?.Message,
            GiftCard = card
        };
    }

    private static GiftCardLookupResponse? NormalizeGiftCardLookupResult(GiftCardLookupResponse? result)
    {
        if (result == null)
        {
            return null;
        }

        if (result.Found == false)
        {
            result.Success = false;
            result.Error ??= "Card not found";
            return result;
        }

        if (result.GiftCard == null)
        {
            result.Success = false;
            result.Error ??= "Gift card details were not returned";
            return result;
        }

        if (result.CanUse.HasValue && !result.GiftCard.CanUse.HasValue)
        {
            result.GiftCard.CanUse = result.CanUse;
        }

        if (result.IsExpired.HasValue && !result.GiftCard.IsExpiredFlag.HasValue)
        {
            result.GiftCard.IsExpiredFlag = result.IsExpired;
        }

        if (string.IsNullOrWhiteSpace(result.GiftCard.Status))
        {
            result.GiftCard.Status = "active";
        }

        if (!result.GiftCard.IsUsable)
        {
            result.Success = false;
            result.Error ??= result.GiftCard.IsExpired
                ? "Gift card is expired"
                : result.GiftCard.CanUse == false
                    ? "Gift card cannot be used"
                    : result.GiftCard.Balance <= 0
                        ? "Gift card has no remaining balance"
                        : "Gift card is not active";
            return result;
        }

        result.Success = true;
        return result;
    }

    private static GiftCardRedeemResponse? ParseGiftCardRedeemResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<GiftCardRedeemResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var data = TryGetProperty(root, out var dataElement, "data", "result") ? dataElement : root;
        var redemption = TryGetProperty(data, out var redemptionElement, "redemption", "transaction", "gift_card", "giftCard", "card")
            ? redemptionElement
            : data;
        var card = FindGiftCardElement(root);
        if (card.ValueKind == JsonValueKind.Undefined)
        {
            card = default;
        }

        result ??= new GiftCardRedeemResponse();
        result.Success = GetBool(root, "success") ?? (string.IsNullOrWhiteSpace(result.Error) && string.IsNullOrWhiteSpace(GetString(root, "error")));
        result.Error ??= GetString(root, "error") ?? GetString(data, "error");
        result.Message ??= GetString(root, "message") ?? GetString(data, "message");
        result.PreviousBalance ??= GetFirstNullableDecimal("previous_balance", "previousBalance");
        result.NewBalance ??= GetFirstNullableDecimal("new_balance", "newBalance");
        result.RemainingBalance ??= GetFirstNullableDecimal("remaining_balance", "remainingBalance", "balance");
        result.RedeemedAmount ??= GetFirstNullableDecimal("redeemed_amount", "redeemedAmount");
        result.AmountRedeemed ??= GetFirstNullableDecimal("amount_redeemed", "amountRedeemed", "amount");
        result.IsFullyRedeemed ??= GetFirstBool("is_fully_redeemed", "isFullyRedeemed");

        return result;

        decimal? GetFirstNullableDecimal(params string[] names)
        {
            return GetNullableDecimal(root, names)
                ?? GetNullableDecimal(data, names)
                ?? GetNullableDecimal(redemption, names)
                ?? GetNullableDecimal(card, names);
        }

        bool? GetFirstBool(params string[] names)
        {
            return GetBool(root, names)
                ?? GetBool(data, names)
                ?? GetBool(redemption, names)
                ?? GetBool(card, names);
        }
    }

    private static JsonElement FindGiftCardElement(JsonElement root)
    {
        if (TryGetProperty(root, out var cardElement, "gift_card", "giftCard", "card", "giftCardDetails"))
        {
            return cardElement;
        }

        if (TryGetProperty(root, out var dataElement, "data", "result")
            && TryGetProperty(dataElement, out cardElement, "gift_card", "giftCard", "card", "giftCardDetails"))
        {
            return cardElement;
        }

        return HasAnyProperty(root, "balance", "card_number", "cardNumber")
            ? root
            : default;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names)
    {
        return names.Any(name => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _));
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal GetDecimal(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static decimal? GetNullableDecimal(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null
        };
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] names)
    {
        var value = GetString(element, names);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string ParseGiftCardError(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallback}. OrderWeb returned a web page instead of API JSON. Please check the API base URL and gift card endpoint configuration.";
        }

        try
        {
            var lookup = JsonSerializer.Deserialize<GiftCardLookupResponse>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (!string.IsNullOrWhiteSpace(lookup?.Error) || !string.IsNullOrWhiteSpace(lookup?.Message))
            {
                return lookup?.Error ?? lookup!.Message!;
            }

            var redeem = JsonSerializer.Deserialize<GiftCardRedeemResponse>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return redeem?.Error ?? redeem?.Message ?? fallback;
        }
        catch
        {
            return trimmed.Length > 180 ? $"{trimmed[..180]}..." : trimmed;
        }
    }

    private static string ParseLoyaltyError(string content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
            return fallback;

        var trimmed = content.Trim();
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallback}. OrderWeb returned a web page instead of API JSON. Please check the API base URL and loyalty endpoint configuration.";
        }

        try
        {
            var result = JsonSerializer.Deserialize<LoyaltyLookupResponse>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Error ?? result?.Message ?? fallback;
        }
        catch
        {
            return trimmed.Length > 180 ? $"{trimmed[..180]}..." : trimmed;
        }
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
