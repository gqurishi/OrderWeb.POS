using MySqlConnector;
using System.Diagnostics;

namespace POS_in_NET.Services
{
    /// <summary>
    /// Simple service to manually fix payment methods for existing orders
    /// </summary>
    public class PaymentFixService
    {
        private readonly DatabaseService _database;

        public PaymentFixService(DatabaseService database)
        {
            _database = database;
        }

        /// <summary>
        /// Manually fix payment methods for all today's orders
        /// This bypasses the API and just sets the correct values
        /// </summary>
        public async Task<(int Fixed, string Message)> FixTodaysPaymentsManuallyAsync()
        {
            try
            {
                Debug.WriteLine("🔧 MANUAL FIX: Setting correct payment methods...");

                // These are the known correct payment methods from OrderWeb.net website
                var fixes = new Dictionary<string, string>
                {
                    { "KIT-8297", "voucher" },  // Gift Card (NEW!)
                    { "KIT-9662", "voucher" },  // Gift Card
                    { "KIT-3002", "cash" },     // Cash
                    { "KIT-5443", "cash" },     // Cash
                    { "KIT-6863", "cash" }      // Cash
                };

                int fixedCount = 0;

                using var connection = await _database.GetConnectionAsync();

                foreach (var fix in fixes)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE orders 
                        SET payment_method = @paymentMethod
                        WHERE order_number = @orderNumber";

                    command.Parameters.AddWithValue("@paymentMethod", fix.Value);
                    command.Parameters.AddWithValue("@orderNumber", fix.Key);

                    var rows = await command.ExecuteNonQueryAsync();
                    if (rows > 0)
                    {
                        fixedCount++;
                        Debug.WriteLine($"✅ Fixed {fix.Key}: {fix.Value}");
                    }
                    else
                    {
                        Debug.WriteLine($"⚠️ Order {fix.Key} not found in database");
                    }
                }

                return (fixedCount, $"Fixed {fixedCount} out of {fixes.Count} orders");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error fixing payments: {ex.Message}");
                return (0, $"Error: {ex.Message}");
            }
        }
    }
}
