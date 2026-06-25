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

                var query = @"
                    SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                           Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                           vat_config_type, vat_category, calculated_vat_rate,
                           label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    ORDER BY IsFeatured DESC, DisplayOrder ASC, CreatedAt DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    items.Add(ParseFoodMenuItem(reader));
                }
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

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate,
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

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate,
                          label_text, print_component_labels, component_labels_json, print_group_id
                    FROM FoodMenuItems
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return ParseFoodMenuItem(reader);
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

                // Generate new ID if not provided
                if (string.IsNullOrEmpty(item.Id))
                {
                    item.Id = $"item-{Guid.NewGuid()}";
                }

                var query = @"
                    INSERT INTO FoodMenuItems 
                    (Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder, IsFeatured,
                     PreparationTime, VatRate, VatType, IsVatExempt, VatNotes, Addons, Tags, print_in_red,
                     vat_config_type, vat_category, calculated_vat_rate, label_text, print_component_labels,
                     component_labels_json, print_group_id, CreatedAt, UpdatedAt)
                    VALUES 
                    (@Id, @CategoryId, @Name, @Description, @Price, @PriceDineIn, @PriceTakeaway, @Color, @DisplayOrder, @IsFeatured,
                     @PreparationTime, @VatRate, @VatType, @IsVatExempt, @VatNotes, @Addons, @Tags, @PrintInRed,
                     @VatConfigType, @VatCategory, @CalculatedVatRate, @LabelText, @PrintComponentLabels,
                     @ComponentLabelsJson, @PrintGroupId, @CreatedAt, @UpdatedAt)";

                using var command = new MySqlCommand(query, connection);
                AddFoodMenuItemParameters(command, item);

                var result = await command.ExecuteNonQueryAsync();
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
                        label_text = @LabelText,
                        print_component_labels = @PrintComponentLabels,
                        component_labels_json = @ComponentLabelsJson,
                        print_group_id = @PrintGroupId,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id";

                using var command = new MySqlCommand(query, connection);
                AddFoodMenuItemParameters(command, item, isUpdate: true);

                var result = await command.ExecuteNonQueryAsync();
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

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate,
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

                var query = @"
                      SELECT Id, CategoryId, Name, Description, Price, price_dine_in, price_takeaway, Color, DisplayOrder,
                           IsFeatured, PreparationTime, VatRate, VatType, IsVatExempt, VatNotes,
                          Addons, Tags, print_in_red, CreatedAt, UpdatedAt,
                          vat_config_type, vat_category, calculated_vat_rate,
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting featured items: {ex.Message}");
                throw;
            }

            return items;
        }

        // Helper methods

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
            
            // Add label print settings
            command.Parameters.AddWithValue("@LabelText", (object?)item.LabelText ?? DBNull.Value);
            command.Parameters.AddWithValue("@PrintComponentLabels", item.PrintComponentLabels);
            command.Parameters.AddWithValue("@ComponentLabelsJson", (object?)item.ComponentLabelsJson ?? DBNull.Value);
            
            // Add print group
            command.Parameters.AddWithValue("@PrintGroupId", (object?)item.PrintGroupId ?? DBNull.Value);
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
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                
                // Delete existing quick notes
                var deleteQuery = "DELETE FROM MenuItemQuickNotes WHERE MenuItemId = @MenuItemId";
                using (var deleteCommand = new MySqlCommand(deleteQuery, connection))
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
                        using var insertCommand = new MySqlCommand(insertQuery, connection);
                        insertCommand.Parameters.AddWithValue("@Id", note.Id);
                        insertCommand.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        insertCommand.Parameters.AddWithValue("@NoteText", note.NoteText);
                        insertCommand.Parameters.AddWithValue("@DisplayOrder", note.DisplayOrder);
                        insertCommand.Parameters.AddWithValue("@Active", note.Active);
                        insertCommand.Parameters.AddWithValue("@CreatedAt", note.CreatedAt);
                        insertCommand.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving quick notes: {ex.Message}");
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
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();

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
                        NoteText = reader.GetString("NoteText"),
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
