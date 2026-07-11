using MySqlConnector;
using Microsoft.Maui.Storage;
using POS_in_NET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public class FloorService
    {
        public static event EventHandler<FloorChangedEventArgs>? FloorsChanged;

        private readonly DatabaseService _db;
        private const string BackgroundCacheFolderName = "floor-backgrounds";

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
                await EnsureFloorSchemaCompatibilityAsync(connection);
                
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
                await PublishFloorChangeSafelyAsync(
                    connection,
                    newFloorId.ToString(),
                    new { action = "created", floorName });
                AppDataRefreshService.RequestRefresh(AppDataChangeKind.TableLayout);
                
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
                    await PublishFloorChangeSafelyAsync(
                        connection,
                        floorId.ToString(),
                        new { action = "deleted", tableCount });

                    NotifyFloorsChanged(FloorChangeAction.Deleted, new Floor
                    {
                        Id = floorId,
                        Name = string.Empty,
                        IsActive = false
                    });
                    AppDataRefreshService.RequestRefresh(AppDataChangeKind.TableLayout);

                    string message = tableCount > 0 
                        ? $"Floor and {tableCount} table(s) permanently deleted" 
                        : "Floor permanently deleted";
                    return (true, message);
                }

                NotifyFloorsChanged(FloorChangeAction.Deleted, new Floor
                {
                    Id = floorId,
                    Name = string.Empty,
                    IsActive = false
                });
                AppDataRefreshService.RequestRefresh(AppDataChangeKind.TableLayout);

                return (true, "Floor already deleted");
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
                    await PublishFloorChangeSafelyAsync(
                        connection,
                        floorId.ToString(),
                        new { action = "background_updated" });

                    System.Diagnostics.Debug.WriteLine($" Floor background updated: ID={floorId}");
                    return true;
                }
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateFloorBackgroundAsync Error: {error.Message}");
            }

            return false;
        }

        public async Task<string?> SaveFloorBackgroundImageAsync(
            int floorId,
            string sourceFileName,
            string mimeType,
            byte[] imageBytes)
        {
            if (imageBytes.Length == 0)
            {
                return null;
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                await EnsureFloorBackgroundImagesTableAsync(connection);

                var contentHash = ComputeSha256(imageBytes);
                var safeFileName = BuildCachedFileName(floorId, contentHash, sourceFileName);
                var localPath = await WriteBackgroundCacheAsync(safeFileName, imageBytes);

                const string upsertSql = @"
                    INSERT INTO FloorBackgroundImages
                        (FloorId, FileName, MimeType, ContentHash, ImageData, UpdatedAt)
                    VALUES
                        (@FloorId, @FileName, @MimeType, @ContentHash, @ImageData, NOW())
                    ON DUPLICATE KEY UPDATE
                        FileName = VALUES(FileName),
                        MimeType = VALUES(MimeType),
                        ContentHash = VALUES(ContentHash),
                        ImageData = VALUES(ImageData),
                        UpdatedAt = NOW()";

                using (var command = new MySqlCommand(upsertSql, connection))
                {
                    command.Parameters.AddWithValue("@FloorId", floorId);
                    command.Parameters.AddWithValue("@FileName", safeFileName);
                    command.Parameters.AddWithValue("@MimeType", string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
                    command.Parameters.AddWithValue("@ContentHash", contentHash);
                    command.Parameters.Add("@ImageData", MySqlDbType.MediumBlob).Value = imageBytes;
                    await command.ExecuteNonQueryAsync();
                }

                await UpdateFloorBackgroundKeyAsync(connection, floorId, $"db:{contentHash}");

                await PublishFloorChangeSafelyAsync(
                    connection,
                    floorId.ToString(),
                    new { action = "background_image_updated", contentHash });

                return localPath;
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"SaveFloorBackgroundImageAsync Error: {error.Message}");
                return null;
            }
        }

        public async Task<string?> ResolveFloorBackgroundImageAsync(Floor floor)
        {
            if (floor == null)
            {
                return null;
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                await EnsureFloorBackgroundImagesTableAsync(connection);

                const string query = @"
                    SELECT FileName, ImageData
                    FROM FloorBackgroundImages
                    WHERE FloorId = @FloorId
                    LIMIT 1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@FloorId", floor.Id);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var fileName = reader.GetString("FileName");
                    var imageBytes = (byte[])reader["ImageData"];
                    var localPath = GetBackgroundCachePath(fileName);

                    if (!File.Exists(localPath))
                    {
                        await WriteBackgroundCacheAsync(fileName, imageBytes);
                    }

                    return localPath;
                }
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveFloorBackgroundImageAsync DB fallback: {error.Message}");
            }

            return IsUsableLocalImagePath(floor.BackgroundImage) ? floor.BackgroundImage : null;
        }

        public async Task<bool> RemoveFloorBackgroundImageAsync(int floorId)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync();
                await EnsureFloorSchemaCompatibilityAsync(connection);
                await EnsureFloorBackgroundImagesTableAsync(connection);

                using (var deleteCommand = new MySqlCommand("DELETE FROM FloorBackgroundImages WHERE FloorId = @FloorId", connection))
                {
                    deleteCommand.Parameters.AddWithValue("@FloorId", floorId);
                    await deleteCommand.ExecuteNonQueryAsync();
                }

                await UpdateFloorBackgroundKeyAsync(connection, floorId, string.Empty);

                await PublishFloorChangeSafelyAsync(
                    connection,
                    floorId.ToString(),
                    new { action = "background_image_removed" });

                return true;
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveFloorBackgroundImageAsync Error: {error.Message}");
                return false;
            }
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

        private static async Task EnsureFloorBackgroundImagesTableAsync(MySqlConnection connection)
        {
            const string createSql = @"
                CREATE TABLE IF NOT EXISTS FloorBackgroundImages (
                    FloorId INT PRIMARY KEY,
                    FileName VARCHAR(255) NOT NULL,
                    MimeType VARCHAR(100) NOT NULL,
                    ContentHash CHAR(64) NOT NULL,
                    ImageData MEDIUMBLOB NOT NULL,
                    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    CONSTRAINT fk_floor_background_floor
                        FOREIGN KEY (FloorId) REFERENCES Floors(Id)
                        ON DELETE CASCADE,
                    INDEX idx_floor_background_hash (ContentHash),
                    INDEX idx_floor_background_updated (UpdatedAt)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using var command = new MySqlCommand(createSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task UpdateFloorBackgroundKeyAsync(MySqlConnection connection, int floorId, string backgroundKey)
        {
            await EnsureBackgroundImageColumnExistsAsync(connection);

            const string updateSql = @"
                UPDATE Floors
                SET BackgroundImage = @BackgroundImage,
                    UpdatedDate = NOW()
                WHERE Id = @FloorId AND IsActive = 1";

            using var updateCommand = new MySqlCommand(updateSql, connection);
            updateCommand.Parameters.AddWithValue("@FloorId", floorId);
            updateCommand.Parameters.AddWithValue("@BackgroundImage", backgroundKey ?? string.Empty);
            await updateCommand.ExecuteNonQueryAsync();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string BuildCachedFileName(int floorId, string contentHash, string originalFileName)
        {
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".img";
            }

            return $"floor_{floorId}_{contentHash}{extension.ToLowerInvariant()}";
        }

        private static string GetBackgroundCacheDirectory()
        {
            var directory = Path.Combine(FileSystem.AppDataDirectory, BackgroundCacheFolderName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string GetBackgroundCachePath(string fileName)
        {
            return Path.Combine(GetBackgroundCacheDirectory(), Path.GetFileName(fileName));
        }

        private static async Task<string> WriteBackgroundCacheAsync(string fileName, byte[] imageBytes)
        {
            var localPath = GetBackgroundCachePath(fileName);
            await File.WriteAllBytesAsync(localPath, imageBytes);
            return localPath;
        }

        private static bool IsUsableLocalImagePath(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && !path.StartsWith("db:", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path);
        }

        private async Task EnsureFloorSchemaCompatibilityAsync(MySqlConnection connection)
        {
            await EnsureFloorTablesExistAsync(connection);

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

        private static async Task EnsureFloorTablesExistAsync(MySqlConnection connection)
        {
            const string createFloorsSql = @"
                CREATE TABLE IF NOT EXISTS Floors (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL,
                    Description VARCHAR(255) NULL,
                    BackgroundImage TEXT NULL,
                    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    IsActive TINYINT(1) NOT NULL DEFAULT 1,
                    INDEX idx_name (Name),
                    INDEX idx_active (IsActive)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            const string createRestaurantTablesSql = @"
                CREATE TABLE IF NOT EXISTS RestaurantTables (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    TableNumber VARCHAR(50) NOT NULL,
                    FloorId INT NOT NULL,
                    Capacity INT NOT NULL,
                    Shape VARCHAR(20) NOT NULL DEFAULT 'Square',
                    Status VARCHAR(20) NOT NULL DEFAULT 'Available',
                    TableDesignIcon VARCHAR(100) NULL DEFAULT 'table_1.png',
                    PositionX INT NOT NULL DEFAULT 0,
                    PositionY INT NOT NULL DEFAULT 0,
                    CurrentSessionId INT NULL,
                    LastOccupied DATETIME NULL,
                    TotalSessionsToday INT NOT NULL DEFAULT 0,
                    CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    IsActive TINYINT(1) NOT NULL DEFAULT 1,
                    CONSTRAINT fk_table_floor
                        FOREIGN KEY (FloorId) REFERENCES Floors(Id)
                        ON DELETE CASCADE
                        ON UPDATE CASCADE,
                    UNIQUE KEY unique_table_per_floor (FloorId, TableNumber),
                    INDEX idx_floor_id (FloorId),
                    INDEX idx_status (Status),
                    INDEX idx_active (IsActive),
                    INDEX idx_table_number (TableNumber),
                    INDEX idx_current_session (CurrentSessionId)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using var createFloorsCommand = new MySqlCommand(createFloorsSql, connection);
            await createFloorsCommand.ExecuteNonQueryAsync();

            using var createTablesCommand = new MySqlCommand(createRestaurantTablesSql, connection);
            await createTablesCommand.ExecuteNonQueryAsync();
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
            await PublishFloorChangeSafelyAsync(
                floorId.ToString(),
                new { action = "updated", floorName });
            AppDataRefreshService.RequestRefresh(AppDataChangeKind.TableLayout);

            return (true, $"Floor '{floorName}' updated successfully");
        }

        private static async Task PublishFloorChangeSafelyAsync(
            MySqlConnection connection,
            string floorId,
            object payload)
        {
            try
            {
                await TerminalEventSyncService.PublishAsync(
                    connection,
                    AppDataChangeKind.TableLayout,
                    "floor",
                    floorId,
                    null,
                    payload);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor live-update publish skipped: {ex.Message}");
            }
        }

        private static async Task PublishFloorChangeSafelyAsync(
            string floorId,
            object payload)
        {
            try
            {
                await TerminalEventSyncService.PublishAsync(
                    AppDataChangeKind.TableLayout,
                    "floor",
                    floorId,
                    null,
                    payload);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floor live-update publish skipped: {ex.Message}");
            }
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
