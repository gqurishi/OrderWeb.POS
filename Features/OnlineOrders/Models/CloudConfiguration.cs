namespace POS_in_NET.Models;

/// <summary>
/// Model representing OrderWeb.net cloud configuration
/// Stores REST API, WebSocket, and all connection settings
/// Matches OrderWeb.net's provided configuration structure
/// </summary>
public class CloudConfiguration
{
    public int Id { get; set; }
    
    // REST API Configuration (from OrderWeb.net REST API section)
    public string RestApiBaseUrl { get; set; } = "https://orderweb.net/api";
    public string RestApiEndpoint { get; set; } = ""; // e.g., /kitchen or /pos/pull-orders
    
    // WebSocket Configuration (from OrderWeb.net WebSocket section)  
    public string WebSocketUrl { get; set; } = "wss://orderweb.net/ws/pos";
    
    // Authentication (from OrderWeb.net API Configuration section)
    public string TenantSlug { get; set; } = ""; // Restaurant ID
    public string ApiKey { get; set; } = "";
    
    // Legacy fields (keep for backward compatibility)
    public string ApiBaseUrl { get; set; } = "https://orderweb.net/api";
    
    // Connection Settings
    public int ConnectionTimeout { get; set; } = 30;
    public int PollingIntervalSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    
    // Status
    public bool IsEnabled { get; set; } = false;
    public bool IsApiTested { get; set; } = false;
    public bool IsWebSocketTested { get; set; } = false;
    public string? ApiTestResult { get; set; }
    public string? WebSocketTestResult { get; set; }
    public DateTime? LastApiTest { get; set; }
    public DateTime? LastWebSocketTest { get; set; }
    
    // Features
    public bool AutoPrintEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    
    // Timestamps
    public DateTime? LastSync { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Check if configuration is complete
    /// </summary>
    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(ApiBaseUrl) &&
               !string.IsNullOrWhiteSpace(TenantSlug) &&
               !string.IsNullOrWhiteSpace(ApiKey);
    }

    /// <summary>
    /// Check if both API and WebSocket have been tested successfully
    /// </summary>
    public bool IsFullyTested()
    {
        return IsApiTested && IsWebSocketTested;
    }

    /// <summary>
    /// Get the full REST API URL
    /// Combines base URL with restaurant-specific endpoint
    /// </summary>
    public string GetFullRestApiUrl()
    {
        if (!string.IsNullOrWhiteSpace(RestApiEndpoint))
            return RestApiBaseUrl.TrimEnd('/') + "/" + RestApiEndpoint.TrimStart('/');
        
        // Fallback: use tenant slug
        if (!string.IsNullOrWhiteSpace(TenantSlug))
            return RestApiBaseUrl.TrimEnd('/') + "/" + TenantSlug;
            
        return RestApiBaseUrl;
    }

    /// <summary>
    /// Get the WebSocket URL from configuration or auto-generate
    /// </summary>
    public string GetWebSocketUrl()
    {
        if (!string.IsNullOrWhiteSpace(WebSocketUrl))
            return WebSocketUrl;

        // Auto-generate from REST API URL
        var wsBase = RestApiBaseUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://")
            .TrimEnd('/');
            
        if (!string.IsNullOrWhiteSpace(TenantSlug))
            return $"{wsBase}/ws/pos/{TenantSlug}";
            
        return $"{wsBase}/ws/pos";
    }

    /// <summary>
    /// Get connection status as display string
    /// </summary>
    public string GetConnectionStatus()
    {
        if (!IsConfigured())
            return "🔴 Not configured";

        if (IsEnabled && IsFullyTested())
            return "🟢 LIVE - Ready to receive orders";

        if (IsFullyTested())
            return "🟡 Configured and tested - Ready to start";

        if (IsConfigured())
            return "🟠 Configured - Not tested";

        return "🔴 Not configured";
    }
}
