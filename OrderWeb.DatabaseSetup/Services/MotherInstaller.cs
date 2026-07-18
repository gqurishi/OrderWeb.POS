using System.Security.Cryptography;
using System.Net;
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
    private const string ApplicationPrivileges =
        "SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES, " +
        "CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, CREATE VIEW, SHOW VIEW, TRIGGER";

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
        IReadOnlyCollection<string> childHosts,
        string? databasePassword = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootUser) || string.IsNullOrWhiteSpace(rootPassword))
        {
            return ProvisionResult.Failed("MariaDB admin credentials are required for install-mother.");
        }

        if (string.Equals(rootUser.Trim(), PosDefaults.ProductionDatabaseUser, StringComparison.OrdinalIgnoreCase))
        {
            return ProvisionResult.Failed(
                "Do not use the application database account for MariaDB administration. " +
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
        IReadOnlyList<string> validatedChildHosts;
        try
        {
            validatedChildHosts = ValidateChildHosts(childHosts);
        }
        catch (ArgumentException ex)
        {
            return ProvisionResult.Failed(ex.Message);
        }

        var statements = new List<string>
        {
            $"CREATE DATABASE IF NOT EXISTS `{escapedDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
        };

        foreach (var host in ApplicationUserHosts.Concat(validatedChildHosts).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var escapedHost = EscapeString(host);
            statements.Add($"CREATE USER IF NOT EXISTS '{escapedUser}'@'{escapedHost}' IDENTIFIED BY '{escapedPassword}';");
            statements.Add($"ALTER USER '{escapedUser}'@'{escapedHost}' IDENTIFIED BY '{escapedPassword}';");
            statements.Add(
                $"GRANT {ApplicationPrivileges} ON `{escapedDb}`.* TO '{escapedUser}'@'{escapedHost}';");
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

    public static async Task ConfigureChildAccessAsync(
        string rootUser,
        string rootPassword,
        DatabaseConfig config,
        IReadOnlyCollection<string> childHosts,
        bool requireTls,
        CancellationToken cancellationToken = default)
    {
        var validatedChildHosts = ValidateChildHosts(childHosts);
        var credentialCheck = ProductionCredentialPolicy.ValidateAppCredentials(config.DatabaseUser, config.DatabasePassword);
        if (!credentialCheck.IsValid)
        {
            throw new InvalidOperationException(credentialCheck.Message);
        }

        await using var connection = new MySqlConnection(ConfigStore.BuildRootConnectionString(
            "localhost",
            config.DatabasePort,
            rootUser.Trim(),
            rootPassword));
        await connection.OpenAsync(cancellationToken);

        var existingRemoteHosts = new List<string>();
        await using (var readHosts = new MySqlCommand("SELECT Host FROM mysql.user WHERE User = @user", connection))
        {
            readHosts.Parameters.AddWithValue("@user", config.DatabaseUser);
            await using var reader = await readHosts.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var host = reader.GetString(0);
                if (!ApplicationUserHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                {
                    existingRemoteHosts.Add(host);
                }
            }
        }

        var escapedUser = EscapeString(config.DatabaseUser);
        var escapedPassword = EscapeString(config.DatabasePassword);
        var escapedDb = EscapeIdentifier(config.DatabaseName);

        foreach (var obsoleteHost in existingRemoteHosts.Except(validatedChildHosts, StringComparer.OrdinalIgnoreCase))
        {
            await ExecuteAsync(
                connection,
                $"DROP USER IF EXISTS '{escapedUser}'@'{EscapeString(obsoleteHost)}';",
                cancellationToken);
        }

        foreach (var host in validatedChildHosts)
        {
            var escapedHost = EscapeString(host);
            var tlsClause = requireTls ? " REQUIRE SSL" : " REQUIRE NONE";
            await ExecuteAsync(
                connection,
                $"CREATE USER IF NOT EXISTS '{escapedUser}'@'{escapedHost}' IDENTIFIED BY '{escapedPassword}'{tlsClause};",
                cancellationToken);
            await ExecuteAsync(
                connection,
                $"ALTER USER '{escapedUser}'@'{escapedHost}' IDENTIFIED BY '{escapedPassword}'{tlsClause};",
                cancellationToken);
            await ExecuteAsync(
                connection,
                $"GRANT {ApplicationPrivileges} ON `{escapedDb}`.* TO '{escapedUser}'@'{escapedHost}';",
                cancellationToken);
        }

        await ExecuteAsync(connection, "FLUSH PRIVILEGES;", cancellationToken);
    }

    public static IReadOnlyList<string> ValidateChildHosts(IEnumerable<string>? childHosts)
    {
        var result = new List<string>();
        foreach (var candidate in childHosts ?? [])
        {
            var host = candidate.Trim();
            if (!IPAddress.TryParse(host, out var address)
                || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || !IsPrivateIpv4(address))
            {
                throw new ArgumentException($"Child address '{candidate}' must be an exact private IPv4 address; wildcard/subnet grants are forbidden.");
            }

            if (!result.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(host);
            }
        }

        return result;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
