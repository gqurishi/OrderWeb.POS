using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing meal deals — fixed price bundles with pick-N choices.
    /// </summary>
    public class MealDealService
    {
        private readonly string _connectionString;
        private static bool _schemaEnsured;

        public MealDealService()
        {
            _connectionString = POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString();
        }

        private async Task EnsureSchemaAsync(MySqlConnection connection)
        {
            if (_schemaEnsured)
            {
                return;
            }

            const string sql = @"
                CREATE TABLE IF NOT EXISTS MealDeals (
                    Id VARCHAR(36) PRIMARY KEY,
                    Name VARCHAR(150) NOT NULL,
                    Description TEXT NULL,
                    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    Color VARCHAR(20) NOT NULL DEFAULT '#F59E0B',
                    Categories JSON NOT NULL,
                    Active BOOLEAN NOT NULL DEFAULT TRUE,
                    DisplayOrder INT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_mealdeal_active (Active),
                    INDEX idx_mealdeal_order (DisplayOrder)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

            using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            _schemaEnsured = true;
        }

        public async Task<List<MealDeal>> GetAllDealsAsync()
        {
            var deals = new List<MealDeal>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                const string query = @"
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

        public async Task<List<MealDeal>> GetActiveDealsAsync()
        {
            var all = await GetAllDealsAsync();
            return all.Where(d => d.Active && d.Choices.Count > 0).ToList();
        }

        public async Task<MealDeal?> GetDealByIdAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                const string query = @"
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

        public async Task<bool> CreateDealAsync(MealDeal deal)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                if (string.IsNullOrEmpty(deal.Id))
                {
                    deal.Id = $"meal-{Guid.NewGuid()}";
                }

                const string query = @"
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

                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating meal deal: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> UpdateDealAsync(MealDeal deal)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                const string query = @"
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

                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating meal deal: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteDealAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                using var command = new MySqlCommand("DELETE FROM MealDeals WHERE Id = @Id", connection);
                command.Parameters.AddWithValue("@Id", id);
                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting meal deal: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureSchemaAsync(connection);

                const string query = @"
                    UPDATE MealDeals
                    SET Active = NOT Active, UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling meal deal status: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validate customer choice names against deal rules.
        /// </summary>
        public (bool IsValid, List<string> Errors) ValidateChoiceSelections(MealDeal deal, IList<string> selectedChoiceNames)
        {
            var errors = new List<string>();
            var normalizedSelected = selectedChoiceNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();

            if (deal.Choices.Count == 0)
            {
                errors.Add("This meal deal has no choices configured.");
                return (false, errors);
            }

            if (normalizedSelected.Count != deal.PickCount)
            {
                errors.Add($"Please pick exactly {deal.PickCount} item(s). You selected {normalizedSelected.Count}.");
            }

            var validNames = deal.Choices
                .Select(c => c.Name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in normalizedSelected)
            {
                if (!validNames.Contains(name))
                {
                    errors.Add($"'{name}' is not a valid choice for this deal.");
                }
            }

            if (normalizedSelected.Count != normalizedSelected.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                errors.Add("Each choice can only be selected once.");
            }

            return (errors.Count == 0, errors);
        }

        /// <summary>Legacy slot-based validation.</summary>
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

                if (selectionCount < category.MinSelections)
                {
                    errors.Add($"{category.Name}: Minimum {category.MinSelections} selection(s) required (you selected {selectionCount})");
                }

                if (selectionCount > category.MaxSelections)
                {
                    errors.Add($"{category.Name}: Maximum {category.MaxSelections} selection(s) allowed (you selected {selectionCount})");
                }

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

        private static MealDeal ParseMealDeal(MySqlDataReader reader)
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

            var categoriesJson = reader.GetString(reader.GetOrdinal("Categories"));
            MealDeal.ApplyConfigFromJson(deal, categoriesJson);

            return deal;
        }
    }
}
