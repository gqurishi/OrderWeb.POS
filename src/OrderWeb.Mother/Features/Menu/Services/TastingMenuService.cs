using MyFirstMauiApp.Models.FoodMenu;
using MySqlConnector;
using POS_in_NET.Services;

namespace MyFirstMauiApp.Services
{
    public class TastingMenuService
    {
        private readonly string _connectionString;
        private static bool _schemaEnsured;

        public TastingMenuService()
        {
            _connectionString = POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString();
        }

        private async Task EnsureSchemaAsync(MySqlConnection connection)
        {
            if (RuntimeSchemaPolicy.IsMigrationManaged) return;
            if (_schemaEnsured)
            {
                return;
            }

            const string sql = @"
                CREATE TABLE IF NOT EXISTS TastingMenus (
                    Id VARCHAR(36) PRIMARY KEY,
                    Name VARCHAR(150) NOT NULL,
                    Description TEXT NULL,
                    Color VARCHAR(20) NOT NULL DEFAULT '#0EA5E9',
                    ConfigJson JSON NOT NULL,
                    Active BOOLEAN NOT NULL DEFAULT TRUE,
                    DisplayOrder INT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX idx_tasting_menu_active (Active),
                    INDEX idx_tasting_menu_order (DisplayOrder)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

            using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
            _schemaEnsured = true;
        }

        public async Task<List<TastingMenu>> GetAllAsync()
        {
            var menus = new List<TastingMenu>();

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            const string query = @"
                SELECT Id, Name, Description, Color, ConfigJson, Active, DisplayOrder, CreatedAt, UpdatedAt
                FROM TastingMenus
                ORDER BY DisplayOrder ASC, CreatedAt DESC";

            using var command = new MySqlCommand(query, connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                menus.Add(Parse(reader));
            }

            return menus;
        }

        public async Task<List<TastingMenu>> GetActiveAsync()
        {
            var all = await GetAllAsync();
            return all
                .Where(menu => menu.Active && menu.Options.Count > 0 && menu.Courses.Count > 0)
                .ToList();
        }

        public async Task<bool> CreateAsync(TastingMenu menu)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            if (string.IsNullOrWhiteSpace(menu.Id))
            {
                menu.Id = Guid.NewGuid().ToString();
            }

            const string query = @"
                INSERT INTO TastingMenus
                (Id, Name, Description, Color, ConfigJson, Active, DisplayOrder, CreatedAt, UpdatedAt)
                VALUES
                (@Id, @Name, @Description, @Color, @ConfigJson, @Active, @DisplayOrder, @CreatedAt, @UpdatedAt)";

            using var command = new MySqlCommand(query, connection);
            AddParameters(command, menu);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> UpdateAsync(TastingMenu menu)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            const string query = @"
                UPDATE TastingMenus
                SET Name = @Name,
                    Description = @Description,
                    Color = @Color,
                    ConfigJson = @ConfigJson,
                    Active = @Active,
                    DisplayOrder = @DisplayOrder,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            using var command = new MySqlCommand(query, connection);
            AddParameters(command, menu);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            using var command = new MySqlCommand("DELETE FROM TastingMenus WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Id", id);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> ToggleActiveAsync(string id)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            const string query = @"
                UPDATE TastingMenus
                SET Active = NOT Active, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        private static void AddParameters(MySqlCommand command, TastingMenu menu)
        {
            command.Parameters.AddWithValue("@Id", menu.Id);
            command.Parameters.AddWithValue("@Name", menu.Name);
            command.Parameters.AddWithValue("@Description", (object?)menu.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@Color", menu.Color);
            command.Parameters.AddWithValue("@ConfigJson", menu.ConfigJson);
            command.Parameters.AddWithValue("@Active", menu.Active);
            command.Parameters.AddWithValue("@DisplayOrder", menu.DisplayOrder);
            command.Parameters.AddWithValue("@CreatedAt", menu.CreatedAt == default ? DateTime.Now : menu.CreatedAt);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
        }

        private static TastingMenu Parse(MySqlDataReader reader)
        {
            var menu = new TastingMenu
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Color = reader.IsDBNull(reader.GetOrdinal("Color")) ? "#0EA5E9" : reader.GetString(reader.GetOrdinal("Color")),
                Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };

            TastingMenu.ApplyConfigFromJson(menu, reader.GetString(reader.GetOrdinal("ConfigJson")));
            return menu;
        }
    }
}
