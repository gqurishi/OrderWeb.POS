using MySqlConnector;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public class OrderNumberService
    {
        private readonly DatabaseService _databaseService;

        public OrderNumberService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        /// <summary>
        /// Generates a new order number in format: [Prefix][Type][Number]
        /// Example: KITT0001 (KIT + T + 0001)
        /// </summary>
        public async Task<string> GenerateOrderNumberAsync(string orderType)
        {
            using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureOrderNumberSchemaAsync(connection);

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // Get current settings
                var settings = await GetOrderNumberSettingsAsync(connection, transaction);
                
                string prefix = settings.Prefix;
                int counter = settings.Counter;
                DateTime counterDate = settings.CounterDate;
                DateTime today = DateTime.Today;

                // Reset counter if date changed
                if (counterDate.Date != today.Date)
                {
                    counter = 0;
                    counterDate = today;
                }

                // Increment counter for this order
                int currentNumber = counter + 1;
                counter = currentNumber;

                // Update settings with new counter
                await UpdateCounterAsync(connection, transaction, prefix, counter, counterDate);

                // Commit transaction
                await transaction.CommitAsync();

                // Generate order number
                string typeChar = GetOrderTypeChar(orderType);
                string orderNumber = $"{prefix}{typeChar}{currentNumber:D4}";

                return orderNumber;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Gets the current order number settings
        /// </summary>
        private async Task<(string Prefix, int Counter, DateTime CounterDate)> GetOrderNumberSettingsAsync(
            MySqlConnection connection, MySqlTransaction transaction)
        {
            string query = @"
                SELECT order_prefix, daily_counter, counter_date 
                FROM order_number_settings 
                LIMIT 1";

            using var command = new MySqlCommand(query, connection, transaction);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string prefix = reader.GetString("order_prefix");
                int counter = reader.GetInt32("daily_counter");
                DateTime counterDate = reader.GetDateTime("counter_date");
                return (prefix, counter, counterDate);
            }

            // If no settings exist, create default
            await reader.CloseAsync();
            await CreateDefaultSettingsAsync(connection, transaction);
            return ("KIT", 0, DateTime.Today);
        }

        /// <summary>
        /// Updates the counter in database
        /// </summary>
        private async Task UpdateCounterAsync(MySqlConnection connection, MySqlTransaction transaction, 
            string prefix, int counter, DateTime counterDate)
        {
            string query = @"
                UPDATE order_number_settings 
                SET daily_counter = @counter, 
                    counter_date = @counterDate,
                    order_prefix = @prefix
                LIMIT 1";

            using var command = new MySqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@counter", counter);
            command.Parameters.AddWithValue("@counterDate", counterDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("@prefix", prefix);

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Creates default settings if none exist
        /// </summary>
        private async Task CreateDefaultSettingsAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            string query = @"
                INSERT INTO order_number_settings (order_prefix, daily_counter, counter_date)
                VALUES ('KIT', 0, CURDATE())";

            using var command = new MySqlCommand(query, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets current prefix and today's order count for display
        /// </summary>
        public async Task<(string Prefix, int TodayCount)> GetCurrentSettingsAsync()
        {
            using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureOrderNumberSchemaAsync(connection);

            string query = @"
                SELECT order_prefix, daily_counter, counter_date 
                FROM order_number_settings 
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                string prefix = reader.GetString("order_prefix");
                int counter = reader.GetInt32("daily_counter");
                DateTime counterDate = reader.GetDateTime("counter_date");

                // If date is not today, counter should be 0
                if (counterDate.Date != DateTime.Today.Date)
                {
                    return (prefix, 0);
                }

                return (prefix, counter);
            }

            return ("KIT", 0);
        }

        /// <summary>
        /// Updates the order prefix (admin setting)
        /// </summary>
        public async Task UpdatePrefixAsync(string newPrefix)
        {
            if (string.IsNullOrWhiteSpace(newPrefix) || newPrefix.Length != 3)
            {
                throw new ArgumentException("Prefix must be exactly 3 letters");
            }

            // Validate only letters
            if (!char.IsLetter(newPrefix[0]) || !char.IsLetter(newPrefix[1]) || !char.IsLetter(newPrefix[2]))
            {
                throw new ArgumentException("Prefix must contain only letters");
            }

            newPrefix = newPrefix.ToUpper();

            using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureOrderNumberSchemaAsync(connection);

            string query = @"
                UPDATE order_number_settings 
                SET order_prefix = @prefix
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@prefix", newPrefix);

            await command.ExecuteNonQueryAsync();
        }

        private async Task EnsureOrderNumberSchemaAsync(MySqlConnection connection)
        {
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS order_number_settings (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    order_prefix VARCHAR(3) NOT NULL DEFAULT 'KIT',
                    daily_counter INT NOT NULL DEFAULT 0,
                    counter_date DATE NOT NULL,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )";

            using (var createCommand = new MySqlCommand(createTableSql, connection))
            {
                await createCommand.ExecuteNonQueryAsync();
            }

            // Ensure legacy databases are upgraded from VARCHAR(2) to VARCHAR(3).
            string alterPrefixSql = @"
                ALTER TABLE order_number_settings
                MODIFY COLUMN order_prefix VARCHAR(3) NOT NULL DEFAULT 'KIT'";

            try
            {
                using var alterCommand = new MySqlCommand(alterPrefixSql, connection);
                await alterCommand.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore if engine/version treats this as no-op or already compatible.
            }

            string ensureRowSql = @"
                INSERT INTO order_number_settings (order_prefix, daily_counter, counter_date)
                SELECT 'KIT', 0, CURDATE()
                WHERE NOT EXISTS (SELECT 1 FROM order_number_settings LIMIT 1)";

            using var ensureRowCommand = new MySqlCommand(ensureRowSql, connection);
            await ensureRowCommand.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Converts order type to single character
        /// </summary>
        private string GetOrderTypeChar(string orderType)
        {
            return orderType?.ToUpper() switch
            {
                "COLLECTION" or "COL" => "C",
                "DELIVERY" or "DEL" => "D",
                "TABLE" or "TBL" => "T",
                "WEB" => "W",
                _ => "C" // Default to Collection
            };
        }

        /// <summary>
        /// Generates preview order numbers for display
        /// </summary>
        public async Task<(string Collection, string Delivery, string Table, string Web)> GetNextOrderNumbersPreviewAsync()
        {
            var settings = await GetCurrentSettingsAsync();
            string prefix = settings.Prefix;
            int nextNumber = settings.TodayCount + 1;

            return (
                Collection: $"{prefix}C{nextNumber:D4}",
                Delivery: $"{prefix}D{nextNumber:D4}",
                Table: $"{prefix}T{nextNumber:D4}",
                Web: $"{prefix}W{nextNumber:D4}"
            );
        }
    }
}
