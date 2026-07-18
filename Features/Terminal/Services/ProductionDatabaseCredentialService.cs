using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed record ProductionDatabaseCredentialResult(
    InstallerDatabaseConfig Config,
    string AppDataConfigPath,
    string? CommonDataConfigPath,
    string SqlSetupScriptPath);

/// <summary>
/// Development-only helper for generating credentials and SQL scripts.
/// Production installs must use OrderWeb.DatabaseSetup.exe install-mother.
/// </summary>
public static class ProductionDatabaseCredentialService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static ProductionDatabaseCredentialResult GenerateAndSave(
        string? databaseHost = null,
        int databasePort = 3306,
        string? databaseName = null,
        string? databaseUser = null,
        string? databasePassword = null)
    {
        var config = new InstallerDatabaseConfig
        {
            DatabaseHost = string.IsNullOrWhiteSpace(databaseHost) ? "localhost" : databaseHost.Trim(),
            DatabasePort = databasePort <= 0 ? 3306 : databasePort,
            DatabaseName = string.IsNullOrWhiteSpace(databaseName)
                ? PosDatabaseDefaults.ProductionDatabaseName
                : databaseName.Trim(),
            DatabaseUser = string.IsNullOrWhiteSpace(databaseUser)
                ? PosDatabaseDefaults.ProductionDatabaseUser
                : databaseUser.Trim(),
            DatabasePassword = string.IsNullOrWhiteSpace(databasePassword)
                ? GeneratePassword()
                : databasePassword.Trim(),
            InstalledBySetup = true
        };

        var validation = ProductionDatabaseCredentialPolicy.Validate(config.DatabaseUser, config.DatabasePassword);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, PosDatabaseDefaults.InstallerConfigFileName);
        WriteConfigFile(appDataPath, config);

        string? commonDataPath = null;
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(commonAppData))
        {
            commonDataPath = Path.Combine(
                commonAppData,
                PosDatabaseDefaults.InstallerConfigFolderName,
                PosDatabaseDefaults.InstallerConfigFileName);

            try
            {
                WriteConfigFile(commonDataPath, config);
            }
            catch (UnauthorizedAccessException)
            {
                commonDataPath = null;
            }
        }

        var sqlPath = Path.Combine(FileSystem.AppDataDirectory, "create_orderweb_app_user.generated.sql");
        File.WriteAllText(sqlPath, BuildCreateUserSql(config), Encoding.UTF8);

        TerminalConfigurationService.TryApplyInstallerDatabaseConfig(forceReapply: true);

        return new ProductionDatabaseCredentialResult(config, appDataPath, commonDataPath, sqlPath);
    }

    public static string GeneratePassword(int byteCount = 32)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_";
        var length = Math.Max(24, byteCount);
        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        for (var index = 0; index < bytes.Length; index++)
        {
            builder.Append(alphabet[bytes[index] % alphabet.Length]);
        }

        var password = builder.ToString();
        return ProductionDatabaseCredentialPolicy.Validate(PosDatabaseDefaults.ProductionDatabaseUser, password).IsValid
            ? password
            : GeneratePassword(byteCount + 4);
    }

    private static void WriteConfigFile(string path, InstallerDatabaseConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions), Encoding.UTF8);
    }

    private static string BuildCreateUserSql(InstallerDatabaseConfig config)
    {
        var databaseName = EscapeIdentifier(config.DatabaseName);
        var user = EscapeSqlLiteral(config.DatabaseUser);
        var password = EscapeSqlLiteral(config.DatabasePassword);

        return $"""
            -- Generated OrderWeb POS production database credentials.
            -- Run on the Mother PC as MariaDB admin:
            --   mysql -u root -p < create_orderweb_app_user.generated.sql

            CREATE DATABASE IF NOT EXISTS `{databaseName}`
              CHARACTER SET utf8mb4
              COLLATE utf8mb4_unicode_ci;

            CREATE USER IF NOT EXISTS '{user}'@'localhost' IDENTIFIED BY '{password}';
            CREATE USER IF NOT EXISTS '{user}'@'127.0.0.1' IDENTIFIED BY '{password}';

            ALTER USER '{user}'@'localhost' IDENTIFIED BY '{password}';
            ALTER USER '{user}'@'127.0.0.1' IDENTIFIED BY '{password}';

            GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER
              ON `{databaseName}`.*
              TO '{user}'@'localhost';

            GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER
              ON `{databaseName}`.*
              TO '{user}'@'127.0.0.1';

            FLUSH PRIVILEGES;
            """;
    }

    private static string EscapeIdentifier(string value) =>
        value.Replace("`", "``", StringComparison.Ordinal);

    private static string EscapeSqlLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);
}
