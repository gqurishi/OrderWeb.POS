using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services
{
    public class DeliveryCustomerService
    {
        private readonly string _connectionString;
        private readonly CustomerDataService _customerDataService = new();
        private bool _tableChecked = false;

        public DeliveryCustomerService()
        {
            _connectionString = TerminalConfigurationService.GetPosConnectionString(pooled: false);
        }

        private async Task EnsureTableExistsAsync(MySqlConnection connection)
        {
            if (RuntimeSchemaPolicy.IsMigrationManaged) return;
            if (_tableChecked) return;

            try
            {
                var createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS delivery_customers (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        name VARCHAR(255) NOT NULL,
                        phone_number VARCHAR(50) NOT NULL,
                        address TEXT NOT NULL,
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

        public Task<List<DeliveryCustomer>> SearchCustomersByNameAsync(string searchName)
        {
            return SearchCustomersAsync(addressOrPostcode: null, name: searchName, phone: searchName);
        }

        /// <summary>
        /// Search local delivery customers by address/postcode, name, and/or phone.
        /// Any non-empty field is matched with OR logic.
        /// </summary>
        public async Task<List<DeliveryCustomer>> SearchCustomersAsync(string? addressOrPostcode, string? name, string? phone)
        {
            var customers = new List<DeliveryCustomer>();
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(name))
            {
                conditions.Add("name LIKE @name");
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                conditions.Add("phone_number LIKE @phone");
            }

            if (!string.IsNullOrWhiteSpace(addressOrPostcode))
            {
                conditions.Add("address LIKE @address");
            }

            if (conditions.Count == 0)
            {
                return customers;
            }

            try
            {
                using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = $@"
                    SELECT id, name, phone_number, address, created_at, last_order_date
                    FROM delivery_customers
                    WHERE {string.Join(" OR ", conditions)}
                    ORDER BY last_order_date DESC, name ASC
                    LIMIT 50";

                using var command = new MySqlCommand(query, connection);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    command.Parameters.AddWithValue("@name", $"%{name.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(phone))
                {
                    command.Parameters.AddWithValue("@phone", $"%{phone.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(addressOrPostcode))
                {
                    command.Parameters.AddWithValue("@address", $"%{addressOrPostcode.Trim()}%");
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    customers.Add(ReadCustomer(reader));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching customers: {ex.Message}");
            }

            return customers;
        }

        private static DeliveryCustomer ReadCustomer(MySqlDataReader reader)
        {
            return new DeliveryCustomer
            {
                Id = reader.GetInt32("id"),
                Name = reader.GetString("name"),
                PhoneNumber = reader.GetString("phone_number"),
                Address = reader.GetString("address"),
                CreatedAt = reader.GetDateTime("created_at"),
                LastOrderDate = reader.IsDBNull(reader.GetOrdinal("last_order_date"))
                    ? null
                    : reader.GetDateTime("last_order_date")
            };
        }

        public async Task<DeliveryCustomer> SaveCustomerAsync(string name, string phoneNumber, string address)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                // Check if customer exists by phone number
                var checkQuery = "SELECT id, name, phone_number, address, created_at, last_order_date FROM delivery_customers WHERE phone_number = @phoneNumber";
                using var checkCommand = new MySqlCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);

                using var reader = await checkCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    // Customer exists, update address if changed and return
                    var existingCustomer = new DeliveryCustomer
                    {
                        Id = reader.GetInt32("id"),
                        Name = reader.GetString("name"),
                        PhoneNumber = reader.GetString("phone_number"),
                        Address = reader.GetString("address"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastOrderDate = reader.IsDBNull(reader.GetOrdinal("last_order_date")) 
                            ? null 
                            : reader.GetDateTime("last_order_date")
                    };
                    await reader.CloseAsync();

                    var nameChanged = !existingCustomer.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
                    var addressChanged = existingCustomer.Address != address;
                    if (nameChanged || addressChanged)
                    {
                        var updateQuery = "UPDATE delivery_customers SET name = @name, address = @address WHERE id = @id";
                        using var updateCommand = new MySqlCommand(updateQuery, connection);
                        updateCommand.Parameters.AddWithValue("@name", name);
                        updateCommand.Parameters.AddWithValue("@address", address);
                        updateCommand.Parameters.AddWithValue("@id", existingCustomer.Id);
                        await updateCommand.ExecuteNonQueryAsync();
                        existingCustomer.Name = name;
                        existingCustomer.Address = address;
                    }

                    await SyncToCustomerDataAsync(name, phoneNumber, address);
                    return existingCustomer;
                }
                await reader.CloseAsync();

                // Insert new customer
                var insertQuery = @"
                    INSERT INTO delivery_customers (name, phone_number, address, created_at) 
                    VALUES (@name, @phoneNumber, @address, @createdAt);
                    SELECT LAST_INSERT_ID();";

                using var insertCommand = new MySqlCommand(insertQuery, connection);
                insertCommand.Parameters.AddWithValue("@name", name);
                insertCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);
                insertCommand.Parameters.AddWithValue("@address", address);
                insertCommand.Parameters.AddWithValue("@createdAt", DateTime.Now);

                var newId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());

                var customer = new DeliveryCustomer
                {
                    Id = newId,
                    Name = name,
                    PhoneNumber = phoneNumber,
                    Address = address,
                    CreatedAt = DateTime.Now
                };

                await SyncToCustomerDataAsync(name, phoneNumber, address);
                return customer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving customer: {ex.Message}");
                return null!;
            }
        }

        private async Task SyncToCustomerDataAsync(string name, string phoneNumber, string address)
        {
            try
            {
                await _customerDataService.UpsertDeliveryCustomerAsync(
                    name,
                    phoneNumber,
                    address,
                    city: ParseAddressLine(address, 1),
                    county: null,
                    postcode: ParseAddressLine(address, 2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeliveryCustomer] Customer Data sync failed: {ex.Message}");
            }
        }

        public async Task UpdateLastOrderDateAsync(int customerId)
        {
            try
            {
                using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = "UPDATE delivery_customers SET last_order_date = @lastOrderDate WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@lastOrderDate", DateTime.Now);
                command.Parameters.AddWithValue("@id", customerId);

                await command.ExecuteNonQueryAsync();

                using var phoneCommand = new MySqlCommand("SELECT phone_number FROM delivery_customers WHERE id = @id", connection);
                phoneCommand.Parameters.AddWithValue("@id", customerId);
                var phone = Convert.ToString(await phoneCommand.ExecuteScalarAsync());
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    await _customerDataService.UpdateLastDeliveryOrderDateAsync(phone);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating last order date: {ex.Message}");
            }
        }

        private static string? ParseAddressLine(string address, int index)
        {
            var lines = address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > index ? lines[index] : null;
        }
    }
}
