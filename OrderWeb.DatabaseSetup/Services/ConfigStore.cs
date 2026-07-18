using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using MySqlConnector;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public static class ConfigStore
{
    public const string InstallManifestFileName = "install-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string ResolveProductionConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, PosDefaults.InstallerConfigFolderName, PosDefaults.InstallerConfigFileName);
        }

        return ResolveDefaultConfigPath();
    }

    public static string ResolveProductionConfigDirectory()
    {
        return Path.GetDirectoryName(ResolveProductionConfigPath())
            ?? throw new InvalidOperationException("Could not resolve production config directory.");
    }

    public static string ResolveInstallManifestPath() =>
        Path.Combine(ResolveProductionConfigDirectory(), InstallManifestFileName);

    public static string ResolveDefaultConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return ResolveProductionConfigPath();
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".orderwebpos", PosDefaults.InstallerConfigFileName);
    }

    public static string ResolveMigrationsPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(AppContext.BaseDirectory, "Migrations");
    }

    public static string ResolveBackupFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, PosDefaults.InstallerConfigFolderName, "Backups");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".orderwebpos", "Backups");
    }

    public static DatabaseConfig Load(string? configPath = null)
    {
        var path = string.IsNullOrWhiteSpace(configPath) ? ResolveProductionConfigPath() : Path.GetFullPath(configPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Database config not found: {path}");
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<DatabaseConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse database config: {path}");

        if (string.IsNullOrWhiteSpace(config.DatabasePassword))
        {
            throw new InvalidOperationException("Database password is missing in config.");
        }

        var validation = ProductionCredentialPolicy.ValidateAppCredentials(config.DatabaseUser, config.DatabasePassword);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid production database config: {validation.Message}");
        }

        return config;
    }

    public static ProductionConfigSaveResult SaveProductionConfig(DatabaseConfig config, string? configPath = null)
    {
        var validation = ProductionCredentialPolicy.ValidateAppCredentials(config.DatabaseUser, config.DatabasePassword);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        config.InstalledBySetup = true;
        var path = string.IsNullOrWhiteSpace(configPath) ? ResolveProductionConfigPath() : Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            RestrictProductionDirectoryAccess(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
        RestrictConfigFileAccess(path);

        return new ProductionConfigSaveResult(path, directory ?? string.Empty);
    }

    public static void Save(DatabaseConfig config, string? configPath = null)
    {
        SaveProductionConfig(config, configPath);
    }

    public static void WriteInstallManifest(InstallManifest manifest)
    {
        var path = ResolveInstallManifestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RestrictProductionDirectoryAccess(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(path, json);
        RestrictConfigFileAccess(path);
    }

    public static string BuildConnectionString(
        DatabaseConfig config,
        bool includeDatabase = true,
        int connectionTimeoutSeconds = 15)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(config.DatabaseHost) ? "localhost" : config.DatabaseHost,
            Port = (uint)config.DatabasePort,
            UserID = config.DatabaseUser,
            Password = config.DatabasePassword,
            ConnectionTimeout = (uint)connectionTimeoutSeconds,
            AllowUserVariables = true,
            SslMode = ParseSslMode(config.DatabaseSslMode),
            Pooling = false
        };

        if (includeDatabase)
        {
            builder.Database = config.DatabaseName;
        }

        return builder.ConnectionString;
    }

    public static string BuildRootConnectionString(
        string host,
        int port,
        string rootUser,
        string rootPassword,
        int connectionTimeoutSeconds = 15)
    {
        return new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(host) ? "localhost" : host,
            Port = (uint)port,
            UserID = rootUser,
            Password = rootPassword,
            ConnectionTimeout = (uint)connectionTimeoutSeconds,
            SslMode = MySqlSslMode.Preferred,
            Pooling = false
        }.ConnectionString;
    }

    private static MySqlSslMode ParseSslMode(string? value) =>
        Enum.TryParse<MySqlSslMode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : MySqlSslMode.Preferred;

    private static void RestrictProductionDirectoryAccess(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return;
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directory).SetAccessControl(security);
    }

    private static void RestrictConfigFileAccess(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            security.AddAccessRule(new FileSystemAccessRule(
                administrators,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not secure production database config '{path}'.", ex);
        }
    }
}

public sealed record ProductionConfigSaveResult(string ConfigPath, string ConfigDirectory);
