using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services
{
    public class DeliveryCustomerService
    {
        private readonly string _connectionString;
        private bool _tableChecked = false;

        public DeliveryCustomerService()
        {
            var host = "localhost";
            var user = "root";
            var password = "root";
            var database = "Pos-net";
            var port = "3306";
            
            _connectionString = $"Server={host};Database={database};Uid={user};Pwd={password};Port={port};Connection Timeout=5;";
        }

        private async Task EnsureTableExistsAsync(MySqlConnection connection)
        {
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

        public async Task<List<DeliveryCustomer>> SearchCustomersByNameAsync(string searchName)
        {
            var customers = new List<DeliveryCustomer>();
            
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = @"
                    SELECT id, name, phone_number, address, created_at, last_order_date 
                    FROM delivery_customers 
                    WHERE name LIKE @searchName OR phone_number LIKE @searchName
                    ORDER BY last_order_date DESC, name ASC 
                    LIMIT 50";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@searchName", $"%{searchName}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    customers.Add(new DeliveryCustomer
                    {
                        Id = reader.GetInt32("id"),
                        Name = reader.GetString("name"),
                        PhoneNumber = reader.GetString("phone_number"),
                        Address = reader.GetString("address"),
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

        public async Task<DeliveryCustomer> SaveCustomerAsync(string name, string phoneNumber, string address)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
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
                    
                    // Update address if different
                    if (existingCustomer.Address != address)
                    {
                        var updateQuery = "UPDATE delivery_customers SET address = @address WHERE id = @id";
                        using var updateCommand = new MySqlCommand(updateQuery, connection);
                        updateCommand.Parameters.AddWithValue("@address", address);
                        updateCommand.Parameters.AddWithValue("@id", existingCustomer.Id);
                        await updateCommand.ExecuteNonQueryAsync();
                        existingCustomer.Address = address;
                    }
                    
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

                return new DeliveryCustomer
                {
                    Id = newId,
                    Name = name,
                    PhoneNumber = phoneNumber,
                    Address = address,
                    CreatedAt = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving customer: {ex.Message}");
                return null!;
            }
        }

        public async Task UpdateLastOrderDateAsync(int customerId)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                await EnsureTableExistsAsync(connection);

                var query = "UPDATE delivery_customers SET last_order_date = @lastOrderDate WHERE id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@lastOrderDate", DateTime.Now);
                command.Parameters.AddWithValue("@id", customerId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating last order date: {ex.Message}");
            }
        }
    }
}
