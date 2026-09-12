using MySqlConnector;
using POS_in_NET.Models;
using System.Data;

namespace POS_in_NET.Services;

/// <summary>
/// Database service for network printer CRUD operations
/// </summary>
public class NetworkPrinterDatabaseService
{
    private readonly DatabaseService _db;

    public NetworkPrinterDatabaseService(DatabaseService databaseService)
    {
        _db = databaseService;
    }

    /// <summary>
    /// Ensures the printer tables exist in the database
    /// </summary>
    public async Task EnsureTablesExistAsync()
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        try
        {
            using var connection = await _db.GetConnectionAsync();
            
            // Create network_printers table
            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = @"
                CREATE TABLE IF NOT EXISTS network_printers (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    name VARCHAR(100) NOT NULL,
                    ip_address VARCHAR(45) NOT NULL,
                    port INT DEFAULT 9100,
                    brand ENUM('epson', 'star', 'other') DEFAULT 'epson',
                    printer_type ENUM('receipt', 'kitchen', 'bar', 'label', 'online', 'takeaway') NOT NULL,
                    paper_width ENUM('80mm', '58mm') DEFAULT '80mm',
                    has_cash_drawer BOOLEAN DEFAULT FALSE,
                    has_cutter BOOLEAN DEFAULT TRUE,
                    has_buzzer BOOLEAN DEFAULT FALSE,
                    supports_two_color BOOLEAN DEFAULT FALSE,
                    is_enabled BOOLEAN DEFAULT TRUE,
                    is_online BOOLEAN DEFAULT FALSE,
                    last_seen DATETIME NULL,
                    color_code VARCHAR(7) DEFAULT '#6366F1',
                    display_order INT DEFAULT 0,
                    notes TEXT NULL,
                    print_group_id VARCHAR(36) NULL,
                    technology VARCHAR(50) NULL,
                    manufacturer VARCHAR(50) NULL,
                    model_code VARCHAR(100) NULL,
                    protocol VARCHAR(30) NULL,
                    resolution_dpi INT NULL,
                    printing_method VARCHAR(50) NULL,
                    max_print_width_mm DECIMAL(7,2) NULL,
                    supported_media VARCHAR(150) NULL,
                    transport VARCHAR(50) NULL,
                    windows_driver VARCHAR(150) NULL,
                    label_profile VARCHAR(100) NULL,
                    media_width_mm DECIMAL(7,2) NULL,
                    label_width_mm DECIMAL(7,2) NULL,
                    label_height_mm DECIMAL(7,2) NULL,
                    gap_size_mm DECIMAL(7,2) NULL,
                    sensor_type VARCHAR(30) NULL,
                    print_speed INT NULL,
                    print_darkness INT NULL,
                    horizontal_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0,
                    vertical_offset_mm DECIMAL(7,2) NOT NULL DEFAULT 0,
                    finishing_mode VARCHAR(30) NOT NULL DEFAULT 'tearoff',
                    number_of_copies INT NOT NULL DEFAULT 1,
                    is_default_label_printer BOOLEAN NOT NULL DEFAULT FALSE,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    UNIQUE INDEX idx_ip_port (ip_address, port)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await cmd1.ExecuteNonQueryAsync();

            // Create network_print_queue table
            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = @"
                CREATE TABLE IF NOT EXISTS network_print_queue (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    printer_id INT NOT NULL,
                    order_id VARCHAR(100) NULL,
                    job_type VARCHAR(50) NOT NULL DEFAULT 'receipt',
                    print_data LONGBLOB NOT NULL,
                    status ENUM('pending', 'printing', 'completed', 'failed') DEFAULT 'pending',
                    retry_count INT DEFAULT 0,
                    max_retries INT DEFAULT 5,
                    error_message TEXT NULL,
                    created_by_terminal_name VARCHAR(120) NULL,
                    claimed_by_terminal_name VARCHAR(120) NULL,
                    claimed_at DATETIME NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    started_at DATETIME NULL,
                    completed_at DATETIME NULL,
                    printed_at DATETIME NULL,
                    last_attempt DATETIME NULL,
                    FOREIGN KEY (printer_id) REFERENCES network_printers(id) ON DELETE CASCADE,
                    INDEX idx_status (status),
                    INDEX idx_printer_status (printer_id, status)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await cmd2.ExecuteNonQueryAsync();

            await MigratePrinterTablesAsync(connection);

            System.Diagnostics.Debug.WriteLine(" Printer tables ensured");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error ensuring printer tables: {ex.Message}");
            throw;
        }
    }

    #region Printer CRUD

    /// <summary>
    /// Get all printers ordered by display order
    /// </summary>
    public async Task<List<NetworkPrinter>> GetAllPrintersAsync()
    {
        var printers = new List<NetworkPrinter>();

        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM network_printers 
                ORDER BY display_order, printer_type, name";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                printers.Add(MapPrinter(reader));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting printers: {ex.Message}");
        }

        return printers;
    }

    /// <summary>
    /// Get printers by type (receipt, kitchen, bar, label)
    /// </summary>
    public async Task<List<NetworkPrinter>> GetPrintersByTypeAsync(NetworkPrinterType type)
    {
        var printers = new List<NetworkPrinter>();
        var typeStr = type.ToString().ToLower();

        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM network_printers 
                WHERE printer_type = @type AND is_enabled = TRUE
                ORDER BY display_order, name";
            cmd.Parameters.AddWithValue("@type", typeStr);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                printers.Add(MapPrinter(reader));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting printers by type: {ex.Message}");
        }

        return printers;
    }

    /// <summary>
    /// Get a single printer by ID
    /// </summary>
    public async Task<NetworkPrinter?> GetPrinterByIdAsync(int id)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM network_printers WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapPrinter(reader);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting printer by ID: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> EndpointExistsAsync(string ipAddress, int port, int? exceptPrinterId = null)
    {
        using var connection = await _db.GetConnectionAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM network_printers
            WHERE ip_address = @ip AND port = @port
              AND (@exceptId IS NULL OR id <> @exceptId)";
        cmd.Parameters.AddWithValue("@ip", ipAddress);
        cmd.Parameters.AddWithValue("@port", port);
        cmd.Parameters.AddWithValue("@exceptId", exceptPrinterId ?? (object)DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task SetDefaultLabelPrinterAsync(int printerId)
    {
        using var connection = await _db.GetConnectionAsync();
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = @"
                UPDATE network_printers
                SET is_default_label_printer = CASE WHEN id = @id THEN TRUE ELSE FALSE END
                WHERE printer_type = 'label'";
            clear.Parameters.AddWithValue("@id", printerId);
            await clear.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Add a new printer
    /// </summary>
    public async Task<int> AddPrinterAsync(NetworkPrinter printer)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO network_printers 
                (name, ip_address, port, brand, printer_type, paper_width, 
                 has_cash_drawer, has_cutter, has_buzzer, supports_two_color, is_enabled,
                 color_code, display_order, notes, print_group_id,
                 technology, manufacturer, model_code, protocol, resolution_dpi, printing_method,
                 max_print_width_mm, supported_media, transport, windows_driver, label_profile,
                 media_width_mm, label_width_mm, label_height_mm, gap_size_mm, sensor_type,
                 print_speed, print_darkness, horizontal_offset_mm, vertical_offset_mm,
                 finishing_mode, number_of_copies, is_default_label_printer)
                VALUES 
                (@name, @ip, @port, @brand, @type, @width,
                 @drawer, @cutter, @buzzer, @twoColor, @enabled,
                 @color, @order, @notes, @printGroupId,
                 @technology, @manufacturer, @modelCode, @protocol, @resolutionDpi, @printingMethod,
                 @maxPrintWidth, @supportedMedia, @transport, @windowsDriver, @labelProfile,
                 @mediaWidth, @labelWidth, @labelHeight, @gapSize, @sensorType,
                 @printSpeed, @printDarkness, @horizontalOffset, @verticalOffset,
                 @finishingMode, @copies, @isDefaultLabel);
                SELECT LAST_INSERT_ID();";

            cmd.Parameters.AddWithValue("@name", printer.Name);
            cmd.Parameters.AddWithValue("@ip", printer.IpAddress);
            cmd.Parameters.AddWithValue("@port", printer.Port);
            cmd.Parameters.AddWithValue("@brand", printer.Brand.ToString().ToLower());
            cmd.Parameters.AddWithValue("@type", printer.PrinterType.ToString().ToLower());
            cmd.Parameters.AddWithValue("@width", printer.PaperWidth == PaperWidth.Mm80 ? "80mm" : "58mm");
            cmd.Parameters.AddWithValue("@drawer", printer.HasCashDrawer);
            cmd.Parameters.AddWithValue("@cutter", printer.HasCutter);
            cmd.Parameters.AddWithValue("@buzzer", printer.HasBuzzer);
            cmd.Parameters.AddWithValue("@twoColor", printer.SupportsTwoColor);
            cmd.Parameters.AddWithValue("@enabled", printer.IsEnabled);
            cmd.Parameters.AddWithValue("@color", printer.ColorCode);
            cmd.Parameters.AddWithValue("@order", printer.DisplayOrder);
            cmd.Parameters.AddWithValue("@notes", printer.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@printGroupId", printer.PrintGroupId ?? (object)DBNull.Value);
            AddLabelParameters(cmd, printer);

            var result = await cmd.ExecuteScalarAsync();
            var newId = Convert.ToInt32(result);
            
            System.Diagnostics.Debug.WriteLine($" Added printer: {printer.Name} (ID: {newId})");
            return newId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error adding printer: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Update an existing printer
    /// </summary>
    public async Task<bool> UpdatePrinterAsync(NetworkPrinter printer)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE network_printers SET
                    name = @name,
                    ip_address = @ip,
                    port = @port,
                    brand = @brand,
                    printer_type = @type,
                    paper_width = @width,
                    has_cash_drawer = @drawer,
                    has_cutter = @cutter,
                    has_buzzer = @buzzer,
                    supports_two_color = @twoColor,
                    is_enabled = @enabled,
                    color_code = @color,
                    display_order = @order,
                    notes = @notes,
                    print_group_id = @printGroupId,
                    technology = @technology,
                    manufacturer = @manufacturer,
                    model_code = @modelCode,
                    protocol = @protocol,
                    resolution_dpi = @resolutionDpi,
                    printing_method = @printingMethod,
                    max_print_width_mm = @maxPrintWidth,
                    supported_media = @supportedMedia,
                    transport = @transport,
                    windows_driver = @windowsDriver,
                    label_profile = @labelProfile,
                    media_width_mm = @mediaWidth,
                    label_width_mm = @labelWidth,
                    label_height_mm = @labelHeight,
                    gap_size_mm = @gapSize,
                    sensor_type = @sensorType,
                    print_speed = @printSpeed,
                    print_darkness = @printDarkness,
                    horizontal_offset_mm = @horizontalOffset,
                    vertical_offset_mm = @verticalOffset,
                    finishing_mode = @finishingMode,
                    number_of_copies = @copies,
                    is_default_label_printer = @isDefaultLabel
                WHERE id = @id";

            cmd.Parameters.AddWithValue("@id", printer.Id);
            cmd.Parameters.AddWithValue("@name", printer.Name);
            cmd.Parameters.AddWithValue("@ip", printer.IpAddress);
            cmd.Parameters.AddWithValue("@port", printer.Port);
            cmd.Parameters.AddWithValue("@brand", printer.Brand.ToString().ToLower());
            cmd.Parameters.AddWithValue("@type", printer.PrinterType.ToString().ToLower());
            cmd.Parameters.AddWithValue("@width", printer.PaperWidth == PaperWidth.Mm80 ? "80mm" : "58mm");
            cmd.Parameters.AddWithValue("@drawer", printer.HasCashDrawer);
            cmd.Parameters.AddWithValue("@cutter", printer.HasCutter);
            cmd.Parameters.AddWithValue("@buzzer", printer.HasBuzzer);
            cmd.Parameters.AddWithValue("@twoColor", printer.SupportsTwoColor);
            cmd.Parameters.AddWithValue("@enabled", printer.IsEnabled);
            cmd.Parameters.AddWithValue("@color", printer.ColorCode);
            cmd.Parameters.AddWithValue("@order", printer.DisplayOrder);
            cmd.Parameters.AddWithValue("@notes", printer.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@printGroupId", printer.PrintGroupId ?? (object)DBNull.Value);
            AddLabelParameters(cmd, printer);

            var rows = await cmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($" Updated printer: {printer.Name}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error updating printer: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Delete a printer by ID
    /// </summary>
    public async Task<bool> DeletePrinterAsync(int id)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM network_printers WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($" Deleted printer ID: {id}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error deleting printer: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Update printer online status
    /// </summary>
    public async Task UpdatePrinterStatusAsync(int id, bool isOnline)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE network_printers SET 
                    is_online = @online,
                    last_seen = CASE WHEN @online = TRUE THEN NOW() ELSE last_seen END
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@online", isOnline);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error updating printer status: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggle printer enabled/disabled
    /// </summary>
    public async Task<bool> TogglePrinterEnabledAsync(int id)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE network_printers SET is_enabled = NOT is_enabled WHERE id = @id;
                SELECT is_enabled FROM network_printers WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToBoolean(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error toggling printer: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Print Queue

    /// <summary>
    /// Add a job to the print queue
    /// </summary>
    public async Task<int> EnqueuePrintJobAsync(PrintJob job)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO network_print_queue 
                (printer_id, order_id, job_type, print_data, status, max_retries, created_by_terminal_name)
                VALUES (@printer, @order, @type, @data, 'pending', @max, @createdByTerminalName);
                SELECT LAST_INSERT_ID();";

            cmd.Parameters.AddWithValue("@printer", job.PrinterId);
            cmd.Parameters.AddWithValue("@order", job.OrderId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@type", job.JobType.ToString().ToLower().Replace("kitchenticket", "kitchen_ticket"));
            cmd.Parameters.AddWithValue("@data", job.PrintData ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@max", job.MaxRetries);
            cmd.Parameters.AddWithValue("@createdByTerminalName", GetCurrentTerminalName());

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error enqueueing print job: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Get pending print jobs
    /// </summary>
    public async Task<List<PrintJob>> GetPendingJobsAsync()
    {
        var jobs = new List<PrintJob>();

        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT pq.*, np.name as printer_name, np.ip_address, np.port, np.brand
                FROM network_print_queue pq
                JOIN network_printers np ON pq.printer_id = np.id
                WHERE pq.status = 'pending' AND pq.retry_count < pq.max_retries
                ORDER BY pq.created_at";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                jobs.Add(MapPrintJob(reader));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting pending jobs: {ex.Message}");
        }

        return jobs;
    }

    /// <summary>
    /// Update print job status
    /// </summary>
    public async Task UpdateJobStatusAsync(int jobId, PrintJobStatus status, string? errorMessage = null)
    {
        try
        {
            using var connection = await _db.GetConnectionAsync();
            using var cmd = connection.CreateCommand();
            
            var statusStr = status.ToString().ToLower();
            
            if (status == PrintJobStatus.Printing)
            {
                cmd.CommandText = @"
                    UPDATE network_print_queue SET 
                        status = @status, 
                            started_at = NOW(),
                            last_attempt = NOW()
                    WHERE id = @id";
            }
            else if (status == PrintJobStatus.Completed)
            {
                cmd.CommandText = @"
                        UPDATE network_print_queue SET 
                        status = @status, 
                            completed_at = NOW(),
                            printed_at = NOW(),
                            last_attempt = NOW()
                    WHERE id = @id";
            }
            else if (status == PrintJobStatus.Failed)
            {
                cmd.CommandText = @"
                        UPDATE network_print_queue SET 
                        status = CASE WHEN retry_count + 1 >= max_retries THEN 'failed' ELSE 'pending' END,
                        retry_count = retry_count + 1,
                            error_message = @error,
                            last_attempt = NOW()
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@error", errorMessage ?? (object)DBNull.Value);
            }
            else
            {
                    cmd.CommandText = "UPDATE network_print_queue SET status = @status WHERE id = @id";
            }

            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@status", statusStr);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error updating job status: {ex.Message}");
        }
    }

    #endregion

    #region Schema Migration

    private async Task MigratePrinterTablesAsync(MySqlConnection connection)
    {
        await ExecuteNonQueryAsync(connection, @"
            ALTER TABLE network_printers
            MODIFY COLUMN printer_type ENUM('receipt', 'kitchen', 'bar', 'label', 'online', 'takeaway') NOT NULL");
        await ExecuteNonQueryAsync(connection, @"
            ALTER TABLE network_printers
            MODIFY COLUMN brand ENUM('epson', 'star', 'toshiba', 'other') NOT NULL DEFAULT 'epson'");

        if (!await ColumnExistsAsync(connection, "network_printers", "print_group_id"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_printers
                ADD COLUMN print_group_id VARCHAR(36) NULL AFTER notes");
        }

        if (!await ColumnExistsAsync(connection, "network_printers", "supports_two_color"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_printers
                ADD COLUMN supports_two_color BOOLEAN DEFAULT FALSE AFTER has_buzzer");
        }

        var labelColumns = new (string Name, string Definition)[]
        {
            ("technology", "VARCHAR(50) NULL"),
            ("manufacturer", "VARCHAR(50) NULL"),
            ("model_code", "VARCHAR(100) NULL"),
            ("protocol", "VARCHAR(30) NULL"),
            ("resolution_dpi", "INT NULL"),
            ("printing_method", "VARCHAR(50) NULL"),
            ("max_print_width_mm", "DECIMAL(7,2) NULL"),
            ("supported_media", "VARCHAR(150) NULL"),
            ("transport", "VARCHAR(50) NULL"),
            ("windows_driver", "VARCHAR(150) NULL"),
            ("label_profile", "VARCHAR(100) NULL"),
            ("media_width_mm", "DECIMAL(7,2) NULL"),
            ("label_width_mm", "DECIMAL(7,2) NULL"),
            ("label_height_mm", "DECIMAL(7,2) NULL"),
            ("gap_size_mm", "DECIMAL(7,2) NULL"),
            ("sensor_type", "VARCHAR(30) NULL"),
            ("print_speed", "INT NULL"),
            ("print_darkness", "INT NULL"),
            ("horizontal_offset_mm", "DECIMAL(7,2) NOT NULL DEFAULT 0"),
            ("vertical_offset_mm", "DECIMAL(7,2) NOT NULL DEFAULT 0"),
            ("finishing_mode", "VARCHAR(30) NOT NULL DEFAULT 'tearoff'"),
            ("number_of_copies", "INT NOT NULL DEFAULT 1"),
            ("is_default_label_printer", "BOOLEAN NOT NULL DEFAULT FALSE")
        };

        foreach (var column in labelColumns)
        {
            if (!await ColumnExistsAsync(connection, "network_printers", column.Name))
            {
                await ExecuteNonQueryAsync(connection, $"ALTER TABLE network_printers ADD COLUMN {column.Name} {column.Definition}");
            }
        }

        await CreateIndexIfMissingAsync(connection, "network_printers", "idx_network_printers_print_group_id", "CREATE INDEX idx_network_printers_print_group_id ON network_printers(print_group_id)");
        await CreateIndexIfMissingAsync(connection, "network_printers", "idx_network_printers_type_enabled", "CREATE INDEX idx_network_printers_type_enabled ON network_printers(printer_type, is_enabled)");

        if (!await ColumnExistsAsync(connection, "network_print_queue", "max_retries"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN max_retries INT DEFAULT 5 AFTER retry_count");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "started_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN started_at DATETIME NULL AFTER created_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "completed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN completed_at DATETIME NULL AFTER started_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "printed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN printed_at DATETIME NULL AFTER completed_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "last_attempt"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN last_attempt DATETIME NULL AFTER printed_at");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "created_by_terminal_name"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN created_by_terminal_name VARCHAR(120) NULL AFTER error_message");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "claimed_by_terminal_name"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN claimed_by_terminal_name VARCHAR(120) NULL AFTER created_by_terminal_name");
        }

        if (!await ColumnExistsAsync(connection, "network_print_queue", "claimed_at"))
        {
            await ExecuteNonQueryAsync(connection, @"
                ALTER TABLE network_print_queue
                ADD COLUMN claimed_at DATETIME NULL AFTER claimed_by_terminal_name");
        }

        await CreateIndexIfMissingAsync(connection, "network_print_queue", "idx_network_print_queue_status", "CREATE INDEX idx_network_print_queue_status ON network_print_queue(status)");
        await CreateIndexIfMissingAsync(connection, "network_print_queue", "idx_network_print_queue_printer_status", "CREATE INDEX idx_network_print_queue_printer_status ON network_print_queue(printer_id, status)");
    }

    private static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string tableName, string columnName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        cmd.Parameters.AddWithValue("@columnName", columnName);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> IndexExistsAsync(MySqlConnection connection, string tableName, string indexName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
              AND INDEX_NAME = @indexName";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        cmd.Parameters.AddWithValue("@indexName", indexName);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task CreateIndexIfMissingAsync(MySqlConnection connection, string tableName, string indexName, string sql)
    {
        if (!await IndexExistsAsync(connection, tableName, indexName))
        {
            await ExecuteNonQueryAsync(connection, sql);
        }
    }

    private static async Task ExecuteNonQueryAsync(MySqlConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetCurrentTerminalName()
    {
        try
        {
            return TerminalConfigurationService.GetConfiguration().TerminalName;
        }
        catch
        {
            return "Terminal";
        }
    }

    #endregion

    #region Mapping Helpers

    private static void AddLabelParameters(MySqlCommand cmd, NetworkPrinter printer)
    {
        cmd.Parameters.AddWithValue("@technology", DbValue(printer.Technology));
        cmd.Parameters.AddWithValue("@manufacturer", DbValue(printer.Manufacturer));
        cmd.Parameters.AddWithValue("@modelCode", DbValue(printer.ModelCode));
        cmd.Parameters.AddWithValue("@protocol", DbValue(printer.Protocol));
        cmd.Parameters.AddWithValue("@resolutionDpi", DbValue(printer.ResolutionDpi));
        cmd.Parameters.AddWithValue("@printingMethod", DbValue(printer.PrintingMethod));
        cmd.Parameters.AddWithValue("@maxPrintWidth", DbValue(printer.MaximumPrintWidthMm));
        cmd.Parameters.AddWithValue("@supportedMedia", DbValue(printer.SupportedMedia));
        cmd.Parameters.AddWithValue("@transport", DbValue(printer.Transport));
        cmd.Parameters.AddWithValue("@windowsDriver", DbValue(printer.WindowsDriver));
        cmd.Parameters.AddWithValue("@labelProfile", DbValue(printer.LabelProfile));
        cmd.Parameters.AddWithValue("@mediaWidth", DbValue(printer.MediaWidthMm));
        cmd.Parameters.AddWithValue("@labelWidth", DbValue(printer.LabelWidthMm));
        cmd.Parameters.AddWithValue("@labelHeight", DbValue(printer.LabelHeightMm));
        cmd.Parameters.AddWithValue("@gapSize", DbValue(printer.GapSizeMm));
        cmd.Parameters.AddWithValue("@sensorType", printer.SensorType?.ToString().ToLowerInvariant() ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@printSpeed", DbValue(printer.PrintSpeed));
        cmd.Parameters.AddWithValue("@printDarkness", DbValue(printer.PrintDarkness));
        cmd.Parameters.AddWithValue("@horizontalOffset", printer.HorizontalOffsetMm);
        cmd.Parameters.AddWithValue("@verticalOffset", printer.VerticalOffsetMm);
        cmd.Parameters.AddWithValue("@finishingMode", printer.FinishingMode.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@copies", printer.NumberOfCopies);
        cmd.Parameters.AddWithValue("@isDefaultLabel", printer.IsDefaultLabelPrinter);
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private NetworkPrinter MapPrinter(IDataReader reader)
    {
        return new NetworkPrinter
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            IpAddress = reader.GetString(reader.GetOrdinal("ip_address")),
            Port = reader.GetInt32(reader.GetOrdinal("port")),
            Brand = Enum.Parse<PrinterBrand>(reader.GetString(reader.GetOrdinal("brand")), true),
            PrinterType = Enum.Parse<NetworkPrinterType>(reader.GetString(reader.GetOrdinal("printer_type")), true),
            PaperWidth = reader.GetString(reader.GetOrdinal("paper_width")) == "80mm" ? PaperWidth.Mm80 : PaperWidth.Mm58,
            HasCashDrawer = reader.GetBoolean(reader.GetOrdinal("has_cash_drawer")),
            HasCutter = reader.GetBoolean(reader.GetOrdinal("has_cutter")),
            HasBuzzer = reader.GetBoolean(reader.GetOrdinal("has_buzzer")),
            SupportsTwoColor = reader.GetBoolean(reader.GetOrdinal("supports_two_color")),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            IsOnline = reader.GetBoolean(reader.GetOrdinal("is_online")),
            LastSeen = reader.IsDBNull(reader.GetOrdinal("last_seen")) ? null : reader.GetDateTime(reader.GetOrdinal("last_seen")),
            ColorCode = reader.GetString(reader.GetOrdinal("color_code")),
            DisplayOrder = reader.GetInt32(reader.GetOrdinal("display_order")),
            Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
            PrintGroupId = reader.IsDBNull(reader.GetOrdinal("print_group_id")) ? null : reader.GetString(reader.GetOrdinal("print_group_id")),
            Technology = ReadNullableString(reader, "technology"),
            Manufacturer = ReadNullableString(reader, "manufacturer"),
            ModelCode = ReadNullableString(reader, "model_code"),
            Protocol = ReadNullableString(reader, "protocol"),
            ResolutionDpi = ReadNullableInt(reader, "resolution_dpi"),
            PrintingMethod = ReadNullableString(reader, "printing_method"),
            MaximumPrintWidthMm = ReadNullableDecimal(reader, "max_print_width_mm"),
            SupportedMedia = ReadNullableString(reader, "supported_media"),
            Transport = ReadNullableString(reader, "transport"),
            WindowsDriver = ReadNullableString(reader, "windows_driver"),
            LabelProfile = ReadNullableString(reader, "label_profile"),
            MediaWidthMm = ReadNullableDecimal(reader, "media_width_mm"),
            LabelWidthMm = ReadNullableDecimal(reader, "label_width_mm"),
            LabelHeightMm = ReadNullableDecimal(reader, "label_height_mm"),
            GapSizeMm = ReadNullableDecimal(reader, "gap_size_mm"),
            SensorType = ParseNullableEnum<LabelSensorType>(ReadNullableString(reader, "sensor_type")),
            PrintSpeed = ReadNullableInt(reader, "print_speed"),
            PrintDarkness = ReadNullableInt(reader, "print_darkness"),
            HorizontalOffsetMm = ReadNullableDecimal(reader, "horizontal_offset_mm") ?? 0,
            VerticalOffsetMm = ReadNullableDecimal(reader, "vertical_offset_mm") ?? 0,
            FinishingMode = ParseNullableEnum<LabelFinishingMode>(ReadNullableString(reader, "finishing_mode")) ?? LabelFinishingMode.TearOff,
            NumberOfCopies = ReadNullableInt(reader, "number_of_copies") ?? 1,
            IsDefaultLabelPrinter = ReadNullableBoolean(reader, "is_default_label_printer"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        };
    }

    private static string? ReadNullableString(IDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal? ReadNullableDecimal(IDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static bool ReadNullableBoolean(IDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
    }

    private static TEnum? ParseNullableEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;

    private PrintJob MapPrintJob(IDataReader reader)
    {
        var job = new PrintJob
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            PrinterId = reader.GetInt32(reader.GetOrdinal("printer_id")),
            OrderId = ReadNullableInt(reader, "order_id"),
            Status = Enum.Parse<PrintJobStatus>(reader.GetString(reader.GetOrdinal("status")), true),
            RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count")),
            MaxRetries = reader.GetInt32(reader.GetOrdinal("max_retries")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
        };

        // Parse job type
        var jobTypeStr = reader.GetString(reader.GetOrdinal("job_type")).Replace("_", "");
        job.JobType = Enum.Parse<PrintJobType>(jobTypeStr, true);

        // Get print data if present
        var dataOrdinal = reader.GetOrdinal("print_data");
        if (!reader.IsDBNull(dataOrdinal))
        {
            job.PrintData = (byte[])reader.GetValue(dataOrdinal);
        }

        // Map printer info if available
        try
        {
            job.Printer = new NetworkPrinter
            {
                Id = job.PrinterId,
                Name = reader.GetString(reader.GetOrdinal("printer_name")),
                IpAddress = reader.GetString(reader.GetOrdinal("ip_address")),
                Port = reader.GetInt32(reader.GetOrdinal("port")),
                Brand = Enum.Parse<PrinterBrand>(reader.GetString(reader.GetOrdinal("brand")), true)
            };
        }
        catch { /* Printer columns not in query */ }

        return job;
    }

    private static int? ReadNullableInt(IDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is int intValue)
        {
            return intValue;
        }

        return int.TryParse(value.ToString(), out var parsedValue) ? parsedValue : null;
    }

    #endregion
}
