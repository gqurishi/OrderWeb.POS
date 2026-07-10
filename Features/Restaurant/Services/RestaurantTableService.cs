using MySqlConnector;
using POS_in_NET.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    /// <summary>
    /// Service for managing restaurant tables with MariaDB database
    /// </summary>
    public class RestaurantTableService
    {
        private readonly DatabaseService _databaseService;

        public RestaurantTableService()
        {
            _databaseService = new DatabaseService();
        }

        /// <summary>
        /// Get all active tables with floor names
        /// </summary>
        public async Task<List<RestaurantTable>> GetAllTablesAsync()
        {
            var tables = new List<RestaurantTable>();

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // First, check if PositionX/PositionY columns exist
                bool hasPositionColumns = await CheckPositionColumnsExistAsync(connection);
                System.Diagnostics.Debug.WriteLine($"HasPositionColumns: {hasPositionColumns}");
                
                string query;
                if (hasPositionColumns)
                {
                    query = @"
                        SELECT 
                            t.Id,
                            t.TableNumber,
                            t.FloorId,
                            t.Capacity,
                            t.Shape,
                            t.Status,
                            t.TableDesignIcon,
                            COALESCE(t.PositionX, 0) AS PositionX,
                            COALESCE(t.PositionY, 0) AS PositionY,
                            t.CreatedDate,
                            t.UpdatedDate,
                            t.IsActive,
                            f.Name AS FloorName
                        FROM RestaurantTables t
                        INNER JOIN Floors f ON t.FloorId = f.Id
                        WHERE t.IsActive = 1 AND f.IsActive = 1
                        ORDER BY f.Name, t.TableNumber";
                }
                else
                {
                    query = @"
                        SELECT 
                            t.Id,
                            t.TableNumber,
                            t.FloorId,
                            t.Capacity,
                            t.Shape,
                            t.Status,
                            t.TableDesignIcon,
                            0 AS PositionX,
                            0 AS PositionY,
                            t.CreatedDate,
                            t.UpdatedDate,
                            t.IsActive,
                            f.Name AS FloorName
                        FROM RestaurantTables t
                        INNER JOIN Floors f ON t.FloorId = f.Id
                        WHERE t.IsActive = 1 AND f.IsActive = 1
                        ORDER BY f.Name, t.TableNumber";
                }

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(new RestaurantTable
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        TableNumber = reader.GetString(reader.GetOrdinal("TableNumber")),
                        FloorId = reader.GetInt32(reader.GetOrdinal("FloorId")),
                        Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
                        Shape = Enum.Parse<TableShape>(reader.GetString(reader.GetOrdinal("Shape"))),
                        Status = Enum.Parse<TableStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                        TableDesignIcon = reader.IsDBNull(reader.GetOrdinal("TableDesignIcon")) ? "table_1.png" : reader.GetString(reader.GetOrdinal("TableDesignIcon")),
                        PositionX = reader.GetInt32(reader.GetOrdinal("PositionX")),
                        PositionY = reader.GetInt32(reader.GetOrdinal("PositionY")),
                        CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                        UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        FloorName = reader.GetString(reader.GetOrdinal("FloorName"))
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"Total tables loaded: {tables.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error getting tables: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }

            return tables;
        }
        
        /// <summary>
        /// Check if PositionX/PositionY columns exist in RestaurantTables
        /// </summary>
        private async Task<bool> CheckPositionColumnsExistAsync(MySqlConnection connection)
        {
            try
            {
                var checkQuery = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND TABLE_NAME = 'RestaurantTables' 
                    AND COLUMN_NAME = 'PositionX'";
                    
                using var cmd = new MySqlCommand(checkQuery, connection);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get tables for a specific floor
        /// </summary>
        public async Task<List<RestaurantTable>> GetTablesByFloorAsync(int floorId)
        {
            var tables = new List<RestaurantTable>();

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Check if PositionX/PositionY columns exist
                bool hasPositionColumns = await CheckPositionColumnsExistAsync(connection);
                
                string query;
                if (hasPositionColumns)
                {
                    query = @"
                        SELECT 
                            t.Id,
                            t.TableNumber,
                            t.FloorId,
                            t.Capacity,
                            t.Shape,
                            t.Status,
                            t.TableDesignIcon,
                            COALESCE(t.PositionX, 0) AS PositionX,
                            COALESCE(t.PositionY, 0) AS PositionY,
                            t.CreatedDate,
                            t.UpdatedDate,
                            t.IsActive,
                            f.Name AS FloorName
                        FROM RestaurantTables t
                        INNER JOIN Floors f ON t.FloorId = f.Id
                        WHERE t.FloorId = @FloorId AND t.IsActive = 1 AND f.IsActive = 1
                        ORDER BY t.TableNumber";
                }
                else
                {
                    query = @"
                        SELECT 
                            t.Id,
                            t.TableNumber,
                            t.FloorId,
                            t.Capacity,
                            t.Shape,
                            t.Status,
                            t.TableDesignIcon,
                            0 AS PositionX,
                            0 AS PositionY,
                            t.CreatedDate,
                            t.UpdatedDate,
                            t.IsActive,
                            f.Name AS FloorName
                        FROM RestaurantTables t
                        INNER JOIN Floors f ON t.FloorId = f.Id
                        WHERE t.FloorId = @FloorId AND t.IsActive = 1 AND f.IsActive = 1
                        ORDER BY t.TableNumber";
                }

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FloorId", floorId);
                
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(new RestaurantTable
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        TableNumber = reader.GetString(reader.GetOrdinal("TableNumber")),
                        FloorId = reader.GetInt32(reader.GetOrdinal("FloorId")),
                        Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
                        Shape = Enum.Parse<TableShape>(reader.GetString(reader.GetOrdinal("Shape"))),
                        Status = Enum.Parse<TableStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                        TableDesignIcon = reader.IsDBNull(reader.GetOrdinal("TableDesignIcon")) ? "table_1.png" : reader.GetString(reader.GetOrdinal("TableDesignIcon")),
                        PositionX = reader.GetInt32(reader.GetOrdinal("PositionX")),
                        PositionY = reader.GetInt32(reader.GetOrdinal("PositionY")),
                        CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                        UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        FloorName = reader.GetString(reader.GetOrdinal("FloorName"))
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"Tables loaded for floor {floorId}: {tables.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error getting tables by floor: {ex.Message}");
            }

            return tables;
        }

        /// <summary>
        /// Get a specific table by ID
        /// </summary>
        public async Task<RestaurantTable?> GetTableByIdAsync(int id)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                var query = @"
                    SELECT 
                        t.Id,
                        t.TableNumber,
                        t.FloorId,
                        t.Capacity,
                        t.Shape,
                        t.Status,
                        t.TableDesignIcon,
                        t.PositionX,
                        t.PositionY,
                        t.CreatedDate,
                        t.UpdatedDate,
                        t.IsActive,
                        f.Name AS FloorName
                    FROM RestaurantTables t
                    INNER JOIN Floors f ON t.FloorId = f.Id
                    WHERE t.Id = @Id AND t.IsActive = 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new RestaurantTable
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        TableNumber = reader.GetString(reader.GetOrdinal("TableNumber")),
                        FloorId = reader.GetInt32(reader.GetOrdinal("FloorId")),
                        Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
                        Shape = Enum.Parse<TableShape>(reader.GetString(reader.GetOrdinal("Shape"))),
                        Status = Enum.Parse<TableStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                        TableDesignIcon = reader.IsDBNull(reader.GetOrdinal("TableDesignIcon")) ? "table_1.png" : reader.GetString(reader.GetOrdinal("TableDesignIcon")),
                        PositionX = reader.IsDBNull(reader.GetOrdinal("PositionX")) ? 0 : reader.GetInt32(reader.GetOrdinal("PositionX")),
                        PositionY = reader.IsDBNull(reader.GetOrdinal("PositionY")) ? 0 : reader.GetInt32(reader.GetOrdinal("PositionY")),
                        CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                        UpdatedDate = reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        FloorName = reader.GetString(reader.GetOrdinal("FloorName"))
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error getting table by ID: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Update table position (for drag-and-drop in Visual Table Layout)
        /// </summary>
        public async Task<bool> UpdateTablePositionAsync(int tableId, int positionX, int positionY)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // First ensure columns exist
                bool hasPositionColumns = await CheckPositionColumnsExistAsync(connection);
                
                if (!hasPositionColumns)
                {
                    // Create the columns if they don't exist
                    System.Diagnostics.Debug.WriteLine("Creating PositionX/PositionY columns...");
                    try
                    {
                        var alterQuery1 = "ALTER TABLE RestaurantTables ADD COLUMN PositionX INT DEFAULT 0";
                        var alterQuery2 = "ALTER TABLE RestaurantTables ADD COLUMN PositionY INT DEFAULT 0";
                        
                        using var alterCmd1 = new MySqlCommand(alterQuery1, connection);
                        await alterCmd1.ExecuteNonQueryAsync();
                        
                        using var alterCmd2 = new MySqlCommand(alterQuery2, connection);
                        await alterCmd2.ExecuteNonQueryAsync();
                        
                        System.Diagnostics.Debug.WriteLine(" Position columns created successfully");
                    }
                    catch (Exception alterEx)
                    {
                        // Columns might already exist (race condition), ignore
                        System.Diagnostics.Debug.WriteLine($"Column creation: {alterEx.Message}");
                    }
                }
                
                var query = @"
                    UPDATE RestaurantTables 
                    SET PositionX = @PositionX,
                        PositionY = @PositionY,
                        UpdatedDate = NOW()
                    WHERE Id = @Id AND IsActive = 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", tableId);
                command.Parameters.AddWithValue("@PositionX", positionX);
                command.Parameters.AddWithValue("@PositionY", positionY);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    await PublishTableChangeSafelyAsync(
                        connection,
                        tableId.ToString(),
                        new { action = "position_updated", positionX, positionY });

                    System.Diagnostics.Debug.WriteLine($" Table position updated: ID={tableId} to ({positionX}, {positionY})");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($" No rows affected for table ID={tableId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error updating table position: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }

            return false;
        }

        /// <summary>
        /// Check if a table number is unique within a floor (excluding a specific table ID)
        /// </summary>
        public async Task<bool> IsTableNumberUniqueInFloorAsync(int floorId, string tableNumber, int? excludeId = null)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                var query = "SELECT COUNT(*) FROM RestaurantTables WHERE FloorId = @FloorId AND TableNumber = @TableNumber AND IsActive = 1";
                
                if (excludeId.HasValue)
                {
                    query += " AND Id != @ExcludeId";
                }

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FloorId", floorId);
                command.Parameters.AddWithValue("@TableNumber", tableNumber.Trim());
                
                if (excludeId.HasValue)
                {
                    command.Parameters.AddWithValue("@ExcludeId", excludeId.Value);
                }

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error checking table number uniqueness: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a new table
        /// </summary>
        public async Task<(bool success, string message, int? tableId)> CreateTableAsync(
            string tableNumber, 
            int floorId, 
            int capacity, 
            TableShape shape,
            string tableDesignIcon = "table_1.png")
        {
            try
            {
                // Validate table number
                tableNumber = tableNumber?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(tableNumber))
                {
                    return (false, "Table number cannot be empty", null);
                }

                // Validate capacity
                if (capacity <= 0)
                {
                    return (false, "Capacity must be greater than 0", null);
                }

                using var connection = await _databaseService.GetConnectionAsync();
                
                var query = @"
                    INSERT INTO RestaurantTables 
                        (TableNumber, FloorId, Capacity, Shape, Status, TableDesignIcon, CreatedDate, UpdatedDate, IsActive)
                    VALUES 
                        (@TableNumber, @FloorId, @Capacity, @Shape, @Status, @TableDesignIcon, NOW(), NOW(), 1);
                    SELECT LAST_INSERT_ID();";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableNumber", tableNumber);
                command.Parameters.AddWithValue("@FloorId", floorId);
                command.Parameters.AddWithValue("@Capacity", capacity);
                command.Parameters.AddWithValue("@Shape", shape.ToString());
                command.Parameters.AddWithValue("@Status", TableStatus.Available.ToString());
                command.Parameters.AddWithValue("@TableDesignIcon", tableDesignIcon);

                var newId = Convert.ToInt32(await command.ExecuteScalarAsync());

                await PublishTableChangeSafelyAsync(
                    connection,
                    newId.ToString(),
                    new { action = "created", tableNumber, floorId });

                System.Diagnostics.Debug.WriteLine($" Table created: {tableNumber} on floor ID {floorId} (ID: {newId}) with design: {tableDesignIcon}");
                return (true, $"Table '{tableNumber}' created successfully", newId);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                System.Diagnostics.Debug.WriteLine($" Duplicate table error creating table: {ex.Message}");
                return (false, $"Table '{tableNumber}' already exists on the selected floor", null);
            }
            catch (MySqlException ex) when (ex.Number == 1452)
            {
                System.Diagnostics.Debug.WriteLine($" Foreign key error creating table: {ex.Message}");
                return (false, "Selected floor does not exist", null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error creating table: {ex.Message}");
                return (false, $"Error creating table: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Update an existing table
        /// </summary>
        public async Task<(bool success, string message)> UpdateTableAsync(
            int id,
            string tableNumber, 
            int floorId, 
            int capacity, 
            TableShape shape,
            TableStatus status,
            string tableDesignIcon = "table_1.png")
        {
            try
            {
                // Validate table number
                tableNumber = tableNumber?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(tableNumber))
                {
                    return (false, "Table number cannot be empty");
                }

                // Validate capacity
                if (capacity <= 0)
                {
                    return (false, "Capacity must be greater than 0");
                }

                using var connection = await _databaseService.GetConnectionAsync();
                
                var query = @"
                    UPDATE RestaurantTables 
                    SET TableNumber = @TableNumber,
                        FloorId = @FloorId,
                        Capacity = @Capacity,
                        Shape = @Shape,
                        Status = @Status,
                        TableDesignIcon = @TableDesignIcon,
                        UpdatedDate = NOW()
                    WHERE Id = @Id AND IsActive = 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@TableNumber", tableNumber);
                command.Parameters.AddWithValue("@FloorId", floorId);
                command.Parameters.AddWithValue("@Capacity", capacity);
                command.Parameters.AddWithValue("@Shape", shape.ToString());
                command.Parameters.AddWithValue("@Status", status.ToString());
                command.Parameters.AddWithValue("@TableDesignIcon", tableDesignIcon);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    await PublishTableChangeSafelyAsync(
                        connection,
                        id.ToString(),
                        new { action = "updated", tableNumber, floorId, status = status.ToString() });

                    System.Diagnostics.Debug.WriteLine($" Table updated: {tableNumber} (ID: {id}) with design: {tableDesignIcon}");
                    return (true, $"Table '{tableNumber}' updated successfully");
                }
                else
                {
                    return (false, "Table not found or already deleted");
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                System.Diagnostics.Debug.WriteLine($" Duplicate table error updating table: {ex.Message}");
                return (false, $"Table '{tableNumber}' already exists on the selected floor");
            }
            catch (MySqlException ex) when (ex.Number == 1452)
            {
                System.Diagnostics.Debug.WriteLine($" Foreign key error updating table: {ex.Message}");
                return (false, "Selected floor does not exist");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error updating table: {ex.Message}");
                return (false, $"Error updating table: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a table
        /// </summary>
        public async Task<(bool success, string message)> DeleteTableAsync(int id)
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Hard delete
                var query = "DELETE FROM RestaurantTables WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    await PublishTableChangeSafelyAsync(
                        connection,
                        id.ToString(),
                        new { action = "deleted" });

                    System.Diagnostics.Debug.WriteLine($" Table deleted (ID: {id})");
                    return (true, "Table deleted successfully");
                }
                else
                {
                    return (true, "Table already deleted");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error deleting table: {ex.Message}");
                return (false, $"Error deleting table: {ex.Message}");
            }
        }

        /// <summary>
        /// Get table statistics
        /// </summary>
        public async Task<(int total, int available, int occupied, int reserved)> GetStatisticsAsync()
        {
            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                
                var query = @"
                    SELECT 
                        COUNT(*) AS Total,
                        SUM(CASE WHEN Status = 'Available' THEN 1 ELSE 0 END) AS Available,
                        SUM(CASE WHEN Status = 'Occupied' THEN 1 ELSE 0 END) AS Occupied,
                        SUM(CASE WHEN Status = 'Reserved' THEN 1 ELSE 0 END) AS Reserved
                    FROM RestaurantTables 
                    WHERE IsActive = 1";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return (
                        reader.GetInt32(reader.GetOrdinal("Total")),
                        reader.GetInt32(reader.GetOrdinal("Available")),
                        reader.GetInt32(reader.GetOrdinal("Occupied")),
                        reader.GetInt32(reader.GetOrdinal("Reserved"))
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error getting table statistics: {ex.Message}");
            }

            return (0, 0, 0, 0);
        }

        /// <summary>
        /// Check if any floors exist (prerequisite for creating tables)
        /// </summary>
        public async Task<bool> AnyFloorsExistAsync()
        {
            try
            {
                var floorService = new FloorService();
                var floors = await floorService.GetAllFloorsAsync();
                return floors.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error checking floors existence: {ex.Message}");
                return false;
            }
        }

        private static async Task PublishTableChangeSafelyAsync(
            MySqlConnection connection,
            string tableId,
            object payload)
        {
            try
            {
                await TerminalEventSyncService.PublishAsync(
                    connection,
                    AppDataChangeKind.TableLayout,
                    "table",
                    tableId,
                    null,
                    payload);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Table live-update publish skipped: {ex.Message}");
            }
        }
    }
}
