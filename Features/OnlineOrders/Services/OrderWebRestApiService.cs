using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace POS_in_NET.Services;

/// <summary>
/// REST API service for on-demand queries to OrderWeb.net
/// Handles: gift cards, loyalty points, daily reports
/// </summary>
public class OrderWebRestApiService
{
    private readonly HttpClient _httpClient;
    private string _apiBaseUrl = "";
    private string _tenantId = "";
    private string _apiKey = "";

    public OrderWebRestApiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Configure API connection settings
    /// </summary>
    public void Configure(string apiBaseUrl, string tenantId, string apiKey)
    {
        _apiBaseUrl = apiBaseUrl?.Trim().TrimEnd('/') ?? "";
        _tenantId = tenantId?.Trim() ?? "";
        _apiKey = apiKey?.Trim() ?? "";

        // Set default headers
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Tenant-ID", _tenantId);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        System.Diagnostics.Debug.WriteLine($"🔧 REST API configured: {_apiBaseUrl}");
    }

    /// <summary>
    /// Test API connection
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_apiBaseUrl) || string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_apiKey))
            {
                return (false, "API configuration missing");
            }

            var url = $"{_apiBaseUrl}/health";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine("✅ API connection test successful");
                return (true, "API connection successful");
            }
            else
            {
                return (false, $"API returned status code: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ API connection test failed: {ex.Message}");
            return (false, $"Connection error: {ex.Message}");
        }
    }

    // ==================== GIFT CARD METHODS ====================

    /// <summary>
    /// Check gift card balance
    /// </summary>
    public async Task<(bool Success, decimal Balance, string Message)> CheckGiftCardBalanceAsync(string cardNumber)
    {
        try
        {
            var url = $"{_apiBaseUrl}/gift-cards/{cardNumber}/balance";
            System.Diagnostics.Debug.WriteLine($"🎁 Checking gift card balance: {cardNumber}");

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GiftCardBalanceResponse>();
                System.Diagnostics.Debug.WriteLine($"✅ Gift card balance: ${result?.Balance ?? 0}");
                return (true, result?.Balance ?? 0, "Balance retrieved");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, 0, "Gift card not found");
            }
            else
            {
                return (false, 0, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error checking gift card: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Deduct amount from gift card
    /// </summary>
    public async Task<(bool Success, decimal RemainingBalance, string Message)> DeductGiftCardAsync(string cardNumber, decimal amount, string orderReference)
    {
        try
        {
            var url = $"{_apiBaseUrl}/gift-cards/{cardNumber}/deduct";
            System.Diagnostics.Debug.WriteLine($"🎁 Deducting ${amount} from gift card: {cardNumber}");

            var request = new
            {
                amount = amount,
                order_reference = orderReference,
                timestamp = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync(url, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GiftCardDeductResponse>();
                System.Diagnostics.Debug.WriteLine($"✅ Gift card deducted. Remaining: ${result?.RemainingBalance ?? 0}");
                return (true, result?.RemainingBalance ?? 0, "Deduction successful");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return (false, 0, "Insufficient balance");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, 0, "Gift card not found");
            }
            else
            {
                return (false, 0, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error deducting gift card: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    // ==================== LOYALTY METHODS ====================

    /// <summary>
    /// Check loyalty points
    /// </summary>
    public async Task<(bool Success, int Points, string Message)> CheckLoyaltyPointsAsync(string customerPhone)
    {
        try
        {
            var url = $"{_apiBaseUrl}/loyalty/{customerPhone}/points";
            System.Diagnostics.Debug.WriteLine($"⭐ Checking loyalty points: {customerPhone}");

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoyaltyPointsResponse>();
                System.Diagnostics.Debug.WriteLine($"✅ Loyalty points: {result?.Points ?? 0}");
                return (true, result?.Points ?? 0, "Points retrieved");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, 0, "Customer not found");
            }
            else
            {
                return (false, 0, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error checking loyalty: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Redeem loyalty points
    /// </summary>
    public async Task<(bool Success, int RemainingPoints, string Message)> RedeemLoyaltyPointsAsync(string customerPhone, int pointsToRedeem, string orderReference)
    {
        try
        {
            var url = $"{_apiBaseUrl}/loyalty/{customerPhone}/redeem";
            System.Diagnostics.Debug.WriteLine($"⭐ Redeeming {pointsToRedeem} points for: {customerPhone}");

            var request = new
            {
                points = pointsToRedeem,
                order_reference = orderReference,
                timestamp = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync(url, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoyaltyRedeemResponse>();
                System.Diagnostics.Debug.WriteLine($"✅ Points redeemed. Remaining: {result?.RemainingPoints ?? 0}");
                return (true, result?.RemainingPoints ?? 0, "Points redeemed successfully");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return (false, 0, "Insufficient points");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, 0, "Customer not found");
            }
            else
            {
                return (false, 0, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error redeeming loyalty: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Add loyalty points (earned from purchase)
    /// </summary>
    public async Task<(bool Success, int NewPoints, string Message)> AddLoyaltyPointsAsync(string customerPhone, int pointsToAdd, string orderReference)
    {
        try
        {
            var url = $"{_apiBaseUrl}/loyalty/{customerPhone}/add";
            System.Diagnostics.Debug.WriteLine($"⭐ Adding {pointsToAdd} points for: {customerPhone}");

            var request = new
            {
                points = pointsToAdd,
                order_reference = orderReference,
                timestamp = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync(url, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoyaltyAddResponse>();
                System.Diagnostics.Debug.WriteLine($"✅ Points added. New total: {result?.NewPoints ?? 0}");
                return (true, result?.NewPoints ?? 0, "Points added successfully");
            }
            else
            {
                return (false, 0, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error adding loyalty: {ex.Message}");
            return (false, 0, $"Error: {ex.Message}");
        }
    }

    // ==================== DAILY REPORT METHOD ====================

    /// <summary>
    /// Upload daily sales report
    /// </summary>
    public async Task<(bool Success, string Message)> UploadDailyReportAsync(DateTime reportDate, decimal totalSales, int orderCount, Dictionary<string, object> reportData)
    {
        try
        {
            var url = $"{_apiBaseUrl}/reports/daily";
            System.Diagnostics.Debug.WriteLine($"📊 Uploading daily report for: {reportDate:yyyy-MM-dd}");

            var request = new
            {
                report_date = reportDate.ToString("yyyy-MM-dd"),
                total_sales = totalSales,
                order_count = orderCount,
                data = reportData,
                uploaded_at = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync(url, request);

            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine("✅ Daily report uploaded successfully");
                return (true, "Report uploaded successfully");
            }
            else
            {
                return (false, $"Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error uploading report: {ex.Message}");
            return (false, $"Error: {ex.Message}");
        }
    }
}

// Response DTOs
public class GiftCardBalanceResponse
{
    public decimal Balance { get; set; }
    public string CardNumber { get; set; } = "";
}

public class GiftCardDeductResponse
{
    public bool Success { get; set; }
    public decimal RemainingBalance { get; set; }
    public string TransactionId { get; set; } = "";
}

public class LoyaltyPointsResponse
{
    public int Points { get; set; }
    public string CustomerPhone { get; set; } = "";
}

public class LoyaltyRedeemResponse
{
    public bool Success { get; set; }
    public int RemainingPoints { get; set; }
    public string TransactionId { get; set; } = "";
}

public class LoyaltyAddResponse
{
    public bool Success { get; set; }
    public int NewPoints { get; set; }
    public string TransactionId { get; set; } = "";
}
