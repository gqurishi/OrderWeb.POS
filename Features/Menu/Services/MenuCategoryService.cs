using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing menu categories with hierarchical structure
    /// </summary>
    public class MenuCategoryService
    {
        private readonly string _connectionString;

        public MenuCategoryService()
        {
            _connectionString = "Server=localhost;Database=Pos-net;User=root;Password=root;";
        }

        /// <summary>
        /// Get all categories (including sub-categories)
        /// </summary>
        public async Task<List<MenuCategory>> GetAllCategoriesAsync()
        {
            var categories = new List<MenuCategory>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, ParentId, DisplayOrder, 
                           Active, Color, Icon, CreatedAt, UpdatedAt
                    FROM FoodMenuCategories
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    categories.Add(new MenuCategory
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                        ParentId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetString(reader.GetOrdinal("ParentId")),
                        DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                        Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                        Color = reader.GetString(reader.GetOrdinal("Color")),
                        Icon = reader.GetString(reader.GetOrdinal("Icon")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting categories: {ex.Message}");
                throw;
            }

            return categories;
        }

        /// <summary>
        /// Get top-level categories only (no parent)
        /// </summary>
        public async Task<List<MenuCategory>> GetTopLevelCategoriesAsync()
        {
            var categories = new List<MenuCategory>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, ParentId, DisplayOrder, 
                           Active, Color, Icon, CreatedAt, UpdatedAt
                    FROM FoodMenuCategories
                    WHERE ParentId IS NULL
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    categories.Add(new MenuCategory
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                        ParentId = null,
                        DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                        Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                        Color = reader.GetString(reader.GetOrdinal("Color")),
                        Icon = reader.GetString(reader.GetOrdinal("Icon")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting top-level categories: {ex.Message}");
                throw;
            }

            return categories;
        }

        /// <summary>
        /// Get sub-categories of a specific parent category
        /// </summary>
        public async Task<List<MenuCategory>> GetSubCategoriesAsync(string parentId)
        {
            var categories = new List<MenuCategory>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, ParentId, DisplayOrder, 
                           Active, Color, Icon, CreatedAt, UpdatedAt
                    FROM FoodMenuCategories
                    WHERE ParentId = @ParentId
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@ParentId", parentId);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    categories.Add(new MenuCategory
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                        ParentId = reader.GetString(reader.GetOrdinal("ParentId")),
                        DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                        Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                        Color = reader.GetString(reader.GetOrdinal("Color")),
                        Icon = reader.GetString(reader.GetOrdinal("Icon")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting sub-categories: {ex.Message}");
                throw;
            }

            return categories;
        }

        /// <summary>
        /// Get a specific category by ID
        /// </summary>
        public async Task<MenuCategory?> GetCategoryByIdAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, ParentId, DisplayOrder, 
                           Active, Color, Icon, CreatedAt, UpdatedAt
                    FROM FoodMenuCategories
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new MenuCategory
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                        ParentId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetString(reader.GetOrdinal("ParentId")),
                        DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                        Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                        Color = reader.GetString(reader.GetOrdinal("Color")),
                        Icon = reader.GetString(reader.GetOrdinal("Icon")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting category by ID: {ex.Message}");
                throw;
            }

            return null;
        }

        /// <summary>
        /// Create a new category
        /// </summary>
        public async Task<bool> CreateCategoryAsync(MenuCategory category)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CreateCategoryAsync called for category: {category.Name}");
                System.Diagnostics.Debug.WriteLine($"Connection string: {_connectionString}");
                
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                
                System.Diagnostics.Debug.WriteLine("Database connection opened successfully");

                // Generate new ID if not provided
                if (string.IsNullOrEmpty(category.Id))
                {
                    category.Id = $"cat-{Guid.NewGuid()}";
                }

                System.Diagnostics.Debug.WriteLine($"Category ID: {category.Id}");

                var query = @"
                    INSERT INTO FoodMenuCategories 
                    (Id, Name, Description, ParentId, DisplayOrder, Active, Color, Icon, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @Name, @Description, @ParentId, @DisplayOrder, @Active, @Color, @Icon, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", category.Id);
                command.Parameters.AddWithValue("@Name", category.Name);
                command.Parameters.AddWithValue("@Description", (object?)category.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("@ParentId", (object?)category.ParentId ?? DBNull.Value);
                command.Parameters.AddWithValue("@DisplayOrder", category.DisplayOrder);
                command.Parameters.AddWithValue("@Active", category.Active);
                command.Parameters.AddWithValue("@Color", category.Color);
                command.Parameters.AddWithValue("@Icon", category.Icon);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                System.Diagnostics.Debug.WriteLine("Executing INSERT query...");
                var result = await command.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"INSERT result: {result} rows affected");
                
                return result > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating category: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.WriteLine($"Error creating category: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update an existing category
        /// </summary>
        public async Task<bool> UpdateCategoryAsync(MenuCategory category)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE FoodMenuCategories 
                    SET Name = @Name,
                        Description = @Description,
                        ParentId = @ParentId,
                        DisplayOrder = @DisplayOrder,
                        Active = @Active,
                        Color = @Color,
                        Icon = @Icon,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", category.Id);
                command.Parameters.AddWithValue("@Name", category.Name);
                command.Parameters.AddWithValue("@Description", (object?)category.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("@ParentId", (object?)category.ParentId ?? DBNull.Value);
                command.Parameters.AddWithValue("@DisplayOrder", category.DisplayOrder);
                command.Parameters.AddWithValue("@Active", category.Active);
                command.Parameters.AddWithValue("@Color", category.Color);
                command.Parameters.AddWithValue("@Icon", category.Icon);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating category: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a category (will cascade delete sub-categories and unlink items)
        /// </summary>
        public async Task<bool> DeleteCategoryAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM FoodMenuCategories WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting category: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update display order for multiple categories (for drag-drop reordering)
        /// </summary>
        public async Task<bool> UpdateDisplayOrdersAsync(Dictionary<string, int> categoryOrders)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    foreach (var kvp in categoryOrders)
                    {
                        var query = "UPDATE FoodMenuCategories SET DisplayOrder = @DisplayOrder, UpdatedAt = @UpdatedAt WHERE Id = @Id";
                        using var command = new MySqlCommand(query, connection, transaction);
                        command.Parameters.AddWithValue("@Id", kvp.Key);
                        command.Parameters.AddWithValue("@DisplayOrder", kvp.Value);
                        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        await command.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating display orders: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Toggle active status for a category
        /// </summary>
        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE FoodMenuCategories 
                    SET Active = NOT Active, UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling active status: {ex.Message}");
                throw;
            }
        }
    }
}
