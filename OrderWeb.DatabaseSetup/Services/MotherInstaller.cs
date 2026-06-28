using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

/// <summary>
/// Creates orderweb_pos + orderweb_app using MariaDB admin credentials.
/// Replaces manual execution of create_orderweb_app_user.sql during install-mother.
/// </summary>
public static class MotherInstaller
{
    private static readonly string[] ApplicationUserHosts = ["localhost", "127.0.0.1"];

    public static string GeneratePassword(int length = 32)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(alphabet[bytes[i] % alphabet.Length]);
        }

        var password = builder.ToString();
        var validation = ProductionCredentialPolicy.ValidateAppCredentials(PosDefaults.ProductionDatabaseUser, password);
        return validation.IsValid ? password : GeneratePassword(length + 4);
    }

    public static async Task<ProvisionResult> ProvisionAsync(
        string rootUser,
        string rootPassword,
        string lanSubnet,
        string? databasePassword = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootUser) || string.IsNullOrWhiteSpace(rootPassword))
        {
            return ProvisionResult.Failed("MariaDB admin credentials are required for install-mother.");
        }

        if (string.Equals(rootUser.Trim(), PosDefaults.ProductionDatabaseUser, StringComparison.OrdinalIgnoreCase)
            && ProductionCredentialPolicy.IsForbiddenRootPasswordForApp(rootPassword))
        {
            return ProvisionResult.Failed(
                "Do not use the application database account with MariaDB admin password 'root'. " +
                "Provide MariaDB admin via --root-user / --root-password; install-mother creates orderweb_app automatically.");
        }

        var password = string.IsNullOrWhiteSpace(databasePassword) ? GeneratePassword() : databasePassword.Trim();
        var credentialCheck = ProductionCredentialPolicy.ValidateAppCredentials(PosDefaults.ProductionDatabaseUser, password);
        if (!credentialCheck.IsValid)
        {
            return ProvisionResult.Failed(credentialCheck.Message);
        }

        var config = new DatabaseConfig
        {
            DatabaseHost = "localhost",
            DatabasePort = 3306,
            DatabaseName = PosDefaults.ProductionDatabaseName,
            DatabaseUser = PosDefaults.ProductionDatabaseUser,
            DatabasePassword = password,
            InstalledBySetup = true
        };

        var rootConnectionString = ConfigStore.BuildRootConnectionString(
            config.DatabaseHost,
            config.DatabasePort,
            rootUser.Trim(),
            rootPassword);

        try
        {
            await SqlExecutor.TestConnectionAsync(rootConnectionString, cancellationToken);
        }
        catch (Exception ex)
        {
            return ProvisionResult.Failed($"MariaDB admin connection failed: {ex.Message}");
        }

        await using var connection = new MySqlConnection(rootConnectionString);
        await connection.OpenAsync(cancellationToken);

        var escapedDb = EscapeIdentifier(config.DatabaseName);
        var escapedUser = EscapeString(config.DatabaseUser);
        var escapedPassword = EscapeString(password);
        var escapedLanSubnet = EscapeString(NormalizeLanSubnet(lanSubnet));

        var statements = new List<string>
        {
            $"CREATE DATABASE IF NOT EXISTS `{escapedDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
        };

        foreach (var host in ApplicationUserHosts.Append(escapedLanSubnet).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            statements.Add($"CREATE USER IF NOT EXISTS '{escapedUser}'@'{host}' IDENTIFIED BY '{escapedPassword}';");
            statements.Add($"ALTER USER '{escapedUser}'@'{host}' IDENTIFIED BY '{escapedPassword}';");
            statements.Add(
                $"GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, " +
                $"CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER " +
                $"ON `{escapedDb}`.* TO '{escapedUser}'@'{host}';");
        }

        statements.Add("FLUSH PRIVILEGES;");

        foreach (var statement in statements)
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await SqlExecutor.TestConnectionAsync(ConfigStore.BuildConnectionString(config), cancellationToken);
        }
        catch (Exception ex)
        {
            return ProvisionResult.Failed($"Application database user verification failed: {ex.Message}");
        }

        return ProvisionResult.Success(config, passwordGenerated: string.IsNullOrWhiteSpace(databasePassword));
    }

    private static string NormalizeLanSubnet(string? lanSubnet)
    {
        return string.IsNullOrWhiteSpace(lanSubnet) ? "192.168.%" : lanSubnet.Trim();
    }

    private static string EscapeIdentifier(string value) => value.Replace("`", "``", StringComparison.Ordinal);

    private static string EscapeString(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class ProvisionResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public DatabaseConfig? Config { get; init; }
    public bool PasswordGenerated { get; init; }

    public static ProvisionResult Success(DatabaseConfig config, bool passwordGenerated) =>
        new()
        {
            IsSuccess = true,
            Message = passwordGenerated
                ? "Database, orderweb_app user, and random password provisioned."
                : "Database and orderweb_app user provisioned.",
            Config = config,
            PasswordGenerated = passwordGenerated
        };

    public static ProvisionResult Failed(string message) =>
        new() { IsSuccess = false, Message = message };
}
