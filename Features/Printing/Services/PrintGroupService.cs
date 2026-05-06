using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing print groups and printer routing
    /// </summary>
    public class PrintGroupService
    {
        private readonly string _connectionString;

        public PrintGroupService()
        {
            _connectionString = "Server=localhost;Database=Pos-net;User=root;Password=root;";
        }

        /// <summary>
        /// Get all print groups
        /// </summary>
        public async Task<List<PrintGroup>> GetAllPrintGroupsAsync()
        {
            var groups = new List<PrintGroup>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, printer_ip, printer_port, printer_type, 
                           is_active, color_code, display_order, created_at, updated_at
                    FROM print_groups
                    ORDER BY display_order ASC, name ASC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    groups.Add(new PrintGroup
                    {
                        Id = reader.GetString("id"),
                        Name = reader.GetString("name"),
                        PrinterIp = reader.IsDBNull(reader.GetOrdinal("printer_ip")) ? null : reader.GetString("printer_ip"),
                        PrinterPort = reader.GetInt32("printer_port"),
                        PrinterType = reader.GetString("printer_type"),
                        IsActive = reader.GetBoolean("is_active"),
                        ColorCode = reader.GetString("color_code"),
                        DisplayOrder = reader.GetInt32("display_order"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        UpdatedAt = reader.GetDateTime("updated_at")
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to get print groups: {ex.Message}");
            }

            return groups;
        }

        /// <summary>
        /// Get active print groups only
        /// </summary>
        public async Task<List<PrintGroup>> GetActivePrintGroupsAsync()
        {
            var allGroups = await GetAllPrintGroupsAsync();
            return allGroups.Where(g => g.IsActive).ToList();
        }

        /// <summary>
        /// Get print group by ID
        /// </summary>
        public async Task<PrintGroup?> GetPrintGroupByIdAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, printer_ip, printer_port, printer_type, 
                           is_active, color_code, display_order, created_at, updated_at
                    FROM print_groups
                    WHERE id = @id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new PrintGroup
                    {
                        Id = reader.GetString("id"),
                        Name = reader.GetString("name"),
                        PrinterIp = reader.IsDBNull(reader.GetOrdinal("printer_ip")) ? null : reader.GetString("printer_ip"),
                        PrinterPort = reader.GetInt32("printer_port"),
                        PrinterType = reader.GetString("printer_type"),
                        IsActive = reader.GetBoolean("is_active"),
                        ColorCode = reader.GetString("color_code"),
                        DisplayOrder = reader.GetInt32("display_order"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        UpdatedAt = reader.GetDateTime("updated_at")
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to get print group: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Create new print group
        /// </summary>
        public async Task<bool> CreatePrintGroupAsync(PrintGroup group)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                if (string.IsNullOrEmpty(group.Id))
                {
                    group.Id = Guid.NewGuid().ToString();
                }

                var query = @"
                    INSERT INTO print_groups 
                    (id, name, printer_ip, printer_port, printer_type, is_active, color_code, display_order)
                    VALUES 
                    (@id, @name, @printerIp, @printerPort, @printerType, @isActive, @colorCode, @displayOrder)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", group.Id);
                command.Parameters.AddWithValue("@name", group.Name);
                command.Parameters.AddWithValue("@printerIp", (object?)group.PrinterIp ?? DBNull.Value);
                command.Parameters.AddWithValue("@printerPort", group.PrinterPort);
                command.Parameters.AddWithValue("@printerType", group.PrinterType);
                command.Parameters.AddWithValue("@isActive", group.IsActive);
                command.Parameters.AddWithValue("@colorCode", group.ColorCode);
                command.Parameters.AddWithValue("@displayOrder", group.DisplayOrder);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to create print group: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update existing print group
        /// </summary>
        public async Task<bool> UpdatePrintGroupAsync(PrintGroup group)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE print_groups 
                    SET name = @name,
                        printer_ip = @printerIp,
                        printer_port = @printerPort,
                        printer_type = @printerType,
                        is_active = @isActive,
                        color_code = @colorCode,
                        display_order = @displayOrder,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", group.Id);
                command.Parameters.AddWithValue("@name", group.Name);
                command.Parameters.AddWithValue("@printerIp", (object?)group.PrinterIp ?? DBNull.Value);
                command.Parameters.AddWithValue("@printerPort", group.PrinterPort);
                command.Parameters.AddWithValue("@printerType", group.PrinterType);
                command.Parameters.AddWithValue("@isActive", group.IsActive);
                command.Parameters.AddWithValue("@colorCode", group.ColorCode);
                command.Parameters.AddWithValue("@displayOrder", group.DisplayOrder);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to update print group: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete print group
        /// </summary>
        public async Task<bool> DeletePrintGroupAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if any items use this print group
                var checkQuery = "SELECT COUNT(*) FROM FoodMenuItems WHERE print_group_id = @id";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@id", id);
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                if (count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARNING] Cannot delete print group - {count} items are using it");
                    return false;
                }

                var query = "DELETE FROM print_groups WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to delete print group: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Toggle print group active status
        /// </summary>
        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE print_groups 
                    SET is_active = NOT is_active,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to toggle print group: {ex.Message}");
                return false;
            }
        }
    }
}
