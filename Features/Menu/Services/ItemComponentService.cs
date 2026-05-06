using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing item components (for mixed VAT items)
    /// </summary>
    public class ItemComponentService
    {
        private readonly string _connectionString;

        public ItemComponentService()
        {
            _connectionString = "Server=localhost;Database=Pos-net;User=root;Password=root;";
        }

        /// <summary>
        /// Get all components for a specific menu item
        /// </summary>
        public async Task<List<ItemComponent>> GetComponentsByItemIdAsync(string menuItemId)
        {
            var components = new List<ItemComponent>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, MenuItemId, ComponentName, ComponentCost, VatRate, 
                           ComponentType, DisplayOrder, CreatedAt, UpdatedAt
                    FROM ItemComponents
                    WHERE MenuItemId = @MenuItemId
                    ORDER BY DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    components.Add(new ItemComponent
                    {
                        Id = reader.GetString(reader.GetOrdinal("Id")),
                        MenuItemId = reader.GetString(reader.GetOrdinal("MenuItemId")),
                        ComponentName = reader.GetString(reader.GetOrdinal("ComponentName")),
                        ComponentCost = reader.GetDecimal(reader.GetOrdinal("ComponentCost")),
                        VatRate = reader.GetDecimal(reader.GetOrdinal("VatRate")),
                        ComponentType = reader.GetString(reader.GetOrdinal("ComponentType")),
                        DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting components: {ex.Message}");
                throw;
            }

            return components;
        }

        /// <summary>
        /// Create a new component
        /// </summary>
        public async Task<bool> CreateComponentAsync(ItemComponent component)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Generate new ID if not provided
                if (string.IsNullOrEmpty(component.Id))
                {
                    component.Id = $"comp-{Guid.NewGuid()}";
                }

                var query = @"
                    INSERT INTO ItemComponents 
                    (Id, MenuItemId, ComponentName, ComponentCost, VatRate, ComponentType, DisplayOrder, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @MenuItemId, @ComponentName, @ComponentCost, @VatRate, @ComponentType, @DisplayOrder, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", component.Id);
                command.Parameters.AddWithValue("@MenuItemId", component.MenuItemId);
                command.Parameters.AddWithValue("@ComponentName", component.ComponentName);
                command.Parameters.AddWithValue("@ComponentCost", component.ComponentCost);
                command.Parameters.AddWithValue("@VatRate", component.VatRate);
                command.Parameters.AddWithValue("@ComponentType", component.ComponentType);
                command.Parameters.AddWithValue("@DisplayOrder", component.DisplayOrder);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating component: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update an existing component
        /// </summary>
        public async Task<bool> UpdateComponentAsync(ItemComponent component)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE ItemComponents 
                    SET ComponentName = @ComponentName,
                        ComponentCost = @ComponentCost,
                        VatRate = @VatRate,
                        ComponentType = @ComponentType,
                        DisplayOrder = @DisplayOrder,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", component.Id);
                command.Parameters.AddWithValue("@ComponentName", component.ComponentName);
                command.Parameters.AddWithValue("@ComponentCost", component.ComponentCost);
                command.Parameters.AddWithValue("@VatRate", component.VatRate);
                command.Parameters.AddWithValue("@ComponentType", component.ComponentType);
                command.Parameters.AddWithValue("@DisplayOrder", component.DisplayOrder);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating component: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a component
        /// </summary>
        public async Task<bool> DeleteComponentAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM ItemComponents WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting component: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete all components for a menu item (when switching from mixed to simple VAT)
        /// </summary>
        public async Task<bool> DeleteAllComponentsForItemAsync(string menuItemId)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM ItemComponents WHERE MenuItemId = @MenuItemId";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);

                await command.ExecuteNonQueryAsync();
                return true; // Return true even if no rows affected
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting all components: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Replace all components for an item (bulk operation)
        /// </summary>
        public async Task<bool> ReplaceComponentsAsync(string menuItemId, List<ItemComponent> newComponents)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Delete existing components
                    var deleteQuery = "DELETE FROM ItemComponents WHERE MenuItemId = @MenuItemId";
                    using (var deleteCommand = new MySqlCommand(deleteQuery, connection, transaction))
                    {
                        deleteCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        await deleteCommand.ExecuteNonQueryAsync();
                    }

                    // Insert new components
                    foreach (var component in newComponents)
                    {
                        if (string.IsNullOrEmpty(component.Id))
                        {
                            component.Id = $"comp-{Guid.NewGuid()}";
                        }

                        var insertQuery = @"
                            INSERT INTO ItemComponents 
                            (Id, MenuItemId, ComponentName, ComponentCost, VatRate, ComponentType, DisplayOrder, CreatedAt, UpdatedAt)
                            VALUES 
                            (@Id, @MenuItemId, @ComponentName, @ComponentCost, @VatRate, @ComponentType, @DisplayOrder, @CreatedAt, @UpdatedAt)";

                        using var insertCommand = new MySqlCommand(insertQuery, connection, transaction);
                        insertCommand.Parameters.AddWithValue("@Id", component.Id);
                        insertCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        insertCommand.Parameters.AddWithValue("@ComponentName", component.ComponentName);
                        insertCommand.Parameters.AddWithValue("@ComponentCost", component.ComponentCost);
                        insertCommand.Parameters.AddWithValue("@VatRate", component.VatRate);
                        insertCommand.Parameters.AddWithValue("@ComponentType", component.ComponentType);
                        insertCommand.Parameters.AddWithValue("@DisplayOrder", component.DisplayOrder);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                        insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                        await insertCommand.ExecuteNonQueryAsync();
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
                Console.WriteLine($"Error replacing components: {ex.Message}");
                throw;
            }
        }
    }
}
