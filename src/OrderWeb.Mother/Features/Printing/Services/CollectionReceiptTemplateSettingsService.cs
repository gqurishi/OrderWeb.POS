using System.Text.Json;
using System.Text.Json.Serialization;
using MySqlConnector;

namespace POS_in_NET.Services;

public sealed class CollectionReceiptTemplateSettings
{
    public const int FooterMaxLength = 60;
    public const string DefaultFooterText = "Thank you for your order";

    public KitchenHeadingSize BusinessNameSize { get; set; } = KitchenHeadingSize.Large;
    public bool BusinessNameBold { get; set; } = true;
    public KitchenHeadingSize AddressSize { get; set; } = KitchenHeadingSize.Normal;
    public bool AddressBold { get; set; }
    public KitchenHeadingSize PhoneSize { get; set; } = KitchenHeadingSize.Normal;
    public bool PhoneBold { get; set; }
    public KitchenHeadingSize VatSize { get; set; } = KitchenHeadingSize.Normal;
    public bool VatBold { get; set; }
    public KitchenHeadingSize HeadingSize { get; set; } = KitchenHeadingSize.Large;
    public bool HeadingBold { get; set; } = true;
    public KitchenHeadingSize OrderInfoSize { get; set; } = KitchenHeadingSize.Normal;
    public bool OrderInfoBold { get; set; }
    public KitchenHeadingSize CustomerNameSize { get; set; } = KitchenHeadingSize.Normal;
    public bool CustomerNameBold { get; set; }
    public KitchenHeadingSize CustomerPhoneSize { get; set; } = KitchenHeadingSize.Normal;
    public bool CustomerPhoneBold { get; set; }
    public KitchenHeadingSize DeliveryAddressSize { get; set; } = KitchenHeadingSize.Normal;
    public bool DeliveryAddressBold { get; set; }
    public KitchenHeadingSize TableNumberSize { get; set; } = KitchenHeadingSize.Normal;
    public bool TableNumberBold { get; set; }
    public KitchenHeadingSize ServiceChargeSize { get; set; } = KitchenHeadingSize.Normal;
    public bool ServiceChargeBold { get; set; }
    public KitchenHeadingSize PaidStatusSize { get; set; } = KitchenHeadingSize.Large;
    public bool PaidStatusBold { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KitchenHeadingSize? CustomerInfoSize { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CustomerInfoBold { get; set; }
    public KitchenHeadingSize PaymentSize { get; set; } = KitchenHeadingSize.Normal;
    public bool PaymentBold { get; set; }
    public string FooterText { get; set; } = DefaultFooterText;

    public static CollectionReceiptTemplateSettings Default() => new();

    public CollectionReceiptTemplateSettings Normalized()
    {
        if (CustomerInfoSize.HasValue)
        {
            CustomerNameSize = CustomerInfoSize.Value;
            CustomerPhoneSize = CustomerInfoSize.Value;
            CustomerInfoSize = null;
        }

        if (CustomerInfoBold.HasValue)
        {
            CustomerNameBold = CustomerInfoBold.Value;
            CustomerPhoneBold = CustomerInfoBold.Value;
            CustomerInfoBold = null;
        }

        FooterText = NormalizeFooterText(FooterText);
        return this;
    }

    public static string NormalizeFooterText(string? footerText)
    {
        var value = string.IsNullOrWhiteSpace(footerText)
            ? DefaultFooterText
            : string.Join(' ', footerText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return value.Length <= FooterMaxLength ? value : value[..FooterMaxLength].TrimEnd();
    }
}

public sealed class CollectionReceiptTemplateSettingsService
{
    private const string SettingsKey = "printing.templates.collection_receipt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseService _databaseService;

    public CollectionReceiptTemplateSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<CollectionReceiptTemplateSettings> GetSettingsAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);

            var value = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(value))
            {
                return CollectionReceiptTemplateSettings.Default();
            }

            return (JsonSerializer.Deserialize<CollectionReceiptTemplateSettings>(value, JsonOptions)
                    ?? CollectionReceiptTemplateSettings.Default())
                .Normalized();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load collection receipt settings: {ex.Message}");
            return CollectionReceiptTemplateSettings.Default();
        }
    }

    public async Task<bool> SaveSettingsAsync(CollectionReceiptTemplateSettings settings)
    {
        try
        {
            settings = settings.Normalized();
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            command.Parameters.AddWithValue("@value", json);
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save collection receipt settings: {ex.Message}");
            return false;
        }
    }
}

public sealed class DeliveryReceiptTemplateSettingsService
{
    private const string SettingsKey = "printing.templates.delivery_receipt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseService _databaseService;

    public DeliveryReceiptTemplateSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<CollectionReceiptTemplateSettings> GetSettingsAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);

            var value = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(value))
            {
                return CollectionReceiptTemplateSettings.Default();
            }

            return (JsonSerializer.Deserialize<CollectionReceiptTemplateSettings>(value, JsonOptions)
                    ?? CollectionReceiptTemplateSettings.Default())
                .Normalized();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load delivery receipt settings: {ex.Message}");
            return CollectionReceiptTemplateSettings.Default();
        }
    }

    public async Task<bool> SaveSettingsAsync(CollectionReceiptTemplateSettings settings)
    {
        try
        {
            settings = settings.Normalized();
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            command.Parameters.AddWithValue("@value", json);
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save delivery receipt settings: {ex.Message}");
            return false;
        }
    }
}

public sealed class TableBillReceiptTemplateSettingsService
{
    private const string SettingsKey = "printing.templates.table_bill_receipt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseService _databaseService;

    public TableBillReceiptTemplateSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<CollectionReceiptTemplateSettings> GetSettingsAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);

            var value = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(value))
            {
                return CollectionReceiptTemplateSettings.Default();
            }

            return (JsonSerializer.Deserialize<CollectionReceiptTemplateSettings>(value, JsonOptions)
                    ?? CollectionReceiptTemplateSettings.Default())
                .Normalized();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load table bill receipt settings: {ex.Message}");
            return CollectionReceiptTemplateSettings.Default();
        }
    }

    public async Task<bool> SaveSettingsAsync(CollectionReceiptTemplateSettings settings)
    {
        try
        {
            settings = settings.Normalized();
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            command.Parameters.AddWithValue("@value", json);
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save table bill receipt settings: {ex.Message}");
            return false;
        }
    }
}

public sealed class TablePaymentReceiptTemplateSettingsService
{
    private const string SettingsKey = "printing.templates.table_payment_receipt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseService _databaseService;

    public TablePaymentReceiptTemplateSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<CollectionReceiptTemplateSettings> GetSettingsAsync()
    {
        try
        {
            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                "SELECT setting_value FROM settings WHERE setting_key = @key LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);

            var value = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(value))
            {
                return CollectionReceiptTemplateSettings.Default();
            }

            return (JsonSerializer.Deserialize<CollectionReceiptTemplateSettings>(value, JsonOptions)
                    ?? CollectionReceiptTemplateSettings.Default())
                .Normalized();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load table payment receipt settings: {ex.Message}");
            return CollectionReceiptTemplateSettings.Default();
        }
    }

    public async Task<bool> SaveSettingsAsync(CollectionReceiptTemplateSettings settings)
    {
        try
        {
            settings = settings.Normalized();
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = new MySqlCommand(
                """
                INSERT INTO settings (setting_key, setting_value)
                VALUES (@key, @value)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)
                """,
                connection);
            command.Parameters.AddWithValue("@key", SettingsKey);
            command.Parameters.AddWithValue("@value", json);
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save table payment receipt settings: {ex.Message}");
            return false;
        }
    }
}
