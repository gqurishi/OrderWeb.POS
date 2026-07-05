using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Service for managing menu items with VAT, addons, and components
    /// </summary>
    public class MenuItemService
    {
        private readonly string _connectionString;
        private static bool _foodMenuItemSchemaReady;
        private static bool _quickNotesSchemaReady;

        public MenuItemService()
        {
            _connectionString = POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString();
        }

        /// <summary>
        /// Get all menu items
        /// </summary>
        public async Task<List<FoodMenuItem>> GetAllItemsAsync()
        {
            var items = new List<FoodMenuItem>();

            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                    SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                           Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                           vat_config_type, vat_category, calculated_vat_rate, ItemType,
                           label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    ORDER BY IsFeatured DESC, DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    items.Add(ParseFoodMenuItem(reader));
                }

                await reader.CloseAsync();
                await LoadVariantsForItemsAsync(connection, items);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting menu items: {ex.Message}");
                throw;
            }

            return items;
        }

        /// <summary>
        /// Get menu items by category
        /// </summary>
        public async Task<List<FoodMenuItem>> GetItemsByCategoryAsync(string categoryId)
        {
            var items = new List<FoodMenuItem>();

            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate, ItemType,
                          label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    WHERE CategoryId = @CategoryId
                    ORDER BY IsFeatured DESC, DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@CategoryId", categoryId);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    items.Add(ParseFoodMenuItem(reader));
                }

                await reader.CloseAsync();
                await LoadVariantsForItemsAsync(connection, items);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting items by category: {ex.Message}");
                throw;
            }

            return items;
        }

        /// <summary>
        /// Get a specific menu item by ID
        /// </summary>
        public async Task<FoodMenuItem?> GetItemByIdAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate, ItemType,
                          label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var item = ParseFoodMenuItem(reader);
                    await reader.CloseAsync();
                    await LoadVariantsForItemsAsync(connection, new List<FoodMenuItem> { item });
                    return item;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting item by ID: {ex.Message}");
                throw;
            }

            return null;
        }

        /// <summary>
        /// Create a new menu item
        /// </summary>
        public async Task<bool> CreateItemAsync(FoodMenuItem item)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                // Generate new ID if not provided
                if (string.IsNullOrEmpty(item.Id))
                {
                    item.Id = Guid.NewGuid().ToString();
                }

                var query = @"
                    INSERT INTO FoodMenuItems 
                    (Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder, IsFeatured,
                     PreparationTime, VatRate, VatType, IsVatExempt, VatNotes, Addons, Tags, print_in_red,
                     vat_config_type, vat_category, calculated_vat_rate, ItemType, label_text, print_component_labels,
                     component_labels_json, print_group_id, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @CategoryId, @Name, @Description, @Price, @PriceDineIn, @PriceTakeaway, @Color, @DisplayOrder, @IsFeatured,
                     @PreparationTime, @VatRate, @VatType, @IsVatExempt, @VatNotes, @Addons, @Tags, @PrintInRed,
                     @VatConfigType, @VatCategory, @CalculatedVatRate, @ItemType, @LabelText, @PrintComponentLabels,
                     @ComponentLabelsJson, @PrintGroupId, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                AddFoodMenuItemParameters(command, item);

                var result = await command.ExecuteNonQueryAsync();
                if (result > 0)
                {
                    await SaveVariantsForItemAsync(connection, item.Id, item.Variants);
                }

                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating menu item: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update an existing menu item
        /// </summary>
        public async Task<bool> UpdateItemAsync(FoodMenuItem item)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                    UPDATE FoodMenuItems 
                    SET CategoryId = @CategoryId,
                        Name = @Name,
                        Description = @Description,
                        Price = @Price,
                        price_dine_in = @PriceDineIn,
                        price_takeaway = @PriceTakeaway,
                        Color = @Color,
                        DisplayOrder = @DisplayOrder,
                        IsFeatured = @IsFeatured,
                        PreparationTime = @PreparationTime,
                        VatRate = @VatRate,
                        VatType = @VatType,
                        IsVatExempt = @IsVatExempt,
                        VatNotes = @VatNotes,
                        Addons = @Addons,
                        Tags = @Tags,
                        print_in_red = @PrintInRed,
                        vat_config_type = @VatConfigType,
                        vat_category = @VatCategory,
                        calculated_vat_rate = @CalculatedVatRate,
                        ItemType = @ItemType,
                        label_text = @LabelText,
                        print_component_labels = @PrintComponentLabels,
                        component_labels_json = @ComponentLabelsJson,
                        print_group_id = @PrintGroupId,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                AddFoodMenuItemParameters(command, item, isUpdate: true);

                var result = await command.ExecuteNonQueryAsync();
                if (result > 0)
                {
                    await SaveVariantsForItemAsync(connection, item.Id, item.Variants);
                }

                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating menu item: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete a menu item
        /// </summary>
        public async Task<bool> DeleteItemAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = "DELETE FROM FoodMenuItems WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting menu item: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Toggle featured status
        /// </summary>
        public async Task<bool> ToggleFeaturedAsync(string id)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                    UPDATE FoodMenuItems 
                    SET IsFeatured = NOT IsFeatured, UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                var result = await command.ExecuteNonQueryAsync();
                return result > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling featured status: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Search menu items by name or tags
        /// </summary>
        public async Task<List<FoodMenuItem>> SearchItemsAsync(string searchTerm)
        {
            var items = new List<FoodMenuItem>();

            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate, ItemType,
                          label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    WHERE Name LIKE @SearchTerm 
                       OR Description LIKE @SearchTerm
                       OR Tags LIKE @SearchTerm
                    ORDER BY IsFeatured DESC, DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    items.Add(ParseFoodMenuItem(reader));
                }

                await reader.CloseAsync();
                await LoadVariantsForItemsAsync(connection, items);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching items: {ex.Message}");
                throw;
            }

            return items;
        }

        /// <summary>
        /// Get featured items only
        /// </summary>
        public async Task<List<FoodMenuItem>> GetFeaturedItemsAsync()
        {
            var items = new List<FoodMenuItem>();

            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureFoodMenuItemSchemaAsync(connection);

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate, ItemType,
                          label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    WHERE IsFeatured = TRUE
                    ORDER BY DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    items.Add(ParseFoodMenuItem(reader));
                }

                await reader.CloseAsync();
                await LoadVariantsForItemsAsync(connection, items);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting featured items: {ex.Message}");
                throw;
            }

            return items;
        }

        // Helper methods

        private static async Task EnsureFoodMenuItemSchemaAsync(MySqlConnection connection)
        {
            if (_foodMenuItemSchemaReady)
            {
                return;
            }

            const string alterSql = @"
                ALTER TABLE FoodMenuItems
                ADD COLUMN IF NOT EXISTS price_dine_in DECIMAL(10,2) DEFAULT NULL,
                ADD COLUMN IF NOT EXISTS price_takeaway DECIMAL(10,2) DEFAULT NULL,
                ADD COLUMN IF NOT EXISTS vat_config_type VARCHAR(20) DEFAULT 'standard',
                ADD COLUMN IF NOT EXISTS vat_category VARCHAR(20) DEFAULT 'HotFood',
                ADD COLUMN IF NOT EXISTS calculated_vat_rate DECIMAL(5,2) DEFAULT 20.00,
                ADD COLUMN IF NOT EXISTS ItemType VARCHAR(20) NOT NULL DEFAULT 'Food',
                ADD COLUMN IF NOT EXISTS print_in_red BOOLEAN NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS label_text VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS print_component_labels TINYINT(1) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS component_labels_json TEXT NULL,
                ADD COLUMN IF NOT EXISTS print_group_id VARCHAR(36) NULL";

            using (var alterCommand = new MySqlCommand(alterSql, connection))
            {
                await alterCommand.ExecuteNonQueryAsync();
            }

            const string indexSql = @"
                ALTER TABLE FoodMenuItems
                ADD INDEX IF NOT EXISTS idx_foodmenu_item_type (ItemType),
                ADD INDEX IF NOT EXISTS idx_foodmenu_print_group (print_group_id)";

            try
            {
                using var indexCommand = new MySqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MenuItemService] Index ensure skipped: {ex.Message}");
            }

            const string variantsTableSql = @"
                CREATE TABLE IF NOT EXISTS MenuItemVariants (
                    Id VARCHAR(36) PRIMARY KEY,
                    MenuItemId VARCHAR(36) NOT NULL,
                    Name VARCHAR(100) NOT NULL,
                    Description TEXT NULL,
                    Price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                    DisplayOrder INT NOT NULL DEFAULT 0,
                    Active BOOLEAN NOT NULL DEFAULT TRUE,
                    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_menu_item_variants_item (MenuItemId),
                    INDEX idx_menu_item_variants_active_order (MenuItemId, Active, DisplayOrder),
                    CONSTRAINT fk_menu_item_variants_item FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using (var variantsCommand = new MySqlCommand(variantsTableSql, connection))
            {
                await variantsCommand.ExecuteNonQueryAsync();
            }

            _foodMenuItemSchemaReady = true;
        }

        private static async Task EnsureQuickNotesSchemaAsync(MySqlConnection connection)
        {
            if (_quickNotesSchemaReady)
            {
                return;
            }

            const string createSql = @"
                CREATE TABLE IF NOT EXISTS MenuItemQuickNotes (
                    Id VARCHAR(36) PRIMARY KEY,
                    MenuItemId VARCHAR(36) NOT NULL,
                    NoteText VARCHAR(255) NULL,
                    DisplayOrder INT NOT NULL DEFAULT 0,
                    Active BOOLEAN NOT NULL DEFAULT TRUE,
                    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_menuitem_quick_note_item (MenuItemId),
                    INDEX idx_menuitem_quick_note_active (Active),
                    INDEX idx_menuitem_quick_note_order (DisplayOrder),
                    CONSTRAINT fk_menuitem_quick_note_item
                        FOREIGN KEY (MenuItemId) REFERENCES FoodMenuItems(Id)
                        ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using (var createCommand = new MySqlCommand(createSql, connection))
            {
                await createCommand.ExecuteNonQueryAsync();
            }

            const string alterSql = @"
                ALTER TABLE MenuItemQuickNotes
                ADD COLUMN IF NOT EXISTS NoteText VARCHAR(255) NULL AFTER MenuItemId,
                ADD COLUMN IF NOT EXISTS DisplayOrder INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS Active BOOLEAN NOT NULL DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ADD COLUMN IF NOT EXISTS UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP";

            using (var alterCommand = new MySqlCommand(alterSql, connection))
            {
                await alterCommand.ExecuteNonQueryAsync();
            }

            if (await ColumnExistsAsync(connection, "MenuItemQuickNotes", "NoteId"))
            {
                try
                {
                    using var noteIdCommand = new MySqlCommand(
                        "ALTER TABLE MenuItemQuickNotes MODIFY COLUMN NoteId VARCHAR(36) NULL",
                        connection);
                    await noteIdCommand.ExecuteNonQueryAsync();
                }
                catch (MySqlException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MenuItemService] Legacy quick-note NoteId relax skipped: {ex.Message}");
                }
            }

            _quickNotesSchemaReady = true;
        }

        private static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string tableName, string columnName)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = @TableName
                  AND column_name = @ColumnName";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }

        public async Task<List<MenuItemVariant>> GetVariantsByMenuItemIdAsync(string menuItemId, bool activeOnly = false)
        {
            if (string.IsNullOrWhiteSpace(menuItemId))
            {
                return new List<MenuItemVariant>();
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureFoodMenuItemSchemaAsync(connection);

            var query = @"
                SELECT Id, MenuItemId, Name, Description, Price, DisplayOrder, Active, CreatedAt, UpdatedAt
                FROM MenuItemVariants
                WHERE MenuItemId = @MenuItemId";
            if (activeOnly)
            {
                query += " AND Active = TRUE";
            }
            query += " ORDER BY DisplayOrder ASC, Name ASC";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@MenuItemId", menuItemId);

            var variants = new List<MenuItemVariant>();
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                variants.Add(ParseMenuItemVariant(reader));
            }

            return variants;
        }

        public async Task<MenuItemVariant?> InferVariantByPriceAsync(string? menuItemId, decimal? price)
        {
            if (string.IsNullOrWhiteSpace(menuItemId) || !price.HasValue)
            {
                return null;
            }

            var roundedPrice = Math.Round(price.Value, 2);
            var variants = await GetVariantsByMenuItemIdAsync(menuItemId, activeOnly: true);
            var matches = variants
                .Where(variant => Math.Round(variant.Price, 2) == roundedPrice)
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private async Task LoadVariantsForItemsAsync(MySqlConnection connection, List<FoodMenuItem> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            var ids = items
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            var parameterNames = ids.Select((_, index) => $"@id{index}").ToList();
            var query = $@"
                SELECT Id, MenuItemId, Name, Description, Price, DisplayOrder, Active, CreatedAt, UpdatedAt
                FROM MenuItemVariants
                WHERE MenuItemId IN ({string.Join(",", parameterNames)})
                ORDER BY MenuItemId, DisplayOrder ASC, Name ASC";

            using var command = new MySqlCommand(query, connection);
            for (var i = 0; i < ids.Count; i++)
            {
                command.Parameters.AddWithValue(parameterNames[i], ids[i]);
            }

            var byItemId = items.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var variant = ParseMenuItemVariant(reader);
                if (byItemId.TryGetValue(variant.MenuItemId, out var item))
                {
                    item.Variants.Add(variant);
                }
            }
        }

        private async Task SaveVariantsForItemAsync(MySqlConnection connection, string menuItemId, List<MenuItemVariant>? variants)
        {
            using (var deleteCommand = new MySqlCommand("DELETE FROM MenuItemVariants WHERE MenuItemId = @MenuItemId", connection))
            {
                deleteCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                await deleteCommand.ExecuteNonQueryAsync();
            }

            if (variants == null || variants.Count == 0)
            {
                return;
            }

            const string insertSql = @"
                INSERT INTO MenuItemVariants
                    (Id, MenuItemId, Name, Description, Price, DisplayOrder, Active, CreatedAt, UpdatedAt)
                VALUES
                    (@Id, @MenuItemId, @Name, @Description, @Price, @DisplayOrder, @Active, @CreatedAt, @UpdatedAt)";

            var displayOrder = 0;
            foreach (var variant in variants.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
            {
                using var command = new MySqlCommand(insertSql, connection);
                command.Parameters.AddWithValue("@Id", string.IsNullOrWhiteSpace(variant.Id) ? Guid.NewGuid().ToString() : variant.Id);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                command.Parameters.AddWithValue("@Name", variant.Name.Trim());
                command.Parameters.AddWithValue("@Description", string.IsNullOrWhiteSpace(variant.Description) ? DBNull.Value : variant.Description.Trim());
                command.Parameters.AddWithValue("@Price", Math.Round(Math.Max(0m, variant.Price), 2));
                command.Parameters.AddWithValue("@DisplayOrder", displayOrder++);
                command.Parameters.AddWithValue("@Active", variant.Active);
                command.Parameters.AddWithValue("@CreatedAt", variant.CreatedAt == default ? DateTime.Now : variant.CreatedAt);
                command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static MenuItemVariant ParseMenuItemVariant(MySqlDataReader reader)
        {
            return new MenuItemVariant
            {
                Id = reader.IsDBNull(reader.GetOrdinal("Id")) ? Guid.NewGuid().ToString() : reader.GetString(reader.GetOrdinal("Id")),
                MenuItemId = reader.IsDBNull(reader.GetOrdinal("MenuItemId")) ? string.Empty : reader.GetString(reader.GetOrdinal("MenuItemId")),
                Name = reader.IsDBNull(reader.GetOrdinal("Name")) ? string.Empty : reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Price = reader.IsDBNull(reader.GetOrdinal("Price")) ? 0m : reader.GetDecimal(reader.GetOrdinal("Price")),
                DisplayOrder = reader.IsDBNull(reader.GetOrdinal("DisplayOrder")) ? 0 : reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                Active = reader.IsDBNull(reader.GetOrdinal("Active")) || reader.GetBoolean(reader.GetOrdinal("Active")),
                CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.Now : reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? DateTime.Now : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private FoodMenuItem ParseFoodMenuItem(MySqlDataReader reader)
        {
            decimal legacyPrice = reader.IsDBNull(reader.GetOrdinal("Price")) ? 0 : reader.GetDecimal(reader.GetOrdinal("Price"));

            var item = new FoodMenuItem
            {
                Id = reader.IsDBNull(reader.GetOrdinal("Id")) ? Guid.NewGuid().ToString() : reader.GetString(reader.GetOrdinal("Id")),
                CategoryId = reader.IsDBNull(reader.GetOrdinal("CategoryId")) ? string.Empty : reader.GetString(reader.GetOrdinal("CategoryId")),
                Name = reader.IsDBNull(reader.GetOrdinal("Name")) ? "Unnamed Item" : reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Price = legacyPrice,
                PriceDineIn = legacyPrice,
                PriceTakeaway = legacyPrice,
                Color = reader.IsDBNull(reader.GetOrdinal("Color")) ? "#3B82F6" : reader.GetString(reader.GetOrdinal("Color")),
                DisplayOrder = reader.IsDBNull(reader.GetOrdinal("DisplayOrder")) ? 0 : reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                IsFeatured = reader.IsDBNull(reader.GetOrdinal("IsFeatured")) ? false : reader.GetBoolean(reader.GetOrdinal("IsFeatured")),
                PreparationTime = reader.IsDBNull(reader.GetOrdinal("PreparationTime")) ? null : reader.GetInt32(reader.GetOrdinal("PreparationTime")),
                VatRate = reader.IsDBNull(reader.GetOrdinal("VatRate")) ? 0 : reader.GetDecimal(reader.GetOrdinal("VatRate")),
                VatType = reader.IsDBNull(reader.GetOrdinal("VatType")) ? "Standard" : reader.GetString(reader.GetOrdinal("VatType")),
                IsVatExempt = reader.IsDBNull(reader.GetOrdinal("IsVatExempt")) ? false : reader.GetBoolean(reader.GetOrdinal("IsVatExempt")),
                VatNotes = reader.IsDBNull(reader.GetOrdinal("VatNotes")) ? null : reader.GetString(reader.GetOrdinal("VatNotes")),
                PrintInRed = reader.IsDBNull(reader.GetOrdinal("print_in_red")) ? false : reader.GetBoolean(reader.GetOrdinal("print_in_red")),
                CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.Now : reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? DateTime.Now : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };

            // Stage 10 (Phase 1 fallback): load dual prices with legacy Price fallback for rollout safety.
            // Phase 2: after migration is fully verified, remove this fallback path.
            try
            {
                var dineInOrdinal = reader.GetOrdinal("price_dine_in");
                if (!reader.IsDBNull(dineInOrdinal))
                {
                    item.PriceDineIn = reader.GetDecimal(dineInOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }

            try
            {
                var takeawayOrdinal = reader.GetOrdinal("price_takeaway");
                if (!reader.IsDBNull(takeawayOrdinal))
                {
                    item.PriceTakeaway = reader.GetDecimal(takeawayOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }

            item.PriceDineIn ??= item.Price;
            item.PriceTakeaway ??= item.Price;

            // Parse JSON fields
            var addonsJson = reader.IsDBNull(reader.GetOrdinal("Addons")) ? null : reader.GetString(reader.GetOrdinal("Addons"));
            item.Addons = FoodMenuItem.ParseAddons(addonsJson);

            var tagsJson = reader.IsDBNull(reader.GetOrdinal("Tags")) ? null : reader.GetString(reader.GetOrdinal("Tags"));
            item.Tags = FoodMenuItem.ParseTags(tagsJson);
            
            // Load new VAT fields if they exist
            try
            {
                var vatConfigTypeOrdinal = reader.GetOrdinal("vat_config_type");
                if (!reader.IsDBNull(vatConfigTypeOrdinal))
                {
                    item.VatConfigType = reader.GetString(vatConfigTypeOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            try
            {
                var vatCategoryOrdinal = reader.GetOrdinal("vat_category");
                if (!reader.IsDBNull(vatCategoryOrdinal))
                {
                    item.VatCategory = reader.GetString(vatCategoryOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            try
            {
                var calcVatRateOrdinal = reader.GetOrdinal("calculated_vat_rate");
                if (!reader.IsDBNull(calcVatRateOrdinal))
                {
                    item.CalculatedVatRate = reader.GetDecimal(calcVatRateOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }

            try
            {
                var itemTypeOrdinal = reader.GetOrdinal("ItemType");
                if (!reader.IsDBNull(itemTypeOrdinal))
                {
                    item.ItemType = NormalizeItemType(reader.GetString(itemTypeOrdinal));
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            // Load label print settings if they exist
            try
            {
                var labelTextOrdinal = reader.GetOrdinal("label_text");
                if (!reader.IsDBNull(labelTextOrdinal))
                {
                    item.LabelText = reader.GetString(labelTextOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            try
            {
                var printComponentLabelsOrdinal = reader.GetOrdinal("print_component_labels");
                if (!reader.IsDBNull(printComponentLabelsOrdinal))
                {
                    item.PrintComponentLabels = reader.GetBoolean(printComponentLabelsOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            // Load component labels JSON
            try
            {
                var componentLabelsJsonOrdinal = reader.GetOrdinal("component_labels_json");
                if (!reader.IsDBNull(componentLabelsJsonOrdinal))
                {
                    item.ComponentLabelsJson = reader.GetString(componentLabelsJsonOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }
            
            // Load print group
            try
            {
                var printGroupIdOrdinal = reader.GetOrdinal("print_group_id");
                if (!reader.IsDBNull(printGroupIdOrdinal))
                {
                    item.PrintGroupId = reader.GetString(printGroupIdOrdinal);
                }
            }
            catch { /* Column doesn't exist yet */ }

            return item;
        }

        private void AddFoodMenuItemParameters(MySqlCommand command, FoodMenuItem item, bool isUpdate = false)
        {
            if (!isUpdate)
            {
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            }
            
            command.Parameters.AddWithValue("@Id", item.Id);
            command.Parameters.AddWithValue("@CategoryId", item.CategoryId);
            command.Parameters.AddWithValue("@Name", item.Name);
            command.Parameters.AddWithValue("@Description", (object?)item.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@Price", item.Price);
            command.Parameters.AddWithValue("@PriceDineIn", (object?)item.PriceDineIn ?? item.Price);
            command.Parameters.AddWithValue("@PriceTakeaway", (object?)item.PriceTakeaway ?? item.Price);
            command.Parameters.AddWithValue("@Color", item.Color);
            command.Parameters.AddWithValue("@DisplayOrder", item.DisplayOrder);
            command.Parameters.AddWithValue("@IsFeatured", item.IsFeatured);
            command.Parameters.AddWithValue("@PreparationTime", (object?)item.PreparationTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@VatRate", item.VatRate);
            command.Parameters.AddWithValue("@VatType", item.VatType);
            command.Parameters.AddWithValue("@IsVatExempt", item.IsVatExempt);
            command.Parameters.AddWithValue("@VatNotes", (object?)item.VatNotes ?? DBNull.Value);
            command.Parameters.AddWithValue("@Addons", (object?)item.AddonsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@Tags", (object?)item.TagsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@PrintInRed", item.PrintInRed);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            
            // Add new VAT fields
            command.Parameters.AddWithValue("@VatConfigType", item.VatConfigType);
            command.Parameters.AddWithValue("@VatCategory", item.VatCategory);
            command.Parameters.AddWithValue("@CalculatedVatRate", item.CalculatedVatRate);
            command.Parameters.AddWithValue("@ItemType", NormalizeItemType(item.ItemType));
            
            // Add label print settings
            command.Parameters.AddWithValue("@LabelText", (object?)item.LabelText ?? DBNull.Value);
            command.Parameters.AddWithValue("@PrintComponentLabels", item.PrintComponentLabels);
            command.Parameters.AddWithValue("@ComponentLabelsJson", (object?)item.ComponentLabelsJson ?? DBNull.Value);
            
            // Add print group
            command.Parameters.AddWithValue("@PrintGroupId", (object?)item.PrintGroupId ?? DBNull.Value);
        }

        private static string NormalizeItemType(string? itemType)
        {
            return itemType?.Trim().ToLowerInvariant() switch
            {
                "drink" => "Drink",
                "other" => "Other",
                _ => "Food"
            };
        }
        
        /// <summary>
        /// Load components for a meal deal item
        /// </summary>
        public async Task<List<MenuItemComponent>> GetItemComponentsAsync(string menuItemId)
        {
            var components = new List<MenuItemComponent>();
            
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                
                var query = @"
                    SELECT Id, MenuItemId, ComponentName, ComponentPrice, ComponentType,
                           VatRate, SortOrder, CreatedAt, UpdatedAt
                    FROM MenuItemComponents
                    WHERE MenuItemId = @MenuItemId
                    ORDER BY SortOrder ASC";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    components.Add(new MenuItemComponent
                    {
                        Id = reader.GetInt32("Id"),
                        MenuItemId = reader.GetString("MenuItemId"),
                        ComponentName = reader.GetString("ComponentName"),
                        ComponentPrice = reader.GetDecimal("ComponentPrice"),
                        ComponentType = reader.GetString("ComponentType"),
                        VatRate = reader.GetDecimal("VatRate"),
                        SortOrder = reader.GetInt32("SortOrder"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.GetDateTime("UpdatedAt")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading components: {ex.Message}");
            }
            
            return components;
        }
        
        /// <summary>
        /// Save components for a meal deal item (replaces all existing)
        /// </summary>
        public async Task<bool> SaveItemComponentsAsync(string menuItemId, List<MenuItemComponent> components)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                
                // Delete existing components
                var deleteQuery = "DELETE FROM MenuItemComponents WHERE MenuItemId = @MenuItemId";
                using (var deleteCommand = new MySqlCommand(deleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                    await deleteCommand.ExecuteNonQueryAsync();
                }
                
                // Insert new components
                if (components.Count > 0)
                {
                    var insertQuery = @"
                        INSERT INTO MenuItemComponents 
                        (MenuItemId, ComponentName, ComponentPrice, ComponentType, VatRate, SortOrder, CreatedAt, UpdatedAt)
                        VALUES 
                        (@MenuItemId, @ComponentName, @ComponentPrice, @ComponentType, @VatRate, @SortOrder, @CreatedAt, @UpdatedAt)";
                    
                    foreach (var component in components)
                    {
                        using var insertCommand = new MySqlCommand(insertQuery, connection);
                        insertCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        insertCommand.Parameters.AddWithValue("@ComponentName", component.ComponentName);
                        insertCommand.Parameters.AddWithValue("@ComponentPrice", component.ComponentPrice);
                        insertCommand.Parameters.AddWithValue("@ComponentType", component.ComponentType);
                        insertCommand.Parameters.AddWithValue("@VatRate", component.VatRate);
                        insertCommand.Parameters.AddWithValue("@SortOrder", component.SortOrder);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                        insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving components: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save quick notes for a menu item (replaces all existing)
        /// </summary>
        public async Task<bool> SaveQuickNotesAsync(string menuItemId, List<MenuItemQuickNote> notes)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureQuickNotesSchemaAsync(connection);

                await using var transaction = await connection.BeginTransactionAsync();
                
                // Delete existing quick notes
                var deleteQuery = "DELETE FROM MenuItemQuickNotes WHERE MenuItemId = @MenuItemId";
                using (var deleteCommand = new MySqlCommand(deleteQuery, connection, transaction))
                {
                    deleteCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                    await deleteCommand.ExecuteNonQueryAsync();
                }
                
                // Insert new notes
                if (notes.Count > 0)
                {
                    var insertQuery = @"
                        INSERT INTO MenuItemQuickNotes 
                        (Id, MenuItemId, NoteText, DisplayOrder, Active, CreatedAt, UpdatedAt)
                        VALUES 
                        (@Id, @MenuItemId, @NoteText, @DisplayOrder, @Active, @CreatedAt, @UpdatedAt)";
                    
                    foreach (var note in notes)
                    {
                        var noteText = (note.NoteText ?? string.Empty).Trim();
                        if (noteText.Length > 255)
                        {
                            noteText = noteText[..255];
                        }

                        if (string.IsNullOrWhiteSpace(noteText))
                        {
                            continue;
                        }

                        using var insertCommand = new MySqlCommand(insertQuery, connection, transaction);
                        insertCommand.Parameters.AddWithValue("@Id", string.IsNullOrWhiteSpace(note.Id) ? Guid.NewGuid().ToString() : note.Id);
                        insertCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        insertCommand.Parameters.AddWithValue("@NoteText", noteText);
                        insertCommand.Parameters.AddWithValue("@DisplayOrder", note.DisplayOrder);
                        insertCommand.Parameters.AddWithValue("@Active", note.Active);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", note.CreatedAt == default ? DateTime.Now : note.CreatedAt);
                        insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving quick notes for item {menuItemId}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MenuItemService] Error saving quick notes for item {menuItemId}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Get quick notes for a menu item
        /// </summary>
        public async Task<List<MenuItemQuickNote>> GetQuickNotesAsync(string menuItemId)
        {
            var notes = new List<MenuItemQuickNote>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureQuickNotesSchemaAsync(connection);

                var query = @"
                    SELECT Id, MenuItemId, NoteText, DisplayOrder, Active, CreatedAt, UpdatedAt
                    FROM MenuItemQuickNotes
                    WHERE MenuItemId = @MenuItemId AND Active = TRUE
                    ORDER BY DisplayOrder ASC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@MenuItemId", menuItemId);
                
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    notes.Add(new MenuItemQuickNote
                    {
                        Id = reader.GetString("Id"),
                        MenuItemId = reader.GetString("MenuItemId"),
                        NoteText = reader.IsDBNull(reader.GetOrdinal("NoteText")) ? string.Empty : reader.GetString("NoteText"),
                        DisplayOrder = reader.GetInt32("DisplayOrder"),
                        Active = reader.GetBoolean("Active"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.GetDateTime("UpdatedAt")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading quick notes: {ex.Message}");
            }

            return notes;
        }
    }
}
