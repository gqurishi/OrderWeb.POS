using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace POS_in_NET.Services;

public sealed record DatabaseBackupResult(bool Success, string Message, string? FilePath = null);
public sealed record DatabaseBackupFileInfo(string FilePath, string FileName, DateTime CreatedAt, long SizeBytes, string BackupType, string AppVersion, string DatabaseName);

public sealed class DatabaseBackupMetadata
{
    public string Product { get; set; } = "OrderWebPOS";
    public string BackupFormat { get; set; } = "orderwebbackup-v1";
    public string BackupType { get; set; } = "manual";
    public string AppVersion { get; set; } = AppInfo.Current.VersionString;
    public string DatabaseName { get; set; } = string.Empty;
    public string TerminalName { get; set; } = string.Empty;
    public string TerminalMode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SqlSha256 { get; set; } = string.Empty;
    public long SqlSizeBytes { get; set; }
}

public sealed class DatabaseBackupService : IDisposable
{
    private const string LastBackupDateKey = "database_backup_last_date";
    private const string BackupExtension = ".orderwebbackup";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] RequiredTables = ["users", "orders", "order_items", "settings", "cloud_config"];
    private readonly DatabaseService _databaseService;
    private readonly object _syncRoot = new();
    private Timer? _timer;
    private bool _isRunning;
    private bool _isBackingUp;

    public DatabaseBackupService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public bool IsRunning => _isRunning;
    public DateTime LastBackupAt { get; private set; } = DateTime.MinValue;
    public string LastBackupPath { get; private set; } = string.Empty;
    public string LastBackupStatus { get; private set; } = "Not started";

    public void Start()
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _timer = new Timer(
                async _ => await RunScheduledBackupIfNeededAsync(),
                null,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromHours(1));
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            _timer?.Dispose();
            _timer = null;
            _isRunning = false;
        }
    }

    public async Task<DatabaseBackupResult> RunScheduledBackupIfNeededAsync()
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new DatabaseBackupResult(false, "Database backups run on the mother terminal only.");
        }

        var todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lastBackupDate = Preferences.Default.Get(LastBackupDateKey, string.Empty);
        if (string.Equals(lastBackupDate, todayKey, StringComparison.Ordinal))
        {
            return new DatabaseBackupResult(true, "Daily database backup already completed today.", LastBackupPath);
        }

        var result = await CreateBackupAsync("scheduled");
        if (result.Success)
        {
            Preferences.Default.Set(LastBackupDateKey, todayKey);
        }

        return result;
    }

    public async Task<DatabaseBackupResult> ExportDatabaseAsync()
    {
        return await CreateBackupAsync("manual");
    }

    public async Task<DatabaseBackupResult> CreateBackupAsync(string backupType)
    {
        if (_isBackingUp)
        {
            return new DatabaseBackupResult(false, "Database backup is already running.");
        }

        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new DatabaseBackupResult(false, "Database backups run on the mother terminal only.");
        }

        _isBackingUp = true;
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var config = TerminalConfigurationService.GetConfiguration();
            var backupFolder = GetBackupFolder();
            Directory.CreateDirectory(backupFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{SanitizeFileName(config.DatabaseName)}_{backupType}_{timestamp}_v{SanitizeFileName(AppInfo.Current.VersionString)}{BackupExtension}";
            var filePath = Path.Combine(backupFolder, fileName);
            var builder = new StringBuilder();

            builder.AppendLine("-- POS-in-NET database backup");
            builder.AppendLine($"-- Database: {config.DatabaseName}");
            builder.AppendLine($"-- Terminal: {config.TerminalName}");
            builder.AppendLine($"-- Generated: {DateTime.Now:O}");
            builder.AppendLine("SET FOREIGN_KEY_CHECKS=0;");
            builder.AppendLine();

            var tables = await GetTablesAsync(connection, config.DatabaseName);
            foreach (var table in tables)
            {
                await AppendTableAsync(connection, builder, table);
            }

            builder.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
            var sql = builder.ToString();
            var sqlBytes = Encoding.UTF8.GetBytes(sql);
            var metadata = new DatabaseBackupMetadata
            {
                BackupType = backupType,
                DatabaseName = config.DatabaseName,
                TerminalName = config.TerminalName,
                TerminalMode = config.Mode.ToString(),
                SqlSizeBytes = sqlBytes.LongLength,
                SqlSha256 = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant()
            };

            await WriteBackupPackageAsync(filePath, sqlBytes, metadata);
            PruneOldBackups(backupFolder);

            LastBackupAt = DateTime.Now;
            LastBackupPath = filePath;
            LastBackupStatus = "Backup complete";

            return new DatabaseBackupResult(true, $"Database backup saved to {filePath}", filePath);
        }
        catch (Exception ex)
        {
            LastBackupAt = DateTime.Now;
            LastBackupStatus = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Database backup failed: {ex.Message}");
            return new DatabaseBackupResult(false, $"Database backup failed: {ex.Message}");
        }
        finally
        {
            _isBackingUp = false;
        }
    }

    public async Task<List<DatabaseBackupFileInfo>> GetBackupHistoryAsync()
    {
        var backupFolder = GetBackupFolder();
        if (!Directory.Exists(backupFolder))
        {
            return [];
        }

        var files = Directory.GetFiles(backupFolder, $"*{BackupExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToList();

        var result = new List<DatabaseBackupFileInfo>();
        foreach (var file in files)
        {
            var metadata = await TryReadMetadataAsync(file.FullName);
            result.Add(new DatabaseBackupFileInfo(
                file.FullName,
                file.Name,
                metadata?.CreatedAtUtc.ToLocalTime() ?? file.CreationTime,
                file.Length,
                metadata?.BackupType ?? "unknown",
                metadata?.AppVersion ?? "",
                metadata?.DatabaseName ?? ""));
        }

        return result;
    }

    public async Task<DatabaseBackupResult> ExportBackupToAsync(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return new DatabaseBackupResult(false, "Backup file was not found.");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination);
            return new DatabaseBackupResult(true, $"Backup exported to {destinationPath}", destinationPath);
        }
        catch (Exception ex)
        {
            return new DatabaseBackupResult(false, $"Backup export failed: {ex.Message}");
        }
    }

    public async Task<DatabaseBackupResult> VerifyBackupAsync(string backupPath)
    {
        try
        {
            var (metadata, sql) = await ReadBackupPackageAsync(backupPath);
            var sqlBytes = Encoding.UTF8.GetBytes(sql);
            var checksum = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant();
            if (!string.Equals(checksum, metadata.SqlSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseBackupResult(false, "Backup checksum does not match. The file may be corrupted.");
            }

            if (!string.Equals(metadata.Product, "OrderWebPOS", StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseBackupResult(false, "This is not an OrderWeb POS backup file.");
            }

            var missingTables = RequiredTables
                .Where(table => !sql.Contains($"CREATE TABLE `{table}`", StringComparison.OrdinalIgnoreCase) &&
                                !sql.Contains($"CREATE TABLE IF NOT EXISTS `{table}`", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (missingTables.Count > 0)
            {
                return new DatabaseBackupResult(false, $"Backup is missing required tables: {string.Join(", ", missingTables)}.");
            }

            return new DatabaseBackupResult(true, "Backup file verified successfully.", backupPath);
        }
        catch (Exception ex)
        {
            return new DatabaseBackupResult(false, $"Backup verification failed: {ex.Message}");
        }
    }

    public async Task<DatabaseBackupResult> RestoreBackupAsync(string backupPath)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return new DatabaseBackupResult(false, "Database restore must be run on the mother terminal.");
        }

        if (_isBackingUp)
        {
            return new DatabaseBackupResult(false, "A database backup or restore is already running.");
        }

        _isBackingUp = true;
        string? safetyBackupPath = null;
        try
        {
            var verifyResult = await VerifyBackupAsync(backupPath);
            if (!verifyResult.Success)
            {
                return verifyResult;
            }

            var safetyBackup = await CreateBackupInternalAsync("before_restore");
            if (!safetyBackup.Success)
            {
                return new DatabaseBackupResult(false, $"Restore cancelled because safety backup failed: {safetyBackup.Message}");
            }

            safetyBackupPath = safetyBackup.FilePath;
            var (_, sql) = await ReadBackupPackageAsync(backupPath);

            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString(pooled: false));
            await connection.OpenAsync();
            await DropAllTablesAsync(connection);
            await ExecuteSqlScriptAsync(connection, sql);

            var postRestoreVerify = await VerifyCurrentDatabaseAsync();
            if (!postRestoreVerify.Success)
            {
                return new DatabaseBackupResult(false, $"Restore imported but verification failed: {postRestoreVerify.Message}. Safety backup: {safetyBackupPath}", safetyBackupPath);
            }

            LastBackupAt = DateTime.Now;
            LastBackupStatus = "Restore complete";
            return new DatabaseBackupResult(true, $"Database restored successfully. Safety backup saved at {safetyBackupPath}", safetyBackupPath);
        }
        catch (Exception ex)
        {
            LastBackupAt = DateTime.Now;
            LastBackupStatus = ex.Message;
            return new DatabaseBackupResult(false, $"Database restore failed: {ex.Message}. Safety backup: {safetyBackupPath}", safetyBackupPath);
        }
        finally
        {
            _isBackingUp = false;
        }
    }

    public async Task<DatabaseBackupResult> VerifyCurrentDatabaseAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();

            var config = TerminalConfigurationService.GetConfiguration();
            var missingTables = new List<string>();
            foreach (var table in RequiredTables)
            {
                const string sql = @"
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = @databaseName
                      AND TABLE_NAME = @tableName";

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@databaseName", config.DatabaseName);
                command.Parameters.AddWithValue("@tableName", table);
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (count == 0)
                {
                    missingTables.Add(table);
                }
            }

            if (missingTables.Count > 0)
            {
                return new DatabaseBackupResult(false, $"Database is missing required tables: {string.Join(", ", missingTables)}.");
            }

            return new DatabaseBackupResult(true, "Database verification passed.");
        }
        catch (Exception ex)
        {
            return new DatabaseBackupResult(false, $"Database verification failed: {ex.Message}");
        }
    }

    private async Task<DatabaseBackupResult> CreateBackupInternalAsync(string backupType)
    {
        var previous = _isBackingUp;
        _isBackingUp = false;
        try
        {
            return await CreateBackupAsync(backupType);
        }
        finally
        {
            _isBackingUp = previous;
        }
    }

    private static async Task<List<string>> GetTablesAsync(MySqlConnection connection, string databaseName)
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
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString("TABLE_NAME"));
        }

        return tables;
    }

    private static async Task AppendTableAsync(MySqlConnection connection, StringBuilder builder, string tableName)
    {
        builder.AppendLine($"-- Table `{tableName}`");
        builder.AppendLine($"DROP TABLE IF EXISTS `{EscapeIdentifier(tableName)}`;");

        await using (var createCommand = new MySqlCommand($"SHOW CREATE TABLE `{EscapeIdentifier(tableName)}`", connection))
        await using (var createReader = await createCommand.ExecuteReaderAsync())
        {
            if (await createReader.ReadAsync())
            {
                builder.AppendLine(createReader.GetString(1) + ";");
            }
        }

        await using var selectCommand = new MySqlCommand($"SELECT * FROM `{EscapeIdentifier(tableName)}`", connection);
        await using var reader = await selectCommand.ExecuteReaderAsync();
        var columnNames = Enumerable.Range(0, reader.FieldCount)
            .Select(index => $"`{EscapeIdentifier(reader.GetName(index))}`")
            .ToArray();
        var columnList = string.Join(", ", columnNames);

        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = FormatSqlValue(reader.GetValue(index));
            }

            builder.AppendLine($"INSERT INTO `{EscapeIdentifier(tableName)}` ({columnList}) VALUES ({string.Join(", ", values)});");
        }

        builder.AppendLine();
    }

    private static string FormatSqlValue(object value)
    {
        if (value == DBNull.Value)
        {
            return "NULL";
        }

        return value switch
        {
            bool boolValue => boolValue ? "1" : "0",
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.ffffff}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:yyyy-MM-dd HH:mm:ss.ffffff zzz}'",
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL",
            _ => $"'{EscapeSqlString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}'"
        };
    }

    private static string EscapeSqlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeIdentifier(string value)
    {
        return value.Replace("`", "``", StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "pos_database" : sanitized;
    }

    private static async Task WriteBackupPackageAsync(string filePath, byte[] sqlBytes, DatabaseBackupMetadata metadata)
    {
        await using var fileStream = File.Create(filePath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        var metadataEntry = archive.CreateEntry("metadata.json", CompressionLevel.Optimal);
        await using (var stream = metadataEntry.Open())
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions);

        var sqlEntry = archive.CreateEntry("backup.sql", CompressionLevel.Optimal);
        await using (var stream = sqlEntry.Open())
        {
            await stream.WriteAsync(sqlBytes);
        }

        var checksumEntry = archive.CreateEntry("checksum.txt", CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(checksumEntry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(metadata.SqlSha256);
        }
    }

    private static async Task<(DatabaseBackupMetadata Metadata, string Sql)> ReadBackupPackageAsync(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var metadataEntry = archive.GetEntry("metadata.json") ?? throw new InvalidDataException("metadata.json is missing.");
        var sqlEntry = archive.GetEntry("backup.sql") ?? throw new InvalidDataException("backup.sql is missing.");

        await using var metadataStream = metadataEntry.Open();
        var metadata = await JsonSerializer.DeserializeAsync<DatabaseBackupMetadata>(metadataStream)
            ?? throw new InvalidDataException("Backup metadata is invalid.");

        await using var sqlStream = sqlEntry.Open();
        using var reader = new StreamReader(sqlStream, Encoding.UTF8);
        var sql = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidDataException("Backup SQL is empty.");
        }

        return (metadata, sql);
    }

    private static async Task<DatabaseBackupMetadata?> TryReadMetadataAsync(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var metadataEntry = archive.GetEntry("metadata.json");
            if (metadataEntry == null)
            {
                return null;
            }

            await using var stream = metadataEntry.Open();
            return await JsonSerializer.DeserializeAsync<DatabaseBackupMetadata>(stream);
        }
        catch
        {
            return null;
        }
    }

    private static async Task DropAllTablesAsync(MySqlConnection connection)
    {
        await using (var disableCommand = new MySqlCommand("SET FOREIGN_KEY_CHECKS=0", connection))
        {
            await disableCommand.ExecuteNonQueryAsync();
        }

        var tables = await GetTablesAsync(connection, TerminalConfigurationService.GetConfiguration().DatabaseName);
        foreach (var table in tables)
        {
            await using var dropCommand = new MySqlCommand($"DROP TABLE IF EXISTS `{EscapeIdentifier(table)}`", connection);
            await dropCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteSqlScriptAsync(MySqlConnection connection, string sql)
    {
        foreach (var statement in SplitSqlStatements(sql))
        {
            var trimmed = statement.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            await using var command = new MySqlCommand(trimmed, connection)
            {
                CommandTimeout = 120
            };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inBacktick = false;
        var escaped = false;

        foreach (var ch in sql)
        {
            builder.Append(ch);

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && (inSingleQuote || inDoubleQuote))
            {
                escaped = true;
                continue;
            }

            if (ch == '\'' && !inDoubleQuote && !inBacktick)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote && !inBacktick)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (ch == '`' && !inSingleQuote && !inDoubleQuote)
            {
                inBacktick = !inBacktick;
            }
            else if (ch == ';' && !inSingleQuote && !inDoubleQuote && !inBacktick)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    public static string GetBackupFolder()
    {
        return Path.Combine(FileSystem.AppDataDirectory, "DatabaseBackups");
    }

    private static void PruneOldBackups(string backupFolder)
    {
        var files = Directory.GetFiles(backupFolder, $"*{BackupExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(30);

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not prune backup {file.FullName}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
