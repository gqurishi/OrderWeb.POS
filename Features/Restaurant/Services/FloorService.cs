using MySqlConnector;
using POS_in_NET.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public class FloorService
    {
        public static event EventHandler<FloorChangedEventArgs>? FloorsChanged;

        private readonly DatabaseService _db;

        public FloorService()
        {
            _db = new DatabaseService();
        }

        // Get all active floors with table counts
        public async Task<List<Floor>> GetAllFloorsAsync()
        {
            List<Floor> floorList = new List<Floor>();

            try
            {
                using var connection = await _db.GetConnectionAsync();
                
                // Check if BackgroundImage column exists
                bool hasBackgroundColumn = await CheckBackgroundColumnExistsAsync(connection);
                System.Diagnostics.Debug.WriteLine($"HasBackgroundColumn: {hasBackgroundColumn}");
                
                string query;
                if (hasBackgroundColumn)
                {
                    query = @"
                        SELECT 
                            f.Id, 
                            f.Name, 
                            f.Description,
                            COALESCE(f.BackgroundImage, '') AS BackgroundImage,
                            f.CreatedDate, 
                            f.UpdatedDate, 
                            f.IsActive,
                            COUNT(t.Id) AS TableCount
                        FROM Floors f
                        LEFT JOIN RestaurantTables t ON f.Id = t.FloorId AND t.IsActive = 1
                        WHERE f.IsActive = 1 
                        GROUP BY f.Id, f.Name, f.Description, f.BackgroundImage, f.CreatedDate, f.UpdatedDate, f.IsActive
                        ORDER BY f.Id";
                }
                else
                {
                    query = @"
                        SELECT 
                            f.Id, 
                            f.Name, 
                            f.Description,
                            '' AS BackgroundImage,
                            f.CreatedDate, 
                            f.UpdatedDate, 
                            f.IsActive,
                            COUNT(t.Id) AS TableCount
                        FROM Floors f
                        LEFT JOIN RestaurantTables t ON f.Id = t.FloorId AND t.IsActive = 1
                        WHERE f.IsActive = 1 
                        GROUP BY f.Id, f.Name, f.Description, f.CreatedDate, f.UpdatedDate, f.IsActive
                        ORDER BY f.Id";
                }
                
                using var command = new MySqlCommand(query, connection);
                using var dataReader = await command.ExecuteReaderAsync();

                while (await dataReader.ReadAsync())
                {
                    Floor floorItem = new Floor
                    {
                        Id = dataReader.GetInt32(0),
                        Name = dataReader.GetString(1),
                        Description = dataReader.IsDBNull(2) ? string.Empty : dataReader.GetString(2),
                        BackgroundImage = dataReader.IsDBNull(3) ? string.Empty : dataReader.GetString(3),
                        CreatedDate = dataReader.IsDBNull(4) ? DateTime.Now : dataReader.GetDateTime(4),
                        UpdatedDate = dataReader.IsDBNull(5) ? DateTime.Now : dataReader.GetDateTime(5),
                        IsActive = dataReader.GetBoolean(6),
                        TableCount = dataReader.GetInt32(7)
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Loaded floor: {floorItem.Name} with {floorItem.TableCount} tables");
                    floorList.Add(floorItem);
                }
                
                System.Diagnostics.Debug.WriteLine($"Total floors loaded: {floorList.Count}");
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllFloorsAsync Error: {error.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {error.StackTrace}");
                // Don't throw - return empty list so UI can show "No floors" message
            }

            return floorList;
        }
        
        // Check if BackgroundImage column exists in Floors table
        private async Task<bool> CheckBackgroundColumnExistsAsync(MySqlConnection connection)
        {
            try
            {
                var checkQuery = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND TABLE_NAME = 'Floors' 
                    AND COLUMN_NAME = 'BackgroundImage'";
                    
                using var cmd = new MySqlCommand(checkQuery, connection);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                return false;
            }
        }

        // Get single floor by ID
        public async Task<Floor?> GetFloorByIdAsync(int floorId)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync();
                
                string query = "SELECT Id, Name, Description, BackgroundImage, CreatedDate, UpdatedDate, IsActive FROM Floors WHERE Id = @FloorId AND IsActive = 1";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FloorId", floorId);
                
                using var dataReader = await command.ExecuteReaderAsync();

                if (await dataReader.ReadAsync())
                {
                    Floor floorItem = new Floor
                    {
                        Id = dataReader.GetInt32(0),
                        Name = dataReader.GetString(1),
                        Description = dataReader.IsDBNull(2) ? string.Empty : dataReader.GetString(2),
                        BackgroundImage = dataReader.IsDBNull(3) ? string.Empty : dataReader.GetString(3),
                        CreatedDate = dataReader.IsDBNull(4) ? DateTime.Now : dataReader.GetDateTime(4),
                        UpdatedDate = dataReader.IsDBNull(5) ? DateTime.Now : dataReader.GetDateTime(5),
                        IsActive = dataReader.GetBoolean(6),
                        TableCount = 0
                    };
                    
                    return floorItem;
                }

                return null;
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"GetFloorByIdAsync Error: {error.Message}");
                return null;
            }
        }

        // Create new floor
        public async Task<(bool success, string message, int? floorId)> CreateFloorAsync(string floorName, string floorDescription = "")
        {
            try
            {
                floorName = floorName?.Trim() ?? string.Empty;
                
                if (string.IsNullOrWhiteSpace(floorName))
                    return (false, "Floor name is required", null);

                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                
                // Check if floor name already exists
                string checkQuery = "SELECT COUNT(*) FROM Floors WHERE LOWER(Name) = LOWER(@FloorName) AND IsActive = 1";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@FloorName", floorName);
                
                int existingCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                if (existingCount > 0)
                    return (false, $"Floor '{floorName}' already exists", null);

                // Insert new floor
                string insertQuery = "INSERT INTO Floors (Name, Description, CreatedDate, UpdatedDate, IsActive) VALUES (@FloorName, @FloorDescription, NOW(), NOW(), 1); SELECT LAST_INSERT_ID();";
                
                using var insertCommand = new MySqlCommand(insertQuery, connection);
                insertCommand.Parameters.AddWithValue("@FloorName", floorName);
                insertCommand.Parameters.AddWithValue("@FloorDescription", floorDescription?.Trim() ?? string.Empty);

                int newFloorId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());

                NotifyFloorsChanged(FloorChangeAction.Created, new Floor
                {
                    Id = newFloorId,
                    Name = floorName,
                    Description = floorDescription?.Trim() ?? string.Empty,
                    IsActive = true
                });
                AppDataRefreshService.RequestRefresh();
                
                return (true, $"Floor '{floorName}' created successfully", newFloorId);
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"CreateFloorAsync Error: {error.Message}");
                return (false, error.Message, null);
            }
        }

        // Update existing floor
        public async Task<(bool success, string message)> UpdateFloorAsync(int floorId, string floorName, string floorDescription = "")
        {
            try
            {
                floorName = floorName?.Trim() ?? string.Empty;
                
                if (string.IsNullOrWhiteSpace(floorName))
                    return (false, "Floor name is required");

                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                
                // Check if another floor has the same name
                string checkQuery = "SELECT COUNT(*) FROM Floors WHERE LOWER(Name) = LOWER(@FloorName) AND Id != @FloorId AND IsActive = 1";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@FloorName", floorName);
                checkCommand.Parameters.AddWithValue("@FloorId", floorId);
                
                int existingCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                if (existingCount > 0)
                    return (false, $"Floor '{floorName}' already exists");

                // Update floor
                string updateQuery = "UPDATE Floors SET Name = @FloorName, Description = @FloorDescription, UpdatedDate = NOW() WHERE Id = @FloorId AND IsActive = 1";
                
                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@FloorId", floorId);
                updateCommand.Parameters.AddWithValue("@FloorName", floorName);
                updateCommand.Parameters.AddWithValue("@FloorDescription", floorDescription?.Trim() ?? string.Empty);

                int affectedRows = await updateCommand.ExecuteNonQueryAsync();

                return affectedRows > 0 
                    ? await NotifyAndReturnFloorUpdateAsync(floorId, floorName, floorDescription) 
                    : (false, "Floor not found");
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateFloorAsync Error: {error.Message}");
                return (false, error.Message);
            }
        }

        // Delete floor (PERMANENT deletion from database)
        public async Task<(bool success, string message)> DeleteFloorAsync(int floorId)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                await using var transaction = await connection.BeginTransactionAsync();
                
                // Get table count before deletion (for user feedback)
                int tableCount = await GetTableCountForFloorAsync(floorId);
                
                using (var deleteTablesCommand = new MySqlCommand("DELETE FROM RestaurantTables WHERE FloorId = @FloorId", connection, transaction))
                {
                    deleteTablesCommand.Parameters.AddWithValue("@FloorId", floorId);
                    await deleteTablesCommand.ExecuteNonQueryAsync();
                }

                string deleteFloorQuery = "DELETE FROM Floors WHERE Id = @FloorId";
                using var deleteFloorCommand = new MySqlCommand(deleteFloorQuery, connection, transaction);
                deleteFloorCommand.Parameters.AddWithValue("@FloorId", floorId);
                int floorsDeleted = await deleteFloorCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
                
                if (floorsDeleted > 0)
                {
                    NotifyFloorsChanged(FloorChangeAction.Deleted, new Floor
                    {
                        Id = floorId,
                        Name = string.Empty,
                        IsActive = false
                    });
                    AppDataRefreshService.RequestRefresh();

                    string message = tableCount > 0 
                        ? $"Floor and {tableCount} table(s) permanently deleted" 
                        : "Floor permanently deleted";
                    return (true, message);
                }
                else
                {
                    return (false, "Floor not found");
                }
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"DeleteFloorAsync Error: {error.Message}");
                return (false, $"Delete failed: {error.Message}");
            }
        }

        // Get table count for a floor
        public async Task<int> GetTableCountForFloorAsync(int floorId)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync();
                
                string query = "SELECT COUNT(*) FROM RestaurantTables WHERE FloorId = @FloorId AND IsActive = 1";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FloorId", floorId);
                
                return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        // Update floor background image
        public async Task<bool> UpdateFloorBackgroundAsync(int floorId, string backgroundImagePath)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync();
                await EnsureBackgroundImageColumnExistsAsync(connection);
                
                string updateQuery = "UPDATE Floors SET BackgroundImage = @BackgroundImage, UpdatedDate = NOW() WHERE Id = @FloorId AND IsActive = 1";
                
                using var updateCommand = new MySqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@FloorId", floorId);
                updateCommand.Parameters.AddWithValue("@BackgroundImage", backgroundImagePath ?? string.Empty);

                int affectedRows = await updateCommand.ExecuteNonQueryAsync();

                if (affectedRows > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Floor background updated: ID={floorId}");
                    return true;
                }
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateFloorBackgroundAsync Error: {error.Message}");
            }

            return false;
        }

        private async Task EnsureBackgroundImageColumnExistsAsync(MySqlConnection connection)
        {
            if (await CheckBackgroundColumnExistsAsync(connection))
            {
                return;
            }

            try
            {
                using var alterCommand = new MySqlCommand("ALTER TABLE Floors ADD COLUMN BackgroundImage TEXT NULL", connection);
                await alterCommand.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine("Added missing Floors.BackgroundImage column");
            }
            catch (Exception ex)
            {
                // If another instance already created the column, we can continue safely.
                System.Diagnostics.Debug.WriteLine($"BackgroundImage column ensure warning: {ex.Message}");
            }
        }

        private async Task EnsureFloorSchemaCompatibilityAsync(MySqlConnection connection)
        {
            try
            {
                using var dropUniqueIndexCommand = new MySqlCommand("ALTER TABLE Floors DROP INDEX Name", connection);
                await dropUniqueIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor unique index compatibility warning: {ex.Message}");
            }

            try
            {
                using var ensureIndexCommand = new MySqlCommand("CREATE INDEX IF NOT EXISTS idx_name ON Floors (Name)", connection);
                await ensureIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor name index compatibility warning: {ex.Message}");
            }
        }

        private static void NotifyFloorsChanged(FloorChangeAction action, Floor? floor = null)
        {
            FloorsChanged?.Invoke(null, new FloorChangedEventArgs(action, floor));
        }

        private async Task<(bool success, string message)> NotifyAndReturnFloorUpdateAsync(int floorId, string floorName, string floorDescription)
        {
            NotifyFloorsChanged(FloorChangeAction.Updated, new Floor
            {
                Id = floorId,
                Name = floorName,
                Description = floorDescription?.Trim() ?? string.Empty,
                IsActive = true
            });
            AppDataRefreshService.RequestRefresh();

            return (true, $"Floor '{floorName}' updated successfully");
        }
    }

    public enum FloorChangeAction
    {
        Created,
        Updated,
        Deleted
    }

    public sealed class FloorChangedEventArgs : EventArgs
    {
        public FloorChangedEventArgs(FloorChangeAction action, Floor? floor)
        {
            Action = action;
            Floor = floor;
        }

        public FloorChangeAction Action { get; }
        public Floor? Floor { get; }
    }
}
