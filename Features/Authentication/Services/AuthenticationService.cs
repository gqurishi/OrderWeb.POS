using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class AuthenticationService
{
    private static AuthenticationService? _instance;
    private static readonly object _lock = new object();
    
    public static AuthenticationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = new AuthenticationService();
                }
            }
            return _instance;
        }
    }

    private readonly DatabaseService _databaseService;
    private readonly string _connectionString;
    private User? _currentUser;
    private readonly SemaphoreSlim _authCacheLock = new(1, 1);
    private readonly Dictionary<string, CachedAuthUser> _authCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _authCacheLoadedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan AuthCacheTtl = TimeSpan.FromMinutes(2);

    private sealed class CachedAuthUser
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public UserRole Role { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public bool IsActive { get; init; } = true;
    }

    // Default constructor for singleton pattern (backward compatibility)
    private AuthenticationService()
    {
        _databaseService = new DatabaseService();
        _connectionString = TerminalConfigurationService.GetPosConnectionString(pooled: false);
    }

    // Dependency injection constructor
    public AuthenticationService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _connectionString = TerminalConfigurationService.GetPosConnectionString(pooled: false);
    }

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;

    public User? GetCurrentUser()
    {
        return _currentUser;
    }

    public async Task WarmAuthenticationCacheAsync()
    {
        try
        {
            await EnsureAuthCacheAsync(forceReload: false);
        }
        catch
        {
            // Best effort only; login can still read directly when needed.
        }
    }

    public async Task<(bool Success, string Message, User? User)> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Username and password are required.", null);
            }

            // Keep login timeout short for faster feedback
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));

            await EnsureAuthCacheAsync(forceReload: false, cts.Token);
            if (!_authCache.TryGetValue(username, out var authUser))
            {
                var cacheIsFresh = _authCache.Count > 0 && DateTime.UtcNow - _authCacheLoadedAtUtc < AuthCacheTtl;
                if (!cacheIsFresh)
                {
                    // Cache may be stale after user changes; refresh once before failing.
                    await EnsureAuthCacheAsync(forceReload: true, cts.Token);
                    _authCache.TryGetValue(username, out authUser);
                }
            }

            if (authUser is null)
            {
                _ = LogUserActivityAsync(null, "login_failed", $"Login attempt with non-existent username: {username}");
                return (false, "Invalid username or password.", null);
            }

            if (!authUser.IsActive)
            {
                _ = LogUserActivityAsync(authUser.Id, "login_failed", $"Inactive user login attempt: {username}");
                return (false, "This user is inactive. Please contact an administrator.", null);
            }

            var isPinLogin = username == password && username.Length == 4 && username.All(char.IsDigit);
            if (!isPinLogin)
            {
                bool isValid;
                try
                {
                    isValid = await Task.Run(() => BCrypt.Net.BCrypt.Verify(password, authUser.PasswordHash), cts.Token);
                }
                catch (Exception bcryptEx)
                {
                    System.Diagnostics.Debug.WriteLine($"BCrypt verify error: {bcryptEx.Message}");
                    return (false, "Invalid username or password.", null);
                }

                if (!isValid)
                {
                    _ = LogUserActivityAsync(null, "login_failed", $"Failed login attempt for username: {username}");
                    return (false, "Invalid username or password.", null);
                }
            }

            var user = new User
            {
                Id = authUser.Id,
                Name = authUser.Name,
                Username = authUser.Username,
                PasswordHash = authUser.PasswordHash,
                Role = authUser.Role,
                CreatedAt = authUser.CreatedAt,
                UpdatedAt = authUser.UpdatedAt,
                IsActive = authUser.IsActive
            };

            _currentUser = user;

            // Log successful login (fire and forget - don't block login)
            _ = LogUserActivityAsync(user.Id, "login", "User logged in successfully");

            return (true, "Login successful.", user);
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("Login timeout after 5 seconds");
            return (false, "Login timeout. Please check your connection and try again.", null);
        }
        catch (MySqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"MySQL error: {ex.Message}");
            return (false, "Database connection failed. Please contact support.", null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
            return (false, "An error occurred during login. Please try again.", null);
        }
    }

    /// <summary>
    /// Validates a staff PIN without starting a POS login session.
    /// </summary>
    public async Task<(bool Success, string Message, User? User)> ValidatePinAsync(string pin)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                return (false, "Please enter a 4-digit PIN.", null);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await EnsureAuthCacheAsync(forceReload: false, cts.Token);

            if (!_authCache.TryGetValue(pin, out var authUser))
            {
                await EnsureAuthCacheAsync(forceReload: true, cts.Token);
                _authCache.TryGetValue(pin, out authUser);
            }

            if (authUser is null)
            {
                return (false, "Wrong PIN. Try again.", null);
            }

            if (!authUser.IsActive)
            {
                return (false, "This staff member is inactive.", null);
            }

            var user = new User
            {
                Id = authUser.Id,
                Name = authUser.Name,
                Username = authUser.Username,
                PasswordHash = authUser.PasswordHash,
                Role = authUser.Role,
                CreatedAt = authUser.CreatedAt,
                UpdatedAt = authUser.UpdatedAt,
                IsActive = authUser.IsActive
            };

            return (true, "PIN accepted.", user);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ValidatePin error: {ex.Message}");
            return (false, "Could not verify PIN. Please try again.", null);
        }
    }

    public async Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            await LogUserActivityAsync(_currentUser.Id, "logout", "User logged out");
            _currentUser = null;
        }
    }

    public async Task<(bool Success, string Message)> CreateUserAsync(string name, string username, string password, UserRole role)
    {
        return await CreateUserInternalAsync(name, username, password, role, requireAuth: true);
    }

    // Internal method for system initialization (bypasses auth check)
    private async Task<(bool Success, string Message)> CreateUserInternalAsync(string name, string username, string password, UserRole role, bool requireAuth = true)
    {
        try
        {
            // Only admins can create users (unless this is system initialization)
            if (requireAuth && _currentUser?.Role != UserRole.Admin)
            {
                return (false, "Access denied. Only administrators can create users.");
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Name, username and password are required.");
            }

            username = username.Trim();
            password = password.Trim();

            if (!IsFourDigitPin(username) || !IsFourDigitPin(password) || !string.Equals(username, password, StringComparison.Ordinal))
            {
                return (false, "PIN must be exactly 4 digits.");
            }

            var schemaResult = await EnsureAuthenticationSchemaAsync();
            if (!schemaResult.Success)
            {
                return schemaResult;
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            // Check if username already exists
            var checkQuery = "SELECT COUNT(*) FROM users WHERE username = @username";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@username", username);
            var userCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

            if (userCount > 0)
            {
                return (false, "Username already exists.");
            }

            // Hash the password
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            // Insert new user
            var insertQuery = @"INSERT INTO users (name, username, password_hash, role, is_active) 
                               VALUES (@name, @username, @password, @role, TRUE)";
            using var insertCommand = new MySqlCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@name", name);
            insertCommand.Parameters.AddWithValue("@username", username);
            insertCommand.Parameters.AddWithValue("@password", hashedPassword);
            insertCommand.Parameters.AddWithValue("@role", role.ToString().ToLower());

            await insertCommand.ExecuteNonQueryAsync();
            await EnsureAuthCacheAsync(forceReload: true);

            if (_currentUser != null)
            {
                await LogUserActivityAsync(_currentUser.Id, "user_created", $"Created new user: {name} ({username}) with role: {role}");
            }

            return (true, "User created successfully.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Create user error: {ex.Message}");
            AppDiagnostics.LogFatal("CreateUser", ex);
            return (false, $"Could not create user: {ex.Message}");
        }
    }

    // Public method for system initialization (doesn't require auth)
    public async Task<(bool Success, string Message)> EnsureUserExistsAsync(string name, string username, string password, UserRole role)
    {
        return await CreateUserInternalAsync(name, username, password, role, requireAuth: false);
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        var users = new List<User>();
        
        try
        {
            var schemaResult = await EnsureAuthenticationSchemaAsync();
            if (!schemaResult.Success)
            {
                return users;
            }

            // Only admins and managers can view all users
            if (_currentUser?.Role != UserRole.Admin && _currentUser?.Role != UserRole.Manager)
            {
                return users;
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = "SELECT id, name, username, role, is_active, created_at, updated_at FROM users ORDER BY is_active DESC, created_at DESC";
            using var command = new MySqlCommand(query, connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new User
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Name = reader["name"].ToString() ?? "",
                    Username = reader["username"].ToString() ?? "",
                    Role = Enum.Parse<UserRole>(reader["role"].ToString() ?? "User", true),
                    IsActive = Convert.ToBoolean(reader["is_active"]),
                    CreatedAt = Convert.ToDateTime(reader["created_at"]),
                    UpdatedAt = Convert.ToDateTime(reader["updated_at"])
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Get users error: {ex.Message}");
        }

        return users;
    }

    private static bool _activityTableInitialized = false;
    private static readonly object _tableInitLock = new object();

    private async Task LogUserActivityAsync(int? userId, string action, string details)
    {
        try
        {
            // Run activity logging in background - don't block login
            _ = Task.Run(async () =>
            {
                try
                {
                    using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                    await connection.OpenAsync();

                    // Create user_activities table only once per app session
                    if (!_activityTableInitialized)
                    {
                        lock (_tableInitLock)
                        {
                            if (!_activityTableInitialized)
                            {
                                var createTableQuery = @"
                                    CREATE TABLE IF NOT EXISTS user_activities (
                                        id INT AUTO_INCREMENT PRIMARY KEY,
                                        user_id INT NULL,
                                        action VARCHAR(50) NOT NULL,
                                        details TEXT,
                                        ip_address VARCHAR(45),
                                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
                                    )";
                                using var createCommand = new MySqlCommand(createTableQuery, connection);
                                createCommand.ExecuteNonQuery();
                                _activityTableInitialized = true;
                            }
                        }
                    }

                    // Insert activity log
                    var insertQuery = @"INSERT INTO user_activities (user_id, action, details, ip_address) 
                                       VALUES (@userId, @action, @details, @ipAddress)";
                    using var insertCommand = new MySqlCommand(insertQuery, connection);
                    insertCommand.Parameters.AddWithValue("@userId", userId.HasValue ? userId.Value : DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@action", action);
                    insertCommand.Parameters.AddWithValue("@details", details);
                    insertCommand.Parameters.AddWithValue("@ipAddress", "localhost");

                    await insertCommand.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background activity log error: {ex.Message}");
                }
            });
            
            // Don't await - let it run in background
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Log activity error: {ex.Message}");
        }
    }

    public bool HasPermission(UserRole requiredRole)
    {
        if (!IsAuthenticated) return false;

        // Admin has access to everything
        if (_currentUser!.Role == UserRole.Admin) return true;

        // Manager has access to Manager and User level features
        if (_currentUser.Role == UserRole.Manager && requiredRole != UserRole.Admin) return true;

        // User only has access to User level features
        return _currentUser.Role == UserRole.User && requiredRole == UserRole.User;
    }

    public async Task<(bool Success, string Message)> DeleteUserAsync(int userId)
    {
        try
        {
            var schemaResult = await EnsureAuthenticationSchemaAsync();
            if (!schemaResult.Success)
            {
                return (false, schemaResult.Message);
            }

            if (_currentUser?.Role != UserRole.Admin)
            {
                return (false, "Access denied. Only administrators can delete users.");
            }

            if (_currentUser.Id == userId)
            {
                return (false, "You cannot delete your own account.");
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            string? username = null;
            var role = string.Empty;
            const string checkQuery = "SELECT username, role FROM users WHERE id = @userId";
            using (var checkCommand = new MySqlCommand(checkQuery, connection))
            {
                checkCommand.Parameters.AddWithValue("@userId", userId);
                using var reader = await checkCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    username = reader["username"].ToString();
                    role = reader["role"].ToString() ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "User not found.");
            }

            if (string.Equals(role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                const string adminCountQuery = "SELECT COUNT(*) FROM users WHERE LOWER(role) = 'admin' AND is_active = TRUE";
                using var adminCountCommand = new MySqlCommand(adminCountQuery, connection);
                var activeAdminCount = Convert.ToInt32(await adminCountCommand.ExecuteScalarAsync());
                if (activeAdminCount <= 1)
                {
                    return (false, "You cannot delete the last active administrator.");
                }
            }

            await LogUserActivityAsync(_currentUser.Id, "user_deleted", $"Deleted user: {username} (ID: {userId})");

            const string deleteQuery = "DELETE FROM users WHERE id = @userId";
            using var deleteCommand = new MySqlCommand(deleteQuery, connection);
            deleteCommand.Parameters.AddWithValue("@userId", userId);
            var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

            if (rowsAffected > 0)
            {
                await EnsureAuthCacheAsync(forceReload: true);
                return (true, "User deleted successfully.");
            }

            return (false, "Failed to delete user.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete user error: {ex.Message}");
            return (false, "An error occurred while deleting the user.");
        }
    }

    public async Task<(bool Success, string Message)> DeactivateUserAsync(int userId)
    {
        try
        {
            var schemaResult = await EnsureAuthenticationSchemaAsync();
            if (!schemaResult.Success)
            {
                return (false, schemaResult.Message);
            }

            // Only admins can deactivate users
            if (_currentUser?.Role != UserRole.Admin)
            {
                return (false, "Access denied. Only administrators can deactivate users.");
            }

            // Cannot deactivate yourself
            if (_currentUser.Id == userId)
            {
                return (false, "You cannot deactivate your own account.");
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            // Check if user exists
            var checkQuery = "SELECT username, is_active FROM users WHERE id = @userId";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@userId", userId);
            string? username = null;
            var isActive = false;
            using (var reader = await checkCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    username = reader["username"].ToString();
                    isActive = Convert.ToBoolean(reader["is_active"]);
                }
            }

            if (string.IsNullOrEmpty(username))
            {
                return (false, "User not found.");
            }

            if (!isActive)
            {
                return (true, "User is already inactive.");
            }

            // Soft deactivate user to preserve time clock and report history.
            var deleteQuery = "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE id = @userId";
            using var deleteCommand = new MySqlCommand(deleteQuery, connection);
            deleteCommand.Parameters.AddWithValue("@userId", userId);
            
            var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                await LogUserActivityAsync(_currentUser.Id, "user_deactivated", $"Deactivated user: {username} (ID: {userId})");
                await EnsureAuthCacheAsync(forceReload: true);
                return (true, "User deactivated successfully.");
            }
            else
            {
                return (false, "Failed to deactivate user.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deactivate user error: {ex.Message}");
            return (false, "An error occurred while deactivating the user.");
        }
    }

    public async Task<(bool Success, string Message)> UpdateUserAsync(User updatedUser, string? newPin = null)
    {
        try
        {
            // Only admins can update other users, users can only update themselves
            if (_currentUser?.Role != UserRole.Admin && _currentUser?.Id != updatedUser.Id)
            {
                return (false, "Access denied. You can only update your own account or need admin privileges.");
            }

            var effectivePin = string.IsNullOrWhiteSpace(newPin)
                ? updatedUser.Username?.Trim() ?? string.Empty
                : newPin.Trim();

            if (!IsFourDigitPin(effectivePin))
            {
                return (false, "PIN must be exactly 4 digits.");
            }

            updatedUser.Username = effectivePin;

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            // Check if username is taken by another user
            var checkQuery = "SELECT id FROM users WHERE username = @username AND id != @id";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@username", updatedUser.Username);
            checkCommand.Parameters.AddWithValue("@id", updatedUser.Id);
            
            var existingUserId = await checkCommand.ExecuteScalarAsync();
            if (existingUserId != null)
            {
                return (false, "Username is already taken by another user.");
            }

            // Build update query - only update password if newPin is provided
            string updateQuery;
            if (!string.IsNullOrEmpty(newPin))
            {
                var hashedPin = BCrypt.Net.BCrypt.HashPassword(newPin);
                updateQuery = @"UPDATE users 
                              SET name = @name, username = @username, password_hash = @passwordHash, 
                                  role = @role, updated_at = NOW()
                              WHERE id = @id";
                
                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@name", updatedUser.Name);
                updateCommand.Parameters.AddWithValue("@username", updatedUser.Username);
                updateCommand.Parameters.AddWithValue("@passwordHash", hashedPin);
                updateCommand.Parameters.AddWithValue("@role", updatedUser.Role.ToString());
                updateCommand.Parameters.AddWithValue("@id", updatedUser.Id);
                
                var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    await LogUserActivityAsync(_currentUser.Id, "user_updated", $"Updated user: {updatedUser.Name} (ID: {updatedUser.Id}) with new PIN");
                    await EnsureAuthCacheAsync(forceReload: true);
                    
                    // Update current user if updating self
                    if (_currentUser.Id == updatedUser.Id)
                    {
                        _currentUser.Name = updatedUser.Name;
                        _currentUser.Username = updatedUser.Username;
                        _currentUser.Role = updatedUser.Role;
                    }
                    
                    return (true, "User updated successfully.");
                }
            }
            else
            {
                updateQuery = @"UPDATE users 
                              SET name = @name, username = @username, role = @role, updated_at = NOW()
                              WHERE id = @id";
                
                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@name", updatedUser.Name);
                updateCommand.Parameters.AddWithValue("@username", updatedUser.Username);
                updateCommand.Parameters.AddWithValue("@role", updatedUser.Role.ToString());
                updateCommand.Parameters.AddWithValue("@id", updatedUser.Id);
                
                var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    await LogUserActivityAsync(_currentUser.Id, "user_updated", $"Updated user: {updatedUser.Name} (ID: {updatedUser.Id})");
                    await EnsureAuthCacheAsync(forceReload: true);
                    
                    // Update current user if updating self
                    if (_currentUser.Id == updatedUser.Id)
                    {
                        _currentUser.Name = updatedUser.Name;
                        _currentUser.Username = updatedUser.Username;
                        _currentUser.Role = updatedUser.Role;
                    }
                    
                    return (true, "User updated successfully.");
                }
            }
            
            return (false, "Failed to update user.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update user error: {ex.Message}");
            return (false, "An error occurred while updating the user.");
        }
    }

    public async Task<(bool Success, string Message)> EnsureDefaultAdminUserAsync()
    {
        return await EnsureAuthenticationSchemaAsync();
    }

    public async Task<(bool Success, string Message)> EnsureAuthenticationSchemaAsync()
    {
        try
        {
            var databaseName = TerminalConfigurationService.GetActiveDatabaseName();
            await using (var serverConnection = new MySqlConnection(
                TerminalConfigurationService.GetPosConnectionString(includeDatabase: false, pooled: false)))
            {
                await serverConnection.OpenAsync();
                await using var createDatabaseCommand = new MySqlCommand(
                    $"CREATE DATABASE IF NOT EXISTS `{EscapeIdentifier(databaseName)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                    serverConnection);
                await createDatabaseCommand.ExecuteNonQueryAsync();
            }

            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            const string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(255) NULL,
                    username VARCHAR(50) UNIQUE NOT NULL,
                    password_hash VARCHAR(255) NOT NULL,
                    role VARCHAR(20) NOT NULL,
                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";

            using var createCommand = new MySqlCommand(createTableQuery, connection);
            await createCommand.ExecuteNonQueryAsync();

            var upgradeStatements = new[]
            {
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS name VARCHAR(255) NULL AFTER id",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE AFTER role",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"
            };

            foreach (var statement in upgradeStatements)
            {
                using var upgradeCommand = new MySqlCommand(statement, connection);
                await upgradeCommand.ExecuteNonQueryAsync();
            }

            return (true, "Database connection successful.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("EnsureAuthenticationSchema", ex);
            return (false, $"Database connection failed: {ex.Message}");
        }
    }

    private static string EscapeIdentifier(string identifier) =>
        string.IsNullOrWhiteSpace(identifier)
            ? "orderweb_pos"
            : identifier.Trim().Replace("`", "``", StringComparison.Ordinal);

    public async Task<bool> HasAnyUserAsync()
    {
        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            using var command = new MySqlCommand("SELECT COUNT(*) FROM users", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<(bool Success, string Message)> CreateInitialAdminUserAsync(string name, string pin)
    {
        return CreateUserInternalAsync(name.Trim(), pin.Trim(), pin.Trim(), UserRole.Admin, requireAuth: false);
    }

    public async Task<(bool Success, string Message)> TestDatabaseConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();

            return (true, "Database connected successfully.");
        }
        catch (Exception ex)
        {
            return (false, $"Database connection failed: {ex.Message}");
        }
    }

    private async Task EnsureAuthCacheAsync(bool forceReload, CancellationToken cancellationToken = default)
    {
        if (!forceReload &&
            _authCache.Count > 0 &&
            DateTime.UtcNow - _authCacheLoadedAtUtc < AuthCacheTtl)
        {
            return;
        }

        await _authCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceReload &&
                _authCache.Count > 0 &&
                DateTime.UtcNow - _authCacheLoadedAtUtc < AuthCacheTtl)
            {
                return;
            }

            var schemaResult = await EnsureAuthenticationSchemaAsync();
            if (!schemaResult.Success)
            {
                throw new InvalidOperationException(schemaResult.Message);
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync(cancellationToken);

            var query = "SELECT id, name, username, password_hash, role, is_active, created_at, updated_at FROM users";
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var cache = new Dictionary<string, CachedAuthUser>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                var username = reader["username"].ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username))
                {
                    continue;
                }

                cache[username] = new CachedAuthUser
                {
                    Id = Convert.ToInt32(reader["id"]),
                    Name = reader["name"].ToString() ?? string.Empty,
                    Username = username,
                    PasswordHash = reader["password_hash"].ToString() ?? string.Empty,
                    Role = Enum.Parse<UserRole>(reader["role"].ToString() ?? "User", true),
                    IsActive = Convert.ToBoolean(reader["is_active"]),
                    CreatedAt = Convert.ToDateTime(reader["created_at"]),
                    UpdatedAt = Convert.ToDateTime(reader["updated_at"])
                };
            }

            _authCache.Clear();
            foreach (var entry in cache)
            {
                _authCache[entry.Key] = entry.Value;
            }
            _authCacheLoadedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            _authCacheLock.Release();
        }
    }

    private static bool IsFourDigitPin(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length == 4
            && value.All(char.IsDigit);
    }
}
