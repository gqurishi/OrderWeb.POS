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
                    printer_type ENUM('receipt', 'kitchen', 'bar', 'label') NOT NULL,
                    paper_width ENUM('80mm', '58mm') DEFAULT '80mm',
                    has_cash_drawer BOOLEAN DEFAULT FALSE,
                    has_cutter BOOLEAN DEFAULT TRUE,
                    has_buzzer BOOLEAN DEFAULT FALSE,
                    is_enabled BOOLEAN DEFAULT TRUE,
                    is_online BOOLEAN DEFAULT FALSE,
                    last_seen DATETIME NULL,
                    color_code VARCHAR(7) DEFAULT '#6366F1',
                    display_order INT DEFAULT 0,
                    notes TEXT NULL,
                    print_group_id VARCHAR(36) NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    UNIQUE INDEX idx_ip_port (ip_address, port)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await cmd1.ExecuteNonQueryAsync();

            // Create print_queue table
            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = @"
                CREATE TABLE IF NOT EXISTS print_queue (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    printer_id INT NOT NULL,
                    order_id INT NULL,
                    job_type ENUM('receipt', 'kitchen_ticket', 'test', 'cash_drawer') NOT NULL,
                    print_data LONGBLOB,
                    status ENUM('pending', 'printing', 'completed', 'failed') DEFAULT 'pending',
                    retry_count INT DEFAULT 0,
                    max_retries INT DEFAULT 5,
                    error_message TEXT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    started_at DATETIME NULL,
                    completed_at DATETIME NULL,
                    FOREIGN KEY (printer_id) REFERENCES network_printers(id) ON DELETE CASCADE,
                    INDEX idx_status (status),
                    INDEX idx_printer_status (printer_id, status)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await cmd2.ExecuteNonQueryAsync();

            System.Diagnostics.Debug.WriteLine("✅ Printer tables ensured");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error ensuring printer tables: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"❌ Error getting printers: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"❌ Error getting printers by type: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"❌ Error getting printer by ID: {ex.Message}");
        }

        return null;
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
                 has_cash_drawer, has_cutter, has_buzzer, is_enabled, 
                 color_code, display_order, notes, print_group_id)
                VALUES 
                (@name, @ip, @port, @brand, @type, @width,
                 @drawer, @cutter, @buzzer, @enabled,
                 @color, @order, @notes, @printGroupId);
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
            cmd.Parameters.AddWithValue("@enabled", printer.IsEnabled);
            cmd.Parameters.AddWithValue("@color", printer.ColorCode);
            cmd.Parameters.AddWithValue("@order", printer.DisplayOrder);
            cmd.Parameters.AddWithValue("@notes", printer.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@printGroupId", printer.PrintGroupId ?? (object)DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            var newId = Convert.ToInt32(result);
            
            System.Diagnostics.Debug.WriteLine($"✅ Added printer: {printer.Name} (ID: {newId})");
            return newId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error adding printer: {ex.Message}");
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
                    is_enabled = @enabled,
                    color_code = @color,
                    display_order = @order,
                    notes = @notes,
                    print_group_id = @printGroupId
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
            cmd.Parameters.AddWithValue("@enabled", printer.IsEnabled);
            cmd.Parameters.AddWithValue("@color", printer.ColorCode);
            cmd.Parameters.AddWithValue("@order", printer.DisplayOrder);
            cmd.Parameters.AddWithValue("@notes", printer.Notes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@printGroupId", printer.PrintGroupId ?? (object)DBNull.Value);

            var rows = await cmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($"✅ Updated printer: {printer.Name}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error updating printer: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"✅ Deleted printer ID: {id}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error deleting printer: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"❌ Error updating printer status: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"❌ Error toggling printer: {ex.Message}");
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
                INSERT INTO print_queue 
                (printer_id, order_id, job_type, print_data, status, max_retries)
                VALUES (@printer, @order, @type, @data, 'pending', @max);
                SELECT LAST_INSERT_ID();";

            cmd.Parameters.AddWithValue("@printer", job.PrinterId);
            cmd.Parameters.AddWithValue("@order", job.OrderId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@type", job.JobType.ToString().ToLower().Replace("kitchenticket", "kitchen_ticket"));
            cmd.Parameters.AddWithValue("@data", job.PrintData ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@max", job.MaxRetries);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error enqueueing print job: {ex.Message}");
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
                FROM print_queue pq
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
            System.Diagnostics.Debug.WriteLine($"❌ Error getting pending jobs: {ex.Message}");
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
                    UPDATE print_queue SET 
                        status = @status, 
                        started_at = NOW()
                    WHERE id = @id";
            }
            else if (status == PrintJobStatus.Completed)
            {
                cmd.CommandText = @"
                    UPDATE print_queue SET 
                        status = @status, 
                        completed_at = NOW()
                    WHERE id = @id";
            }
            else if (status == PrintJobStatus.Failed)
            {
                cmd.CommandText = @"
                    UPDATE print_queue SET 
                        status = CASE WHEN retry_count + 1 >= max_retries THEN 'failed' ELSE 'pending' END,
                        retry_count = retry_count + 1,
                        error_message = @error
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@error", errorMessage ?? (object)DBNull.Value);
            }
            else
            {
                cmd.CommandText = "UPDATE print_queue SET status = @status WHERE id = @id";
            }

            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@status", statusStr);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error updating job status: {ex.Message}");
        }
    }

    #endregion

    #region Mapping Helpers

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
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            IsOnline = reader.GetBoolean(reader.GetOrdinal("is_online")),
            LastSeen = reader.IsDBNull(reader.GetOrdinal("last_seen")) ? null : reader.GetDateTime(reader.GetOrdinal("last_seen")),
            ColorCode = reader.GetString(reader.GetOrdinal("color_code")),
            DisplayOrder = reader.GetInt32(reader.GetOrdinal("display_order")),
            Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
            PrintGroupId = reader.IsDBNull(reader.GetOrdinal("print_group_id")) ? null : reader.GetString(reader.GetOrdinal("print_group_id")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        };
    }

    private PrintJob MapPrintJob(IDataReader reader)
    {
        var job = new PrintJob
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            PrinterId = reader.GetInt32(reader.GetOrdinal("printer_id")),
            OrderId = reader.IsDBNull(reader.GetOrdinal("order_id")) ? null : reader.GetInt32(reader.GetOrdinal("order_id")),
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

    #endregion
}
