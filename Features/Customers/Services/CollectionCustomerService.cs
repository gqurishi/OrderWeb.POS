using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services
{
    public class CollectionCustomerService
    {
        private readonly string _connectionString;
        private readonly CustomerDataService _customerDataService = new();
        private bool _tableChecked = false;

        public CollectionCustomerService()
        {
            _connectionString = TerminalConfigurationService.GetPosConnectionString(pooled: false);
        }

        private async Task EnsureTableExistsAsync(MySqlConnection connection)
        {
            if (_tableChecked) return;

            try
            {
                var createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS collection_customers (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        name VARCHAR(255) NOT NULL,
                        phone_number VARCHAR(50) NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        last_order_date DATETIME NULL,
                        INDEX idx_phone (phone_number),
                        INDEX idx_name (name)
                    )";

                using var command = new MySqlCommand(createTableQuery, connection);
                await command.ExecuteNonQueryAsync();
                _tableChecked = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating table: {ex.Message}");
            }
        }

        public async Task<List<CollectionCustomer>> SearchCustomersByNameAsync(string? searchName, string? phoneNumber = null)
        {
            var customers = new List<CollectionCustomer>();
            
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = @"
                    SELECT id, name, phone_number, created_at, last_order_date 
                    FROM collection_customers 
                    WHERE (@searchName <> '' AND name LIKE @searchNamePattern)
                       OR (@phoneNumber <> '' AND phone_number LIKE @phonePattern)
                    ORDER BY last_order_date DESC, name ASC 
                    LIMIT 50";

                using var command = new MySqlCommand(query, connection);
                var normalizedName = searchName?.Trim() ?? string.Empty;
                var normalizedPhone = phoneNumber?.Trim() ?? string.Empty;
                command.Parameters.AddWithValue("@searchName", normalizedName);
                command.Parameters.AddWithValue("@searchNamePattern", $"%{normalizedName}%");
                command.Parameters.AddWithValue("@phoneNumber", normalizedPhone);
                command.Parameters.AddWithValue("@phonePattern", $"%{normalizedPhone}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    customers.Add(new CollectionCustomer
                    {
                        Id = reader.GetInt32("id"),
                        Name = reader.GetString("name"),
                        PhoneNumber = reader.GetString("phone_number"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastOrderDate = reader.IsDBNull(reader.GetOrdinal("last_order_date")) 
                            ? null 
                            : reader.GetDateTime("last_order_date")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching customers: {ex.Message}");
            }

            return customers;
        }

        public async Task<CollectionCustomer> SaveCustomerAsync(string name, string phoneNumber)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                // Check if customer exists by phone number
                var checkQuery = "SELECT id, name, phone_number, created_at, last_order_date FROM collection_customers WHERE phone_number = @phoneNumber";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);

                using var reader = await checkCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    // Customer exists, return existing
                    var existingCustomer = new CollectionCustomer
                    {
                        Id = reader.GetInt32("id"),
                        Name = reader.GetString("name"),
                        PhoneNumber = reader.GetString("phone_number"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastOrderDate = reader.IsDBNull(reader.GetOrdinal("last_order_date")) 
                            ? null 
                            : reader.GetDateTime("last_order_date")
                    };
                    await reader.CloseAsync();

                    if (!existingCustomer.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        var updateQuery = "UPDATE collection_customers SET name = @name WHERE id = @id";
                        using var updateCommand = new MySqlCommand(updateQuery, connection);
                        updateCommand.Parameters.AddWithValue("@name", name);
                        updateCommand.Parameters.AddWithValue("@id", existingCustomer.Id);
                        await updateCommand.ExecuteNonQueryAsync();
                        existingCustomer.Name = name;
                    }

                    await SyncToCustomerDataAsync(name, phoneNumber);
                    return existingCustomer;
                }
                await reader.CloseAsync();

                // Insert new customer
                var insertQuery = @"
                    INSERT INTO collection_customers (name, phone_number, created_at) 
                    VALUES (@name, @phoneNumber, @createdAt);
                    SELECT LAST_INSERT_ID();";

                using var insertCommand = new MySqlCommand(insertQuery, connection);
                insertCommand.Parameters.AddWithValue("@name", name);
                insertCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);
                insertCommand.Parameters.AddWithValue("@createdAt", DateTime.Now);

                var newId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());

                var customer = new CollectionCustomer
                {
                    Id = newId,
                    Name = name,
                    PhoneNumber = phoneNumber,
                    CreatedAt = DateTime.Now
                };

                await SyncToCustomerDataAsync(name, phoneNumber);
                return customer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving customer: {ex.Message}");
                return null!;
            }
        }

        private async Task SyncToCustomerDataAsync(string name, string phoneNumber)
        {
            try
            {
                await _customerDataService.UpsertCollectionCustomerAsync(name, phoneNumber);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectionCustomer] Customer Data sync failed: {ex.Message}");
            }
        }

        public async Task UpdateLastOrderDateAsync(int customerId)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = "UPDATE collection_customers SET last_order_date = @lastOrderDate WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@lastOrderDate", DateTime.Now);
                command.Parameters.AddWithValue("@id", customerId);

                await command.ExecuteNonQueryAsync();

                using var phoneCommand = new MySqlCommand("SELECT phone_number FROM collection_customers WHERE id = @id", connection);
                phoneCommand.Parameters.AddWithValue("@id", customerId);
                var phone = Convert.ToString(await phoneCommand.ExecuteScalarAsync());
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    await _customerDataService.UpdateLastCollectionOrderDateAsync(phone);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating last order date: {ex.Message}");
            }
        }
    }
}
