using System.Text.Json;
using System.Text.Json.Serialization;
using MySqlConnector;

namespace POS_in_NET.Services;

public enum KitchenHeadingSize
{
    Normal,
    Large,
    ExtraLarge
}

public enum KitchenOrderInfoSize
{
    Normal,
    Large
}

public enum KitchenSectionHeadingStyle
{
    Normal,
    Bold,
    LargeBold
}

public sealed class KitchenTemplateSettings
{
    public const int FooterMaxLength = 60;
    public const string DefaultFooterText = "Thank you for your order";

    public KitchenHeadingSize HeadingSize { get; set; } = KitchenHeadingSize.Large;
    public bool HeadingBold { get; set; } = true;
    public KitchenHeadingSize OrderInfoSize { get; set; } = KitchenHeadingSize.Normal;
    public bool OrderInfoBold { get; set; }
    public KitchenHeadingSize SectionHeadingSize { get; set; } = KitchenHeadingSize.Normal;
    public bool SectionHeadingBold { get; set; } = true;
    public bool ShowCheckedByLine { get; set; } = true;
    public string FooterText { get; set; } = DefaultFooterText;

    [JsonPropertyName("SectionHeadingStyle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KitchenSectionHeadingStyle? LegacySectionHeadingStyle { get; set; }

    public static KitchenTemplateSettings Default() => new();

    public KitchenTemplateSettings Normalized()
    {
        if (LegacySectionHeadingStyle.HasValue)
        {
            SectionHeadingSize = LegacySectionHeadingStyle.Value == KitchenSectionHeadingStyle.LargeBold
                ? KitchenHeadingSize.Large
                : KitchenHeadingSize.Normal;
            SectionHeadingBold = LegacySectionHeadingStyle.Value != KitchenSectionHeadingStyle.Normal;
            LegacySectionHeadingStyle = null;
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

public sealed class KitchenTemplateSettingsService
{
    private const string SettingsKey = "printing.templates.kitchen";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseService _databaseService;

    public KitchenTemplateSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<KitchenTemplateSettings> GetSettingsAsync()
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
                return KitchenTemplateSettings.Default();
            }

            return (JsonSerializer.Deserialize<KitchenTemplateSettings>(value, JsonOptions)
                    ?? KitchenTemplateSettings.Default())
                .Normalized();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load kitchen template settings: {ex.Message}");
            return KitchenTemplateSettings.Default();
        }
    }

    public async Task<bool> SaveSettingsAsync(KitchenTemplateSettings settings)
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
            System.Diagnostics.Debug.WriteLine($"Failed to save kitchen template settings: {ex.Message}");
            return false;
        }
    }
}
