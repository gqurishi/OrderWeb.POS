using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] EncryptedBackupMagic = Encoding.ASCII.GetBytes("OWPBK2\r\n");
    private const int BackupPbkdf2Iterations = 210_000;

    private readonly string _appVersion;

    public BackupService(string appVersion)
    {
        _appVersion = appVersion;
    }

    public async Task<OperationResult> CreateBackupAsync(
        DatabaseConfig config,
        string backupType,
        CancellationToken cancellationToken = default,
        string? outputPath = null,
        int schemaVersion = 0)
    {
        try
        {
            await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);

            var backupFolder = ConfigStore.ResolveBackupFolder();
            Directory.CreateDirectory(backupFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{SanitizeFileName(config.DatabaseName)}_{backupType}_{timestamp}_v{SanitizeFileName(_appVersion)}{PosDefaults.BackupExtension}";
            var filePath = outputPath ?? Path.Combine(backupFolder, fileName);
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("-- OrderWeb POS database backup");
            builder.AppendLine($"-- Database: {config.DatabaseName}");
            builder.AppendLine($"-- Generated: {DateTime.UtcNow:O}");
            builder.AppendLine("SET FOREIGN_KEY_CHECKS=0;");
            builder.AppendLine();

            var tables = await GetTablesAsync(connection, config.DatabaseName, cancellationToken);
            foreach (var table in tables)
            {
                await AppendTableAsync(connection, builder, table, cancellationToken);
            }

            builder.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
            var sql = builder.ToString();
            var sqlBytes = Encoding.UTF8.GetBytes(sql);
            var metadata = new BackupMetadata
            {
                BackupType = backupType,
                DatabaseName = config.DatabaseName,
                AppVersion = _appVersion,
                SqlSizeBytes = sqlBytes.LongLength,
                SqlSha256 = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant(),
                SchemaVersion = schemaVersion
            };

            await WriteBackupPackageAsync(filePath, sqlBytes, metadata, config.DatabasePassword, cancellationToken);
            return OperationResult.Success($"Backup saved to {filePath}", filePath);
        }
        catch (Exception ex)
        {
            return OperationResult.Failed($"Backup failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> RestoreAsync(
        DatabaseConfig config,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                return OperationResult.Failed($"Backup file not found: {backupPath}");
            }

            var verify = await VerifyBackupAsync(backupPath, config.DatabasePassword, cancellationToken);
            if (!verify.IsSuccess)
            {
                return verify;
            }

            var safetyBackup = await CreateBackupAsync(config, "before_restore", cancellationToken);
            if (!safetyBackup.IsSuccess)
            {
                return OperationResult.Failed($"Restore cancelled because safety backup failed: {safetyBackup.Message}");
            }

            var (_, sql) = await ReadBackupPackageAsync(backupPath, config.DatabasePassword, cancellationToken);
            await using var connection = new MySqlConnection(ConfigStore.BuildConnectionString(config));
            await connection.OpenAsync(cancellationToken);
            await DropAllTablesAsync(connection, config.DatabaseName, cancellationToken);
            await SqlExecutor.ExecuteScriptAsync(connection, sql, cancellationToken);

            return OperationResult.Success($"Database restored successfully. Safety backup: {safetyBackup.FilePath}", safetyBackup.FilePath);
        }
        catch (Exception ex)
        {
            return OperationResult.Failed($"Restore failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> VerifyBackupAsync(
        string backupPath,
        string databasePassword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (metadata, sql) = await ReadBackupPackageAsync(backupPath, databasePassword, cancellationToken);
            var sqlBytes = Encoding.UTF8.GetBytes(sql);
            var checksum = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant();
            if (!string.Equals(checksum, metadata.SqlSha256, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failed("Backup checksum does not match.");
            }

            if (!string.Equals(metadata.Product, PosDefaults.ProductName, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Failed("This is not an OrderWeb POS backup file.");
            }

            return OperationResult.Success("Backup file verified.");
        }
        catch (Exception ex)
        {
            return OperationResult.Failed($"Backup verification failed: {ex.Message}");
        }
    }

    private static async Task<List<string>> GetTablesAsync(MySqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @databaseName
              AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";

        var tables = new List<string>();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@databaseName", databaseName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString("TABLE_NAME"));
        }

        return tables;
    }

    private static async Task AppendTableAsync(MySqlConnection connection, StringBuilder builder, string tableName, CancellationToken cancellationToken)
    {
        var escaped = EscapeIdentifier(tableName);
        builder.AppendLine($"-- Table `{tableName}`");
        builder.AppendLine($"DROP TABLE IF EXISTS `{escaped}`;");

        await using (var createCommand = new MySqlCommand($"SHOW CREATE TABLE `{escaped}`", connection))
        await using (var createReader = await createCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await createReader.ReadAsync(cancellationToken))
            {
                builder.AppendLine(createReader.GetString(1) + ";");
            }
        }

        await using var selectCommand = new MySqlCommand($"SELECT * FROM `{escaped}`", connection);
        await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        var fieldCount = reader.FieldCount;
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new string[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                values[i] = FormatSqlValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
            }

            builder.AppendLine($"INSERT INTO `{escaped}` VALUES ({string.Join(", ", values)});");
        }

        builder.AppendLine();
    }

    private static async Task DropAllTablesAsync(MySqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        var tables = await GetTablesAsync(connection, databaseName, cancellationToken);
        await using var command = new MySqlCommand("SET FOREIGN_KEY_CHECKS=0;", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var table in tables)
        {
            await using var drop = new MySqlCommand($"DROP TABLE IF EXISTS `{EscapeIdentifier(table)}`;", connection);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var enable = new MySqlCommand("SET FOREIGN_KEY_CHECKS=1;", connection);
        await enable.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteBackupPackageAsync(
        string filePath,
        byte[] sqlBytes,
        BackupMetadata metadata,
        string databasePassword,
        CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        byte[] packageBytes;
        await using (var packageStream = new MemoryStream())
        {
            using (var zip = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var metadataEntry = zip.CreateEntry("metadata.json");
                await using (var stream = metadataEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
                }

                var sqlEntry = zip.CreateEntry("backup.sql");
                await using (var stream = sqlEntry.Open())
                {
                    await stream.WriteAsync(sqlBytes, cancellationToken);
                }
            }

            packageBytes = packageStream.ToArray();
        }

        try
        {
            var encrypted = EncryptPackage(packageBytes, databasePassword);
            try
            {
                await File.WriteAllBytesAsync(filePath, encrypted, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packageBytes);
        }
    }

    private static async Task<(BackupMetadata Metadata, string Sql)> ReadBackupPackageAsync(
        string filePath,
        string databasePassword,
        CancellationToken cancellationToken)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var packageBytes = DecryptPackageIfNeeded(fileBytes, databasePassword);
        if (!ReferenceEquals(fileBytes, packageBytes))
        {
            CryptographicOperations.ZeroMemory(fileBytes);
        }

        using var packageStream = new MemoryStream(packageBytes, writable: false);
        using var zip = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var metadataEntry = zip.GetEntry("metadata.json") ?? throw new InvalidOperationException("Backup metadata.json is missing.");
        var sqlEntry = zip.GetEntry("backup.sql") ?? throw new InvalidOperationException("Backup backup.sql is missing.");

        BackupMetadata metadata;
        await using (var stream = metadataEntry.Open())
        {
            metadata = await JsonSerializer.DeserializeAsync<BackupMetadata>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Could not parse backup metadata.");
        }

        string sql;
        await using (var stream = sqlEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            sql = await reader.ReadToEndAsync(cancellationToken);
        }

        CryptographicOperations.ZeroMemory(packageBytes);
        return (metadata, sql);
    }

    private static byte[] EncryptPackage(byte[] plaintext, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Database credentials are unavailable; the backup cannot be encrypted.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, BackupPbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, EncryptedBackupMagic);
            return EncryptedBackupMagic.Concat(salt).Concat(nonce).Concat(tag).Concat(ciphertext).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static byte[] DecryptPackageIfNeeded(byte[] fileBytes, string password)
    {
        if (!fileBytes.AsSpan().StartsWith(EncryptedBackupMagic))
        {
            return fileBytes;
        }

        if (string.IsNullOrWhiteSpace(password) || fileBytes.Length < EncryptedBackupMagic.Length + 45)
        {
            throw new InvalidDataException("Encrypted backup credentials are unavailable or the file is incomplete.");
        }

        var offset = EncryptedBackupMagic.Length;
        var salt = fileBytes.AsSpan(offset, 16).ToArray();
        offset += 16;
        var nonce = fileBytes.AsSpan(offset, 12).ToArray();
        offset += 12;
        var tag = fileBytes.AsSpan(offset, 16).ToArray();
        offset += 16;
        var ciphertext = fileBytes.AsSpan(offset).ToArray();
        var plaintext = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, BackupPbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, EncryptedBackupMagic);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Backup authentication failed. The file is damaged or uses different database credentials.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static string FormatSqlValue(object? value)
    {
        if (value == null || value is DBNull)
        {
            return "NULL";
        }

        return value switch
        {
            bool boolean => boolean ? "1" : "0",
            byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double =>
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset.UtcDateTime:yyyy-MM-dd HH:mm:ss}'",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            _ => $"'{EscapeString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}'"
        };
    }

    private static string EscapeIdentifier(string value) => value.Replace("`", "``", StringComparison.Ordinal);
    private static string EscapeString(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string SanitizeFileName(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

public sealed class OperationResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FilePath { get; init; }

    public static OperationResult Success(string message, string? filePath = null) =>
        new() { IsSuccess = true, Message = message, FilePath = filePath };

    public static OperationResult Failed(string message) =>
        new() { IsSuccess = false, Message = message };
}
