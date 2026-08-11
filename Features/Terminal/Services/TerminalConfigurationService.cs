using System.Text.Json;
using Microsoft.Maui.Storage;
using MySqlConnector;
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
    private const string ProtectedDatabasePasswordKey = Prefix + "database_password_protected";
    private const string DatabaseSslModeKey = Prefix + "database_ssl_mode";
    private const string InstallerConfigAppliedKey = Prefix + "installer_db_applied";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
            DatabaseName = Preferences.Default.Get(DatabaseNameKey, PosDatabaseDefaults.ProductionDatabaseName),
            DatabaseUser = Preferences.Default.Get(DatabaseUserKey, PosDatabaseDefaults.ProductionDatabaseUser),
            DatabasePassword = ReadDatabasePassword(),
            DatabaseSslMode = Preferences.Default.Get(DatabaseSslModeKey, nameof(MySqlSslMode.Preferred))
        };
    }

    /// <summary>
    /// Loads database credentials written by Inno Setup / OrderWeb.DatabaseSetup.exe.
    /// Searches app data and common application data (Windows installer path).
    /// </summary>
    public static bool TryApplyInstallerDatabaseConfig(bool forceReapply = false)
    {
        if (!forceReapply && Preferences.Default.Get(InstallerConfigAppliedKey, false))
        {
            return false;
        }

        foreach (var path in GetInstallerConfigCandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                var installerConfig = JsonSerializer.Deserialize<InstallerDatabaseConfig>(json, JsonOptions);
                if (installerConfig == null || string.IsNullOrWhiteSpace(installerConfig.DatabasePassword))
                {
                    continue;
                }

                var databaseUser = string.IsNullOrWhiteSpace(installerConfig.DatabaseUser)
                    ? PosDatabaseDefaults.ProductionDatabaseUser
                    : installerConfig.DatabaseUser.Trim();
                var databasePassword = installerConfig.DatabasePassword.Trim();
                var validation = ProductionDatabaseCredentialPolicy.Validate(databaseUser, databasePassword);
                if (!validation.IsValid)
                {
                    AppDiagnostics.Log($"Skipping invalid installer config at {path}: {validation.Message}");
                    continue;
                }

                var current = GetConfiguration();
                Save(new TerminalConfiguration
                {
                    IsConfigured = current.IsConfigured,
                    Mode = current.Mode,
                    TerminalName = current.TerminalName,
                    DatabaseHost = string.IsNullOrWhiteSpace(installerConfig.DatabaseHost)
                        ? current.DatabaseHost
                        : installerConfig.DatabaseHost.Trim(),
                    DatabasePort = installerConfig.DatabasePort <= 0 ? 3306 : installerConfig.DatabasePort,
                    DatabaseName = string.IsNullOrWhiteSpace(installerConfig.DatabaseName)
                        ? PosDatabaseDefaults.ProductionDatabaseName
                        : installerConfig.DatabaseName.Trim(),
                    DatabaseUser = string.IsNullOrWhiteSpace(installerConfig.DatabaseUser)
                        ? PosDatabaseDefaults.ProductionDatabaseUser
                        : installerConfig.DatabaseUser.Trim(),
                    DatabasePassword = databasePassword,
                    DatabaseSslMode = string.IsNullOrWhiteSpace(installerConfig.DatabaseSslMode)
                        ? nameof(MySqlSslMode.Preferred)
                        : installerConfig.DatabaseSslMode
                });

                Preferences.Default.Set(InstallerConfigAppliedKey, true);
                AppDiagnostics.Log($"Applied installer database config from {path}");
                return true;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogFatal("ApplyInstallerDatabaseConfig", ex);
            }
        }

        return false;
    }

    public static void Save(TerminalConfiguration configuration)
    {
        var databaseUser = string.IsNullOrWhiteSpace(configuration.DatabaseUser)
            ? PosDatabaseDefaults.ProductionDatabaseUser
            : configuration.DatabaseUser.Trim();
        var databasePassword = configuration.DatabasePassword ?? string.Empty;

        var validation = ProductionDatabaseCredentialPolicy.Validate(databaseUser, databasePassword);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var host = configuration.IsMother ? "localhost" : configuration.DatabaseHost.Trim();
        var terminalName = string.IsNullOrWhiteSpace(configuration.TerminalName)
            ? (configuration.IsMother ? "Main" : "Terminal")
            : configuration.TerminalName.Trim();

        Preferences.Default.Set(IsConfiguredKey, true);
        Preferences.Default.Set(ModeKey, configuration.Mode.ToString());
        Preferences.Default.Set(TerminalNameKey, terminalName);
        Preferences.Default.Set(DatabaseHostKey, string.IsNullOrWhiteSpace(host) ? "localhost" : host);
        Preferences.Default.Set(DatabasePortKey, configuration.DatabasePort <= 0 ? 3306 : configuration.DatabasePort);
        Preferences.Default.Set(DatabaseNameKey, string.IsNullOrWhiteSpace(configuration.DatabaseName)
            ? PosDatabaseDefaults.ProductionDatabaseName
            : configuration.DatabaseName.Trim());
        Preferences.Default.Set(DatabaseUserKey, string.IsNullOrWhiteSpace(configuration.DatabaseUser)
            ? PosDatabaseDefaults.ProductionDatabaseUser
            : configuration.DatabaseUser.Trim());
        SaveDatabasePassword(configuration.DatabasePassword ?? string.Empty);
        Preferences.Default.Set(DatabaseSslModeKey, string.IsNullOrWhiteSpace(configuration.DatabaseSslMode)
            ? nameof(MySqlSslMode.Preferred)
            : configuration.DatabaseSslMode);
    }

    public static void SetConfigured(bool isConfigured)
    {
        Preferences.Default.Set(IsConfiguredKey, isConfigured);
    }

    private static string ReadDatabasePassword()
    {
        if (WindowsCredentialProtectionService.IsSupported)
        {
            var protectedValue = Preferences.Default.Get(ProtectedDatabasePasswordKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(protectedValue))
            {
                try
                {
                    return WindowsCredentialProtectionService.Unprotect(protectedValue);
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogFatal("Read protected database credential", ex);
                    return string.Empty;
                }
            }

            // One-time migration from releases that stored the password in MAUI Preferences.
            var legacyValue = Preferences.Default.Get(DatabasePasswordKey, string.Empty);
            if (!string.IsNullOrEmpty(legacyValue))
            {
                try
                {
                    SaveDatabasePassword(legacyValue);
                    return legacyValue;
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogFatal("Migrate database credential", ex);
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        // MacCatalyst remains a development target. Production deployment is Windows.
        return Preferences.Default.Get(DatabasePasswordKey, string.Empty);
    }

    private static void SaveDatabasePassword(string password)
    {
        if (WindowsCredentialProtectionService.IsSupported)
        {
            Preferences.Default.Set(
                ProtectedDatabasePasswordKey,
                WindowsCredentialProtectionService.Protect(password));
            Preferences.Default.Remove(DatabasePasswordKey);
            return;
        }

        Preferences.Default.Set(DatabasePasswordKey, password);
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

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            UserID = config.DatabaseUser,
            Password = config.DatabasePassword,
            Port = (uint)(config.DatabasePort <= 0 ? 3306 : config.DatabasePort),
            ConnectionTimeout = (uint)Math.Max(1, connectionTimeoutSeconds),
            DefaultCommandTimeout = (uint)Math.Max(1, defaultCommandTimeoutSeconds),
            Pooling = pooled,
            AllowUserVariables = true,
            SslMode = Enum.TryParse<MySqlSslMode>(config.DatabaseSslMode, true, out var sslMode)
                ? sslMode
                : MySqlSslMode.Preferred
        };

        if (includeDatabase)
        {
            builder.Database = selectedDatabase;
        }

        if (pooled)
        {
            builder.MinimumPoolSize = 10;
            builder.MaximumPoolSize = 200;
            builder.ConnectionIdleTimeout = 60;
            builder.ConnectionReset = false;
            builder.Keepalive = 15;
        }

        return builder.ConnectionString;
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
        Preferences.Default.Remove(ProtectedDatabasePasswordKey);
        Preferences.Default.Remove(DatabaseSslModeKey);
        Preferences.Default.Remove(InstallerConfigAppliedKey);
    }

    public static string GetActiveDatabaseName() =>
        GetConfiguration().DatabaseName;

    private static IEnumerable<string> GetInstallerConfigCandidatePaths()
    {
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(commonAppData))
        {
            yield return Path.Combine(commonAppData, PosDatabaseDefaults.InstallerConfigFolderName, PosDatabaseDefaults.InstallerConfigFileName);
        }

        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetEnvironmentVariable("ProgramData");
            if (!string.IsNullOrWhiteSpace(programData))
            {
                yield return Path.Combine(programData, PosDatabaseDefaults.InstallerConfigFolderName, PosDatabaseDefaults.InstallerConfigFileName);
            }
        }

        yield return Path.Combine(FileSystem.AppDataDirectory, PosDatabaseDefaults.InstallerConfigFileName);
    }
}
