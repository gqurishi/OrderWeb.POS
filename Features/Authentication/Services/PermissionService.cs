using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public class PermissionService
{
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;

    private static readonly IReadOnlyList<PermissionDefinition> PermissionCatalog =
    [
        new(PermissionKeys.DashboardAdminView, "Dashboard Access"),
        new(PermissionKeys.WebOrdersView, "Web Orders Management"),
        new(PermissionKeys.GiftCardsManage, "Gift Card Management"),
        new(PermissionKeys.LoyaltyManage, "Loyalty Points Management"),
        new(PermissionKeys.ReservationManage, "Reservation Management"),
        new(PermissionKeys.ReportView, "Reports View"),
        new(PermissionKeys.InventoryView, "Inventory View"),
        new(PermissionKeys.SettingsManageBusiness, "Business Settings"),
        new(PermissionKeys.UsersManage, "User Management"),
        new(PermissionKeys.OrderNumberManage, "Order Number Settings"),
        new(PermissionKeys.PrintersManage, "Printer Setup"),
        new(PermissionKeys.FoodMenuManage, "Menu Management")
    ];

    public PermissionService(DatabaseService databaseService, AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
    }

    public async Task<bool> HasPermissionAsync(string permissionKey)
    {
        var currentUser = _authenticationService.CurrentUser;
        if (currentUser == null)
        {
            return false;
        }

        return await HasPermissionAsync(currentUser.Id, currentUser.Role, permissionKey);
    }

    public async Task<bool> HasPermissionAsync(int userId, UserRole role, string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
        {
            return false;
        }

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();

            // 1) User override has highest priority
            using (var userOverrideCmd = new MySqlCommand(@"
                SELECT is_allowed
                FROM user_permissions
                WHERE user_id = @userId AND permission_key = @permissionKey
                LIMIT 1", connection))
            {
                userOverrideCmd.Parameters.AddWithValue("@userId", userId);
                userOverrideCmd.Parameters.AddWithValue("@permissionKey", permissionKey);

                var overrideValue = await userOverrideCmd.ExecuteScalarAsync();
                if (overrideValue != null)
                {
                    return Convert.ToBoolean(overrideValue);
                }
            }

            // 2) Role default
            using var rolePermissionCmd = new MySqlCommand(@"
                SELECT 1
                FROM role_permissions
                WHERE role = @role AND permission_key = @permissionKey
                LIMIT 1", connection);

            rolePermissionCmd.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());
            rolePermissionCmd.Parameters.AddWithValue("@permissionKey", permissionKey);

            var roleAllowed = await rolePermissionCmd.ExecuteScalarAsync();
            return roleAllowed != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission check failed for '{permissionKey}': {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetUserPermissionOverrideAsync(int userId, string permissionKey, bool isAllowed)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(@"
                INSERT INTO user_permissions (user_id, permission_key, is_allowed)
                VALUES (@userId, @permissionKey, @isAllowed)
                ON DUPLICATE KEY UPDATE
                    is_allowed = VALUES(is_allowed),
                    updated_at = CURRENT_TIMESTAMP", connection);

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@permissionKey", permissionKey);
            command.Parameters.AddWithValue("@isAllowed", isAllowed);

            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set user permission override: {ex.Message}");
            return false;
        }
    }

    public async Task<bool?> GetUserPermissionOverrideAsync(int userId, string permissionKey)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(@"
                SELECT is_allowed
                FROM user_permissions
                WHERE user_id = @userId AND permission_key = @permissionKey
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@permissionKey", permissionKey);

            var value = await command.ExecuteScalarAsync();
            if (value == null)
            {
                return null;
            }

            return Convert.ToBoolean(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read user permission override: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ClearUserPermissionOverrideAsync(int userId, string permissionKey)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(@"
                DELETE FROM user_permissions
                WHERE user_id = @userId AND permission_key = @permissionKey", connection);

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@permissionKey", permissionKey);

            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clear user permission override: {ex.Message}");
            return false;
        }
    }

    public IReadOnlyList<PermissionDefinition> GetPermissionCatalog()
    {
        return PermissionCatalog;
    }

    public async Task<HashSet<string>> GetRolePermissionsAsync(UserRole role)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            using var command = new MySqlCommand(@"
                SELECT permission_key
                FROM role_permissions
                WHERE role = @role", connection);

            command.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = reader.GetString("permission_key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    allowed.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load role permissions for {role}: {ex.Message}");
        }

        return allowed;
    }

    public async Task<bool> SetRolePermissionAsync(UserRole role, string permissionKey, bool isAllowed)
    {
        try
        {
            using var connection = await _databaseService.GetConnectionAsync();

            if (isAllowed)
            {
                using var allowCommand = new MySqlCommand(@"
                    INSERT IGNORE INTO role_permissions (role, permission_key)
                    VALUES (@role, @permissionKey)", connection);

                allowCommand.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());
                allowCommand.Parameters.AddWithValue("@permissionKey", permissionKey);
                await allowCommand.ExecuteNonQueryAsync();
                return true;
            }

            using var denyCommand = new MySqlCommand(@"
                DELETE FROM role_permissions
                WHERE role = @role AND permission_key = @permissionKey", connection);

            denyCommand.Parameters.AddWithValue("@role", role.ToString().ToLowerInvariant());
            denyCommand.Parameters.AddWithValue("@permissionKey", permissionKey);
            await denyCommand.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set role permission: {ex.Message}");
            return false;
        }
    }
}

public record PermissionDefinition(string Key, string DisplayName);