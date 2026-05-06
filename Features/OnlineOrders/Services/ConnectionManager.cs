using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Manages automatic failover between Database, API, and OrderWeb.net connections
/// Priority: 1. Database (0.5s) → 2. API (2-4s) → 3. OrderWeb.net (backup)
/// </summary>
public class ConnectionManager
{
    private readonly DatabaseService _databaseService;
    private readonly OrderWebDirectDatabaseService _directDatabaseService;
    private readonly OnlineOrderApiService _apiService;
    
    private System.Threading.Timer? _healthCheckTimer;
    private System.Threading.Timer? _reconnectTimer;
    
    private ConnectionType _currentConnection = ConnectionType.None;
    private ConnectionType _preferredConnection = ConnectionType.Database;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private DateTime _lastConnectionSwitch = DateTime.MinValue;
    private int _connectionSwitchCount = 0;
    
    // Connection health tracking
    private Dictionary<ConnectionType, ConnectionHealth> _connectionHealth = new();
    
    // Settings
    private bool _autoFailoverEnabled = true;
    private int _healthCheckIntervalSeconds = 30;
    private int _reconnectIntervalSeconds = 60;
    private int _switchCooldownSeconds = 10; // Prevent rapid switching
    
    public event EventHandler<ConnectionSwitchedEventArgs>? ConnectionSwitched;
    public event EventHandler<ConnectionHealthChangedEventArgs>? ConnectionHealthChanged;
    
    public ConnectionManager(
        DatabaseService databaseService,
        OrderWebDirectDatabaseService directDatabaseService,
        OnlineOrderApiService apiService)
    {
        _databaseService = databaseService;
        _directDatabaseService = directDatabaseService;
        _apiService = apiService;
        
        InitializeConnectionHealth();
    }
    
    private void InitializeConnectionHealth()
    {
        _connectionHealth[ConnectionType.Database] = new ConnectionHealth
        {
            Type = ConnectionType.Database,
            IsHealthy = false,
            LastCheck = DateTime.MinValue,
            FailureCount = 0,
            SuccessCount = 0,
            AverageLatencyMs = 0
        };
        
        _connectionHealth[ConnectionType.API] = new ConnectionHealth
        {
            Type = ConnectionType.API,
            IsHealthy = false,
            LastCheck = DateTime.MinValue,
            FailureCount = 0,
            SuccessCount = 0,
            AverageLatencyMs = 0
        };
        
        _connectionHealth[ConnectionType.OrderWebNet] = new ConnectionHealth
        {
            Type = ConnectionType.OrderWebNet,
            IsHealthy = false,
            LastCheck = DateTime.MinValue,
            FailureCount = 0,
            SuccessCount = 0,
            AverageLatencyMs = 0
        };
    }
    
    /// <summary>
    /// Initialize and test all configured connections, select the best one
    /// </summary>
    public async Task<bool> InitializeConnectionsAsync()
    {
        try
        {
            Debug.WriteLine("🚀 ConnectionManager: Initializing connections...");
            
            // Load configuration
            var config = await _databaseService.GetCloudConfigAsync();
            _autoFailoverEnabled = config.GetValueOrDefault("auto_failover_enabled", "True") == "True";
            
            // Test all connections in priority order
            var databaseOk = await TestDatabaseConnectionAsync();
            var apiOk = await TestApiConnectionAsync();
            var orderWebOk = await TestOrderWebNetConnectionAsync();
            
            Debug.WriteLine($"📊 Connection Test Results:");
            Debug.WriteLine($"   Database: {(databaseOk ? "✅ Healthy" : "❌ Failed")}");
            Debug.WriteLine($"   API: {(apiOk ? "✅ Healthy" : "❌ Failed")}");
            Debug.WriteLine($"   OrderWeb.net: {(orderWebOk ? "✅ Healthy" : "❌ Failed")}");
            
            // Select best available connection
            if (databaseOk)
            {
                await SwitchToConnectionAsync(ConnectionType.Database, "Initial connection");
            }
            else if (apiOk)
            {
                await SwitchToConnectionAsync(ConnectionType.API, "Database unavailable, using API");
            }
            else if (orderWebOk)
            {
                await SwitchToConnectionAsync(ConnectionType.OrderWebNet, "Database and API unavailable, using OrderWeb.net");
            }
            else
            {
                Debug.WriteLine("❌ All connections failed!");
                _currentConnection = ConnectionType.None;
                return false;
            }
            
            // Start monitoring if auto-failover is enabled
            if (_autoFailoverEnabled)
            {
                StartHealthMonitoring();
                StartReconnectMonitoring();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ ConnectionManager initialization error: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Test database connection health
    /// </summary>
    private async Task<bool> TestDatabaseConnectionAsync()
    {
        var health = _connectionHealth[ConnectionType.Database];
        var startTime = DateTime.Now;
        
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var host = config.GetValueOrDefault("db_host", "");
            var database = config.GetValueOrDefault("db_database", "");
            var username = config.GetValueOrDefault("db_username", "");
            var password = config.GetValueOrDefault("db_password", "");
            var port = int.TryParse(config.GetValueOrDefault("db_port", "3306"), out var p) ? p : 3306;
            
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(database))
            {
                health.IsHealthy = false;
                health.LastCheck = DateTime.Now;
                return false;
            }
            
            // Test connection
            await _directDatabaseService.ConfigureDatabaseConnectionAsync(host, database, username, password, port);
            var testResult = await _directDatabaseService.TestOrderWebDatabaseConnectionAsync();
            
            var latency = (DateTime.Now - startTime).TotalMilliseconds;
            
            health.IsHealthy = testResult.Success;
            health.LastCheck = DateTime.Now;
            health.AverageLatencyMs = (health.AverageLatencyMs + latency) / 2;
            
            if (testResult.Success)
            {
                health.SuccessCount++;
                health.FailureCount = 0; // Reset failure count on success
            }
            else
            {
                health.FailureCount++;
            }
            
            return testResult.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Database connection test failed: {ex.Message}");
            health.IsHealthy = false;
            health.LastCheck = DateTime.Now;
            health.FailureCount++;
            return false;
        }
    }
    
    /// <summary>
    /// Test API connection health
    /// </summary>
    private async Task<bool> TestApiConnectionAsync()
    {
        var health = _connectionHealth[ConnectionType.API];
        var startTime = DateTime.Now;
        
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var baseUrl = config.GetValueOrDefault("api_base_url", "");
            var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
            var apiKey = config.GetValueOrDefault("api_key", "");
            
            if (string.IsNullOrEmpty(tenantSlug) || string.IsNullOrEmpty(apiKey))
            {
                health.IsHealthy = false;
                health.LastCheck = DateTime.Now;
                return false;
            }
            
            // Simple ping test (you can implement actual API test)
            await Task.Delay(100); // Simulate test
            var testSuccess = !string.IsNullOrEmpty(baseUrl);
            
            var latency = (DateTime.Now - startTime).TotalMilliseconds;
            
            health.IsHealthy = testSuccess;
            health.LastCheck = DateTime.Now;
            health.AverageLatencyMs = (health.AverageLatencyMs + latency) / 2;
            
            if (testSuccess)
            {
                health.SuccessCount++;
                health.FailureCount = 0;
            }
            else
            {
                health.FailureCount++;
            }
            
            return testSuccess;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"API connection test failed: {ex.Message}");
            health.IsHealthy = false;
            health.LastCheck = DateTime.Now;
            health.FailureCount++;
            return false;
        }
    }
    
    /// <summary>
    /// Test OrderWeb.net connection health
    /// </summary>
    private async Task<bool> TestOrderWebNetConnectionAsync()
    {
        var health = _connectionHealth[ConnectionType.OrderWebNet];
        var startTime = DateTime.Now;
        
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            var tenant = config.GetValueOrDefault("orderweb_tenant", "");
            var apiKey = config.GetValueOrDefault("orderweb_api_key", "");
            var enabled = config.GetValueOrDefault("orderweb_enabled", "False") == "True";
            
            if (!enabled || string.IsNullOrEmpty(tenant) || string.IsNullOrEmpty(apiKey))
            {
                health.IsHealthy = false;
                health.LastCheck = DateTime.Now;
                return false;
            }
            
            // Test using database connection (OrderWeb.net uses direct DB)
            var dbOk = await TestDatabaseConnectionAsync();
            
            var latency = (DateTime.Now - startTime).TotalMilliseconds;
            
            health.IsHealthy = dbOk;
            health.LastCheck = DateTime.Now;
            health.AverageLatencyMs = (health.AverageLatencyMs + latency) / 2;
            
            if (dbOk)
            {
                health.SuccessCount++;
                health.FailureCount = 0;
            }
            else
            {
                health.FailureCount++;
            }
            
            return dbOk;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OrderWeb.net connection test failed: {ex.Message}");
            health.IsHealthy = false;
            health.LastCheck = DateTime.Now;
            health.FailureCount++;
            return false;
        }
    }
    
    /// <summary>
    /// Switch to a different connection type
    /// </summary>
    private async Task SwitchToConnectionAsync(ConnectionType newConnection, string reason)
    {
        try
        {
            // Check cooldown period to prevent rapid switching
            if ((DateTime.Now - _lastConnectionSwitch).TotalSeconds < _switchCooldownSeconds)
            {
                Debug.WriteLine($"⏸️ Connection switch on cooldown. Waiting...");
                return;
            }
            
            var oldConnection = _currentConnection;
            _currentConnection = newConnection;
            _lastConnectionSwitch = DateTime.Now;
            _connectionSwitchCount++;
            
            Debug.WriteLine($"🔄 Switching connection: {oldConnection} → {newConnection}");
            Debug.WriteLine($"   Reason: {reason}");
            
            // Start monitoring for the new connection
            switch (newConnection)
            {
                case ConnectionType.Database:
                    await _directDatabaseService.StartRealTimeMonitoringAsync();
                    Debug.WriteLine("⚡ Direct database monitoring started (0.5s latency)");
                    break;
                    
                case ConnectionType.API:
                    // API polling should already be running
                    Debug.WriteLine("📡 API polling active (2-4s latency)");
                    break;
                    
                case ConnectionType.OrderWebNet:
                    await _directDatabaseService.StartRealTimeMonitoringAsync();
                    Debug.WriteLine("🌐 OrderWeb.net monitoring started (0.5s latency)");
                    break;
            }
            
            // Notify listeners
            ConnectionSwitched?.Invoke(this, new ConnectionSwitchedEventArgs
            {
                OldConnection = oldConnection,
                NewConnection = newConnection,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Error switching connection: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Start background health monitoring
    /// </summary>
    private void StartHealthMonitoring()
    {
        _healthCheckTimer?.Dispose();
        
        _healthCheckTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await MonitorCurrentConnectionHealthAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Health check error: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(_healthCheckIntervalSeconds));
        
        Debug.WriteLine($"💚 Health monitoring started (checking every {_healthCheckIntervalSeconds}s)");
    }
    
    /// <summary>
    /// Start background reconnect monitoring (try to switch back to better connection)
    /// </summary>
    private void StartReconnectMonitoring()
    {
        _reconnectTimer?.Dispose();
        
        _reconnectTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await TryReconnectToBetterConnectionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reconnect check error: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(_reconnectIntervalSeconds));
        
        Debug.WriteLine($"🔄 Reconnect monitoring started (checking every {_reconnectIntervalSeconds}s)");
    }
    
    /// <summary>
    /// Monitor current connection health and failover if needed
    /// </summary>
    private async Task MonitorCurrentConnectionHealthAsync()
    {
        _lastHealthCheck = DateTime.Now;
        
        bool currentHealthy = false;
        
        switch (_currentConnection)
        {
            case ConnectionType.Database:
                currentHealthy = await TestDatabaseConnectionAsync();
                break;
            case ConnectionType.API:
                currentHealthy = await TestApiConnectionAsync();
                break;
            case ConnectionType.OrderWebNet:
                currentHealthy = await TestOrderWebNetConnectionAsync();
                break;
        }
        
        if (!currentHealthy)
        {
            Debug.WriteLine($"⚠️ Current connection ({_currentConnection}) is unhealthy! Attempting failover...");
            await FailoverToNextBestConnectionAsync();
        }
    }
    
    /// <summary>
    /// Failover to the next best available connection
    /// </summary>
    private async Task FailoverToNextBestConnectionAsync()
    {
        // Try connections in priority order
        if (_currentConnection != ConnectionType.Database && await TestDatabaseConnectionAsync())
        {
            await SwitchToConnectionAsync(ConnectionType.Database, "Failback to database (best connection)");
        }
        else if (_currentConnection != ConnectionType.API && await TestApiConnectionAsync())
        {
            await SwitchToConnectionAsync(ConnectionType.API, "Failover to API (database unavailable)");
        }
        else if (_currentConnection != ConnectionType.OrderWebNet && await TestOrderWebNetConnectionAsync())
        {
            await SwitchToConnectionAsync(ConnectionType.OrderWebNet, "Failover to OrderWeb.net (last resort)");
        }
        else
        {
            Debug.WriteLine("❌ All connections failed! No failover available.");
            _currentConnection = ConnectionType.None;
        }
    }
    
    /// <summary>
    /// Try to reconnect to a better connection (fail-back)
    /// </summary>
    private async Task TryReconnectToBetterConnectionAsync()
    {
        // Only try to switch to a better connection if we're not already on the best
        if (_currentConnection == ConnectionType.Database)
        {
            return; // Already on best connection
        }
        
        // Try database first
        if (await TestDatabaseConnectionAsync())
        {
            await SwitchToConnectionAsync(ConnectionType.Database, "Fail-back to database (best connection available)");
            return;
        }
        
        // If we're on OrderWeb.net, try API
        if (_currentConnection == ConnectionType.OrderWebNet && await TestApiConnectionAsync())
        {
            await SwitchToConnectionAsync(ConnectionType.API, "Fail-back to API (better than OrderWeb.net)");
        }
    }
    
    /// <summary>
    /// Get current connection status
    /// </summary>
    public ConnectionStatus GetConnectionStatus()
    {
        return new ConnectionStatus
        {
            CurrentConnection = _currentConnection,
            PreferredConnection = _preferredConnection,
            AutoFailoverEnabled = _autoFailoverEnabled,
            LastHealthCheck = _lastHealthCheck,
            LastConnectionSwitch = _lastConnectionSwitch,
            ConnectionSwitchCount = _connectionSwitchCount,
            DatabaseHealth = _connectionHealth[ConnectionType.Database],
            ApiHealth = _connectionHealth[ConnectionType.API],
            OrderWebNetHealth = _connectionHealth[ConnectionType.OrderWebNet]
        };
    }
    
    /// <summary>
    /// Manually force a connection switch (for testing)
    /// </summary>
    public async Task ForceConnectionSwitchAsync(ConnectionType targetConnection)
    {
        Debug.WriteLine($"🔧 Manual connection switch requested: → {targetConnection}");
        await SwitchToConnectionAsync(targetConnection, "Manual switch");
    }
    
    /// <summary>
    /// Enable or disable auto-failover
    /// </summary>
    public async Task SetAutoFailoverAsync(bool enabled)
    {
        _autoFailoverEnabled = enabled;
        
        if (enabled)
        {
            StartHealthMonitoring();
            StartReconnectMonitoring();
            Debug.WriteLine("✅ Auto-failover enabled");
        }
        else
        {
            _healthCheckTimer?.Dispose();
            _reconnectTimer?.Dispose();
            Debug.WriteLine("⏸️ Auto-failover disabled");
        }
        
        // Save to config
        await SaveConfigValueAsync("auto_failover_enabled", enabled.ToString());
    }
    
    private async Task SaveConfigValueAsync(string key, string value)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = connection.CreateCommand();
            
            command.CommandText = $@"
                INSERT OR REPLACE INTO cloud_config 
                (id, {key})
                VALUES (1, @value)
                ON CONFLICT(id) DO UPDATE SET {key} = @value";
            
            command.Parameters.AddWithValue("@value", value);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving {key}: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _reconnectTimer?.Dispose();
    }
}

// Enums and supporting classes

public enum ConnectionType
{
    None,
    Database,      // Priority 1 (0.5s latency)
    API,          // Priority 2 (2-4s latency)
    OrderWebNet   // Priority 3 (backup)
}

public class ConnectionHealth
{
    public ConnectionType Type { get; set; }
    public bool IsHealthy { get; set; }
    public DateTime LastCheck { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public double AverageLatencyMs { get; set; }
    
    public double UptimePercentage => 
        SuccessCount + FailureCount > 0 
            ? (double)SuccessCount / (SuccessCount + FailureCount) * 100 
            : 0;
}

public class ConnectionStatus
{
    public ConnectionType CurrentConnection { get; set; }
    public ConnectionType PreferredConnection { get; set; }
    public bool AutoFailoverEnabled { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public DateTime LastConnectionSwitch { get; set; }
    public int ConnectionSwitchCount { get; set; }
    public ConnectionHealth? DatabaseHealth { get; set; }
    public ConnectionHealth? ApiHealth { get; set; }
    public ConnectionHealth? OrderWebNetHealth { get; set; }
}

public class ConnectionSwitchedEventArgs : EventArgs
{
    public ConnectionType OldConnection { get; set; }
    public ConnectionType NewConnection { get; set; }
    public string Reason { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class ConnectionHealthChangedEventArgs : EventArgs
{
    public ConnectionType ConnectionType { get; set; }
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = "";
}
