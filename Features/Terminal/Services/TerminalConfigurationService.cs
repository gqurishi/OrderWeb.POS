using Microsoft.Maui.Storage;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public static class TerminalConfigurationService
{
    private const string Prefix = "terminal_config_";
    private const string IsConfiguredKey = Prefix + "is_configured";
    private const string ModeKey = Prefix + "mode";
    private const string TerminalNameKey = Prefix + "terminal_name";
    private const string DatabaseHostKey = Prefix + "database_host";
    private const string DatabasePortKey = Prefix + "database_port";
    private const string DatabaseNameKey = Prefix + "database_name";
    private const string DatabaseUserKey = Prefix + "database_user";
    private const string DatabasePasswordKey = Prefix + "database_password";

    public static bool IsConfigured => Preferences.Default.Get(IsConfiguredKey, false);
    public static bool IsMotherTerminal => GetConfiguration().IsMother;
    public static bool IsChildTerminal => GetConfiguration().IsChild;

    public static TerminalConfiguration GetConfiguration()
    {
        var modeText = Preferences.Default.Get(ModeKey, nameof(TerminalMode.Mother));
        var mode = Enum.TryParse<TerminalMode>(modeText, true, out var parsedMode)
            ? parsedMode
            : TerminalMode.Mother;

        var defaultHost = mode == TerminalMode.Mother ? "localhost" : string.Empty;

        return new TerminalConfiguration
        {
            IsConfigured = IsConfigured,
            Mode = mode,
            TerminalName = Preferences.Default.Get(TerminalNameKey, mode == TerminalMode.Mother ? "Main" : "Terminal"),
            DatabaseHost = Preferences.Default.Get(DatabaseHostKey, defaultHost),
            DatabasePort = Preferences.Default.Get(DatabasePortKey, 3306),
            DatabaseName = Preferences.Default.Get(DatabaseNameKey, "Pos-net"),
            DatabaseUser = Preferences.Default.Get(DatabaseUserKey, "root"),
            DatabasePassword = Preferences.Default.Get(DatabasePasswordKey, "root")
        };
    }

    public static void Save(TerminalConfiguration configuration)
    {
        var host = configuration.IsMother ? "localhost" : configuration.DatabaseHost.Trim();
        var terminalName = string.IsNullOrWhiteSpace(configuration.TerminalName)
            ? (configuration.IsMother ? "Main" : "Terminal")
            : configuration.TerminalName.Trim();

        Preferences.Default.Set(IsConfiguredKey, true);
        Preferences.Default.Set(ModeKey, configuration.Mode.ToString());
        Preferences.Default.Set(TerminalNameKey, terminalName);
        Preferences.Default.Set(DatabaseHostKey, string.IsNullOrWhiteSpace(host) ? "localhost" : host);
        Preferences.Default.Set(DatabasePortKey, configuration.DatabasePort <= 0 ? 3306 : configuration.DatabasePort);
        Preferences.Default.Set(DatabaseNameKey, string.IsNullOrWhiteSpace(configuration.DatabaseName) ? "Pos-net" : configuration.DatabaseName.Trim());
        Preferences.Default.Set(DatabaseUserKey, string.IsNullOrWhiteSpace(configuration.DatabaseUser) ? "root" : configuration.DatabaseUser.Trim());
        Preferences.Default.Set(DatabasePasswordKey, configuration.DatabasePassword ?? string.Empty);
    }

    public static void SetConfigured(bool isConfigured)
    {
        Preferences.Default.Set(IsConfiguredKey, isConfigured);
    }

    public static string GetPosConnectionString(
        bool includeDatabase = true,
        string? databaseName = null,
        int connectionTimeoutSeconds = 5,
        int defaultCommandTimeoutSeconds = 12,
        bool pooled = true)
    {
        var config = GetConfiguration();
        var host = string.IsNullOrWhiteSpace(config.DatabaseHost) ? "localhost" : config.DatabaseHost;
        var selectedDatabase = string.IsNullOrWhiteSpace(databaseName) ? config.DatabaseName : databaseName.Trim();
        var databasePart = includeDatabase ? $"Database={selectedDatabase};" : string.Empty;
        var poolingPart = pooled
            ? "Pooling=true;Minimum Pool Size=10;Maximum Pool Size=200;Connection Idle Timeout=60;Connection Reset=false;Keepalive=15;"
            : string.Empty;

        return $"Server={host};{databasePart}Uid={config.DatabaseUser};Pwd={config.DatabasePassword};Port={config.DatabasePort};" +
               $"Connection Timeout={connectionTimeoutSeconds};Default Command Timeout={defaultCommandTimeoutSeconds};" +
               $"{poolingPart}Allow User Variables=true;";
    }

    public static void Reset()
    {
        Preferences.Default.Remove(IsConfiguredKey);
        Preferences.Default.Remove(ModeKey);
        Preferences.Default.Remove(TerminalNameKey);
        Preferences.Default.Remove(DatabaseHostKey);
        Preferences.Default.Remove(DatabasePortKey);
        Preferences.Default.Remove(DatabaseNameKey);
        Preferences.Default.Remove(DatabaseUserKey);
        Preferences.Default.Remove(DatabasePasswordKey);
    }
}
