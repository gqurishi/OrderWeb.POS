using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing meal deals with selection rules
    /// </summary>
    public class MealDealService
    {
        private readonly string _connectionString;

        public MealDealService()
        {
            _connectionString = "Server=localhost;Database=Pos-net;User=root;Password=root;";
        }

        /// <summary>
        /// Get all meal deals
        /// </summary>
        public async Task<List<MealDeal>> GetAllDealsAsync()
        {
            var deals = new List<MealDeal>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Price, Color, Categories, 
                           Active, DisplayOrder, CreatedAt, UpdatedAt
                    FROM MealDeals
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    deals.Add(ParseMealDeal(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting meal deals: {ex.Message}");
                throw;
            }

            return deals;
        }

        /// <summary>
        /// Get active meal deals only
        /// </summary>
        public async Task<List<MealDeal>> GetActiveDealsAsync()
        {
            var deals = new List<MealDeal>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Price, Color, Categories, 
                           Active, DisplayOrder, CreatedAt, UpdatedAt
                    FROM MealDeals
                    WHERE Active = TRUE
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    deals.Add(ParseMealDeal(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active meal deals: {ex.Message}");
                throw;
            }

            return deals;
        }

        /// <summary>
        /// Get a specific meal deal by ID
        /// </summary>
        public async Task<MealDeal?> GetDealByIdAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Price, Color, Categories, 
                           Active, DisplayOrder, CreatedAt, UpdatedAt
                    FROM MealDeals
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return ParseMealDeal(reader);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting meal deal by ID: {ex.Message}");
                throw;
            }

            return null;
        }

        /// <summary>
        /// Create a new meal deal
        /// </summary>
        public async Task<bool> CreateDealAsync(MealDeal deal)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Generate new ID if not provided
                if (string.IsNullOrEmpty(deal.Id))
                {
                    deal.Id = $"meal-{Guid.NewGuid()}";
                }

                var query = @"
                    INSERT INTO MealDeals 
                    (Id, Name, Description, Price, Color, Categories, Active, DisplayOrder, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @Name, @Description, @Price, @Color, @Categories, @Active, @DisplayOrder, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", deal.Id);
                command.Parameters.AddWithValue("@Name", deal.Name);
                command.Parameters.AddWithValue("@Description", (object?)deal.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("@Price", deal.Price);
                command.Parameters.AddWithValue("@Color", deal.Color);
                command.Parameters.AddWithValue("@Categories", deal.CategoriesJson);
                command.Parameters.AddWithValue("@Active", deal.Active);
                command.Parameters.AddWithValue("@DisplayOrder", deal.DisplayOrder);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating meal deal: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update an existing meal deal
        /// </summary>
        public async Task<bool> UpdateDealAsync(MealDeal deal)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE MealDeals 
                    SET Name = @Name,
                        Description = @Description,
                        Price = @Price,
                        Color = @Color,
                        Categories = @Categories,
                        Active = @Active,
                        DisplayOrder = @DisplayOrder,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", deal.Id);
                command.Parameters.AddWithValue("@Name", deal.Name);
                command.Parameters.AddWithValue("@Description", (object?)deal.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("@Price", deal.Price);
                command.Parameters.AddWithValue("@Color", deal.Color);
                command.Parameters.AddWithValue("@Categories", deal.CategoriesJson);
                command.Parameters.AddWithValue("@Active", deal.Active);
                command.Parameters.AddWithValue("@DisplayOrder", deal.DisplayOrder);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating meal deal: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a meal deal
        /// </summary>
        public async Task<bool> DeleteDealAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = "DELETE FROM MealDeals WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting meal deal: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Toggle active status
        /// </summary>
        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE MealDeals 
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

        /// <summary>
        /// Validate customer selections against meal deal rules
        /// </summary>
        public (bool IsValid, List<string> Errors) ValidateSelections(MealDeal deal, Dictionary<string, List<string>> selections)
        {
            var errors = new List<string>();

            foreach (var category in deal.Categories)
            {
                if (!selections.ContainsKey(category.Id))
                {
                    if (category.IsRequired && category.MinSelections > 0)
                    {
                        errors.Add($"{category.Name}: Selection is required");
                    }
                    continue;
                }

                var selectedItems = selections[category.Id];
                var selectionCount = selectedItems.Count;

                // Check minimum selections
                if (selectionCount < category.MinSelections)
                {
                    errors.Add($"{category.Name}: Minimum {category.MinSelections} selection(s) required (you selected {selectionCount})");
                }

                // Check maximum selections
                if (selectionCount > category.MaxSelections)
                {
                    errors.Add($"{category.Name}: Maximum {category.MaxSelections} selection(s) allowed (you selected {selectionCount})");
                }

                // Check that all selected items are valid for this category
                foreach (var itemId in selectedItems)
                {
                    if (!category.MenuItemIds.Contains(itemId))
                    {
                        errors.Add($"{category.Name}: Invalid item selection");
                        break;
                    }
                }
            }

            return (errors.Count == 0, errors);
        }

        // Helper method

        private MealDeal ParseMealDeal(MySqlDataReader reader)
        {
            var deal = new MealDeal
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };

            // Parse JSON categories
            var categoriesJson = reader.GetString(reader.GetOrdinal("Categories"));
            deal.Categories = MealDeal.ParseCategories(categoriesJson);

            return deal;
        }
    }
}
