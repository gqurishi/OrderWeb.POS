using System.Net.Http.Json;
using System.Text.Json;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Cloud Sync Service - Handles bidirectional sync with OrderWeb.net cloud
/// POS → Cloud: Daily reports, gift card transactions, loyalty transactions
/// Cloud → POS: Online orders, customer data (via direct database read)
/// </summary>
public class CloudSyncService
{
    private readonly HttpClient _httpClient;
    private readonly DatabaseService _databaseService;
    private readonly Timer? _heartbeatTimer;
    private string _cloudApiUrl = "";
    private string _tenantSlug = "";
    private string _restaurantSlug = "";
    private string _apiKey = "";
    private bool _isEnabled = false;

    public event EventHandler<string>? HeartbeatStatusChanged;
    public event EventHandler<string>? SyncStatusChanged;

    public CloudSyncService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Start heartbeat monitoring (every 60 seconds)
        _heartbeatTimer = new Timer(SendHeartbeatAsync, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
        
        _ = LoadConfigurationAsync();
    }

    private async Task LoadConfigurationAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            _cloudApiUrl = config.GetValueOrDefault("cloud_url", "https://orderweb.net/api");
            _tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            _restaurantSlug = config.GetValueOrDefault("restaurant_slug", "");
            _apiKey = config.GetValueOrDefault("api_key", "");
            _isEnabled = config.GetValueOrDefault("is_enabled", "False") == "True";

            if (_isEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Cloud Sync configured: {_cloudApiUrl}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading cloud sync config: {ex.Message}");
        }
    }

    #region Heartbeat Monitoring

    /// <summary>
    /// Send heartbeat ping to cloud to indicate POS is online
    /// GET /api/pos/{tenant}/{restaurant}/heartbeat
    /// </summary>
    private async void SendHeartbeatAsync(object? state)
    {
        if (!_isEnabled || string.IsNullOrEmpty(_tenantSlug) || string.IsNullOrEmpty(_restaurantSlug))
            return;

        try
        {
            var url = $"{_cloudApiUrl}/pos/{_tenantSlug}/{_restaurantSlug}/heartbeat";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"💚 Heartbeat sent successfully: {result}");
                HeartbeatStatusChanged?.Invoke(this, "Online");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Heartbeat failed: {response.StatusCode}");
                HeartbeatStatusChanged?.Invoke(this, $"Warning: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Heartbeat error: {ex.Message}");
            HeartbeatStatusChanged?.Invoke(this, "Offline");
        }
    }

    /// <summary>
    /// Manually trigger heartbeat test
    /// </summary>
    public async Task<(bool Success, string Message)> TestHeartbeatAsync()
    {
        try
        {
            var url = $"{_cloudApiUrl}/pos/{_tenantSlug}/{_restaurantSlug}/heartbeat";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return (true, $"Connected: {result}");
            }
            else
            {
                return (false, $"Failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Daily Report Upload

    /// <summary>
    /// Generate and upload daily sales report to cloud
    /// POST /api/pos/{tenant}/{restaurant}/daily-report
    /// </summary>
    public async Task<(bool Success, string Message)> UploadDailyReportAsync(DateTime reportDate)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"📊 Generating daily report for {reportDate:yyyy-MM-dd}...");

            // Generate report from local database
            var report = await GenerateDailyReportAsync(reportDate);
            
            if (report == null)
            {
                return (false, "Failed to generate report");
            }

            // Upload to cloud
            var url = $"{_cloudApiUrl}/pos/{_tenantSlug}/{_restaurantSlug}/daily-report";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            
            var response = await _httpClient.PostAsJsonAsync(url, report);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Daily report uploaded successfully");
                
                // Log sync activity
                await LogSyncActivityAsync("daily_report", "upload", "completed", $"Report for {reportDate:yyyy-MM-dd}");
                
                SyncStatusChanged?.Invoke(this, "Daily report uploaded");
                return (true, "Report uploaded successfully");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Report upload failed: {error}");
                
                await LogSyncActivityAsync("daily_report", "upload", "failed", error);
                return (false, $"Upload failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Daily report error: {ex.Message}");
            await LogSyncActivityAsync("daily_report", "upload", "failed", ex.Message);
            return (false, $"Error: {ex.Message}");
        }
    }

    private async Task<DailyReportModel?> GenerateDailyReportAsync(DateTime reportDate)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();

            // Query local orders for the day
            command.CommandText = @"
                SELECT 
                    COUNT(*) as total_local_orders,
                    COALESCE(SUM(total_amount), 0) as total_local_sales,
                    COALESCE(SUM(CASE WHEN payment_method = 'cash' THEN total_amount ELSE 0 END), 0) as cash_sales,
                    COALESCE(SUM(CASE WHEN payment_method = 'card' THEN total_amount ELSE 0 END), 0) as card_sales
                FROM local_orders
                WHERE DATE(created_at) = @reportDate
                AND status IN ('completed', 'served')";

            command.Parameters.AddWithValue("@reportDate", reportDate.ToString("yyyy-MM-dd"));
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                var report = new DailyReportModel
                {
                    ReportDate = reportDate,
                    TenantSlug = _tenantSlug,
                    RestaurantSlug = _restaurantSlug,
                    TotalLocalOrders = reader.IsDBNull(reader.GetOrdinal("total_local_orders")) ? 0 : Convert.ToInt32(reader["total_local_orders"]),
                    TotalLocalSales = reader.IsDBNull(reader.GetOrdinal("total_local_sales")) ? 0 : Convert.ToDecimal(reader["total_local_sales"]),
                    CashSales = reader.IsDBNull(reader.GetOrdinal("cash_sales")) ? 0 : Convert.ToDecimal(reader["cash_sales"]),
                    CardSales = reader.IsDBNull(reader.GetOrdinal("card_sales")) ? 0 : Convert.ToDecimal(reader["card_sales"]),
                    GeneratedAt = DateTime.UtcNow
                };

                System.Diagnostics.Debug.WriteLine($"📊 Report generated: {report.TotalLocalOrders} orders, £{report.TotalLocalSales:F2} sales");
                return report;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error generating report: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Schedule automatic daily report upload at end of day (11:59 PM)
    /// </summary>
    public void ScheduleDailyReportUpload()
    {
        var now = DateTime.Now;
        var tomorrow = now.Date.AddDays(1);
        var uploadTime = tomorrow.AddHours(23).AddMinutes(59); // 11:59 PM
        var delay = uploadTime - now;

        System.Diagnostics.Debug.WriteLine($"📅 Daily report scheduled for {uploadTime:yyyy-MM-dd HH:mm:ss}");

        Task.Run(async () =>
        {
            await Task.Delay(delay);
            await UploadDailyReportAsync(DateTime.Now.Date);
            
            // Reschedule for next day
            ScheduleDailyReportUpload();
        });
    }

    #endregion

    #region Gift Card Transactions

    /// <summary>
    /// Update gift card balance in cloud when used at POS
    /// POST /api/pos/{tenant}/{restaurant}/gift-card-transaction
    /// </summary>
    public async Task<(bool Success, string Message)> RecordGiftCardTransactionAsync(
        string cardNumber, 
        decimal amountUsed, 
        decimal remainingBalance, 
        string orderReference)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"🎁 Recording gift card transaction: {cardNumber}, Amount: £{amountUsed:F2}");

            var transaction = new GiftCardTransactionModel
            {
                CardNumber = cardNumber,
                AmountUsed = amountUsed,
                RemainingBalance = remainingBalance,
                OrderReference = orderReference,
                TenantSlug = _tenantSlug,
                RestaurantSlug = _restaurantSlug,
                TransactionTime = DateTime.UtcNow
            };

            var url = $"{_cloudApiUrl}/pos/{_tenantSlug}/{_restaurantSlug}/gift-card-transaction";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            
            var response = await _httpClient.PostAsJsonAsync(url, transaction);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Gift card transaction recorded");
                await LogSyncActivityAsync("gift_card", "upload", "completed", $"Card: {cardNumber}, Amount: £{amountUsed:F2}");
                return (true, "Transaction recorded");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Gift card transaction failed: {error}");
                await LogSyncActivityAsync("gift_card", "upload", "failed", error);
                return (false, $"Failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Gift card error: {ex.Message}");
            await LogSyncActivityAsync("gift_card", "upload", "failed", ex.Message);
            return (false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Loyalty Points Transactions

    /// <summary>
    /// Update loyalty points in cloud when redeemed at POS
    /// POST /api/pos/{tenant}/{restaurant}/loyalty-transaction
    /// </summary>
    public async Task<(bool Success, string Message)> RecordLoyaltyTransactionAsync(
        string customerPhone,
        int pointsUsed,
        int pointsEarned,
        int remainingPoints,
        string orderReference)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"⭐ Recording loyalty transaction: {customerPhone}, Used: {pointsUsed}, Earned: {pointsEarned}");

            var transaction = new LoyaltyTransactionModel
            {
                CustomerPhone = customerPhone,
                PointsUsed = pointsUsed,
                PointsEarned = pointsEarned,
                RemainingPoints = remainingPoints,
                OrderReference = orderReference,
                TenantSlug = _tenantSlug,
                RestaurantSlug = _restaurantSlug,
                TransactionTime = DateTime.UtcNow
            };

            var url = $"{_cloudApiUrl}/pos/{_tenantSlug}/{_restaurantSlug}/loyalty-transaction";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            
            var response = await _httpClient.PostAsJsonAsync(url, transaction);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Loyalty transaction recorded");
                await LogSyncActivityAsync("loyalty", "upload", "completed", $"Customer: {customerPhone}, Points: {pointsUsed}");
                return (true, "Transaction recorded");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Loyalty transaction failed: {error}");
                await LogSyncActivityAsync("loyalty", "upload", "failed", error);
                return (false, $"Failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Loyalty error: {ex.Message}");
            await LogSyncActivityAsync("loyalty", "upload", "failed", ex.Message);
            return (false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Sync Logging

    private async Task LogSyncActivityAsync(string syncType, string direction, string status, string details)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO sync_log (sync_type, sync_direction, source_system, status, error_details, started_at)
                VALUES (@syncType, @direction, 'orderweb_cloud', @status, @details, @timestamp)";

            command.Parameters.AddWithValue("@syncType", syncType);
            command.Parameters.AddWithValue("@direction", direction);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@details", details);
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error logging sync: {ex.Message}");
        }
    }

    #endregion

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _httpClient?.Dispose();
    }
}

#region Models

public class DailyReportModel
{
    public DateTime ReportDate { get; set; }
    public string TenantSlug { get; set; } = "";
    public string RestaurantSlug { get; set; } = "";
    public int TotalLocalOrders { get; set; }
    public decimal TotalLocalSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public class GiftCardTransactionModel
{
    public string CardNumber { get; set; } = "";
    public decimal AmountUsed { get; set; }
    public decimal RemainingBalance { get; set; }
    public string OrderReference { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string RestaurantSlug { get; set; } = "";
    public DateTime TransactionTime { get; set; }
}

public class LoyaltyTransactionModel
{
    public string CustomerPhone { get; set; } = "";
    public int PointsUsed { get; set; }
    public int PointsEarned { get; set; }
    public int RemainingPoints { get; set; }
    public string OrderReference { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string RestaurantSlug { get; set; } = "";
    public DateTime TransactionTime { get; set; }
}

#endregion
