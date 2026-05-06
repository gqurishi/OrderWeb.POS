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

    // Default constructor for singleton pattern (backward compatibility)
    private AuthenticationService()
    {
        _databaseService = new DatabaseService();
        _connectionString = "Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;Connection Timeout=5;";
    }

    // Dependency injection constructor
    public AuthenticationService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _connectionString = "Server=localhost;Database=Pos-net;Uid=root;Pwd=root;Port=3306;Connection Timeout=5;";
    }

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;

    public User? GetCurrentUser()
    {
        return _currentUser;
    }

    public async Task<(bool Success, string Message, User? User)> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Username and password are required.", null);
            }

            // Add 10-second timeout for the entire login operation
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            var loginTask = Task.Run(async () =>
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cts.Token);

                var query = "SELECT id, username, password_hash, role, created_at, updated_at FROM users WHERE username = @username";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@username", username);

                using var reader = await command.ExecuteReaderAsync(cts.Token);
                if (await reader.ReadAsync(cts.Token))
                {
                    var storedHash = reader["password_hash"].ToString() ?? "";
                    
                    System.Diagnostics.Debug.WriteLine($"🔐 Login attempt for: {username}");
                    System.Diagnostics.Debug.WriteLine($"   Stored hash: {storedHash.Substring(0, Math.Min(30, storedHash.Length))}...");
                    System.Diagnostics.Debug.WriteLine($"   Password to verify: {password}");
                    
                    // Verify password using BCrypt
                    bool isValid = false;
                    try
                    {
                        isValid = BCrypt.Net.BCrypt.Verify(password, storedHash);
                        System.Diagnostics.Debug.WriteLine($"   BCrypt verification result: {isValid}");
                    }
                    catch (Exception bcryptEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ❌ BCrypt error: {bcryptEx.Message}");
                    }
                    
                    if (isValid)
                    {
                        var user = new User
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Username = reader["username"].ToString() ?? "",
                            PasswordHash = storedHash,
                            Role = Enum.Parse<UserRole>(reader["role"].ToString() ?? "User", true),
                            CreatedAt = Convert.ToDateTime(reader["created_at"]),
                            UpdatedAt = Convert.ToDateTime(reader["updated_at"])
                        };

                        _currentUser = user;
                        
                        // Log successful login (fire and forget - don't block login)
                        _ = LogUserActivityAsync(user.Id, "login", "User logged in successfully");
                        
                        return (true, "Login successful.", user);
                    }
                    else
                    {
                        // Log failed login attempt (fire and forget)
                        _ = LogUserActivityAsync(null, "login_failed", $"Failed login attempt for username: {username}");
                        return (false, "Invalid username or password.", (User?)null);
                    }
                }
                else
                {
                    _ = LogUserActivityAsync(null, "login_failed", $"Login attempt with non-existent username: {username}");
                    return (false, "Invalid username or password.", (User?)null);
                }
            }, cts.Token);

            return await loginTask;
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("Login timeout after 10 seconds");
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

            if (password.Length < 3)
            {
                return (false, "Password must be at least 3 characters long.");
            }

            using var connection = new MySqlConnection(_connectionString);
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
            var insertQuery = @"INSERT INTO users (name, username, password_hash, role) 
                               VALUES (@name, @username, @password, @role)";
            using var insertCommand = new MySqlCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@name", name);
            insertCommand.Parameters.AddWithValue("@username", username);
            insertCommand.Parameters.AddWithValue("@password", hashedPassword);
            insertCommand.Parameters.AddWithValue("@role", role.ToString().ToLower());

            await insertCommand.ExecuteNonQueryAsync();

            if (_currentUser != null)
            {
                await LogUserActivityAsync(_currentUser.Id, "user_created", $"Created new user: {name} ({username}) with role: {role}");
            }

            return (true, "User created successfully.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Create user error: {ex.Message}");
            return (false, "An error occurred while creating the user.");
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
            // Only admins and managers can view all users
            if (_currentUser?.Role != UserRole.Admin && _currentUser?.Role != UserRole.Manager)
            {
                return users;
            }

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT id, name, username, role, created_at, updated_at FROM users ORDER BY created_at DESC";
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
                    using var connection = new MySqlConnection(_connectionString);
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
            // Only admins can delete users
            if (_currentUser?.Role != UserRole.Admin)
            {
                return (false, "Access denied. Only administrators can delete users.");
            }

            // Cannot delete yourself
            if (_currentUser.Id == userId)
            {
                return (false, "You cannot delete your own account.");
            }

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if user exists
            var checkQuery = "SELECT username FROM users WHERE id = @userId";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@userId", userId);
            var username = await checkCommand.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(username))
            {
                return (false, "User not found.");
            }

            // Delete user
            var deleteQuery = "DELETE FROM users WHERE id = @userId";
            using var deleteCommand = new MySqlCommand(deleteQuery, connection);
            deleteCommand.Parameters.AddWithValue("@userId", userId);
            
            var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                await LogUserActivityAsync(_currentUser.Id, "user_deleted", $"Deleted user: {username} (ID: {userId})");
                return (true, "User deleted successfully.");
            }
            else
            {
                return (false, "Failed to delete user.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete user error: {ex.Message}");
            return (false, "An error occurred while deleting the user.");
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

            using var connection = new MySqlConnection(_connectionString);
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
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Create users table if it doesn't exist
            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    username VARCHAR(50) UNIQUE NOT NULL,
                    password_hash VARCHAR(255) NOT NULL,
                    role VARCHAR(20) NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";
            using var createCommand = new MySqlCommand(createTableQuery, connection);
            await createCommand.ExecuteNonQueryAsync();

            // Check if admin user exists
            var checkAdminQuery = "SELECT COUNT(*) FROM users WHERE username = 'admin' AND role = 'admin'";
            using var checkCommand = new MySqlCommand(checkAdminQuery, connection);
            var adminCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

            if (adminCount == 0)
            {
                // Create default admin user if it doesn't exist
                var adminPassword = BCrypt.Net.BCrypt.HashPassword("admin123");
                var insertAdminQuery = @"
                    INSERT INTO users (username, password_hash, role) 
                    VALUES ('admin', @password, 'admin')";
                using var insertCommand = new MySqlCommand(insertAdminQuery, connection);
                insertCommand.Parameters.AddWithValue("@password", adminPassword);
                
                await insertCommand.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine("Default admin user created successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Admin user already exists");
            }

            return (true, "Database connection successful. Admin user ready.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database setup error: {ex.Message}");
            return (false, $"Database connection failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> TestDatabaseConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = new MySqlCommand("SELECT COUNT(*) FROM users", connection);
            var userCount = await command.ExecuteScalarAsync();
            
            return (true, $"Database connected successfully. Found {userCount} users.");
        }
        catch (Exception ex)
        {
            return (false, $"Database connection failed: {ex.Message}");
        }
    }
}