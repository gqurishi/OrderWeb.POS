using MySqlConnector;

namespace POS_in_NET.Services;

public enum ReceiptLogoSize
{
    Medium,
    Large
}

public sealed class ReceiptLogoSettingsService
{
    private const string SettingsKey = "printing.receipt.logo_size";
    private readonly DatabaseService _databaseService;

    public ReceiptLogoSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<ReceiptLogoSize> GetLogoSizeAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            var value = Convert.ToString(await command.ExecuteScalarAsync());
            return Enum.TryParse<ReceiptLogoSize>(value, true, out var size)
                ? size
                : ReceiptLogoSize.Medium;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load receipt logo size: {ex.Message}");
            return ReceiptLogoSize.Medium;
        }
    }

    public async Task<bool> SaveLogoSizeAsync(ReceiptLogoSize size)
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            command.Parameters.AddWithValue("@value", size.ToString());
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save receipt logo size: {ex.Message}");
            return false;
        }
    }
}
