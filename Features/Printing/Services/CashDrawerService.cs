using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class CashDrawerService
{
    private const string DeviceType = "receipt_printer_rj11";

    private readonly DatabaseService _databaseService;
    private readonly NetworkPrinterService _printerService;
    private readonly AuthenticationService _authenticationService;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public CashDrawerService(
        DatabaseService databaseService,
        NetworkPrinterService printerService,
        AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _printerService = printerService;
        _authenticationService = authenticationService;
    }

    public async Task<CashDrawerOpenResult> OpenAsync(CashDrawerOpenRequest request)
    {
        await EnsureTableExistsAsync();

        NetworkPrinter? drawerPrinter = null;
        var errorMessage = string.Empty;
        var opened = false;

        try
        {
            drawerPrinter = await GetConfiguredReceiptDrawerAsync();
            if (drawerPrinter == null)
            {
                errorMessage = "No enabled receipt printer is configured with a cash drawer.";
                var missingAuditId = await InsertAuditAsync(request, null, false, errorMessage);
                return new CashDrawerOpenResult
                {
                    Success = false,
                    AuditId = missingAuditId,
                    Message = errorMessage
                };
            }

            opened = await _printerService.OpenCashDrawerAsync(drawerPrinter);
            errorMessage = opened ? string.Empty : $"Failed to send drawer pulse to {drawerPrinter.Name}.";
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        var auditId = await InsertAuditAsync(request, drawerPrinter, opened, errorMessage);
        return new CashDrawerOpenResult
        {
            Success = opened,
            AuditId = auditId,
            PrinterName = drawerPrinter?.Name,
            Message = opened
                ? $"Cash drawer opened on {drawerPrinter?.Name}."
                : errorMessage
        };
    }

    public async Task<List<CashDrawerAuditEntry>> GetAuditEntriesAsync(DateTime startInclusive, DateTime endExclusive)
    {
        await EnsureTableExistsAsync();

        var entries = new List<CashDrawerAuditEntry>();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id,
                   event_at,
                   requested_by_user_id,
                   requested_by_name,
                   requested_by_role,
                   reason,
                   source_area,
                   order_id,
                   order_number,
                   table_session_id,
                   table_number,
                   device_type,
                   printer_id,
                   printer_name,
                   printer_ip,
                   printer_port,
                   success,
                   error_message
            FROM cash_drawer_events
            WHERE event_at >= @startAt
              AND event_at < @endAt
            ORDER BY event_at DESC, id DESC";
        command.Parameters.AddWithValue("@startAt", startInclusive);
        command.Parameters.AddWithValue("@endAt", endExclusive);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new CashDrawerAuditEntry
            {
                Id = reader.GetInt32("id"),
                EventAt = reader.GetDateTime("event_at"),
                RequestedByUserId = reader.IsDBNull(reader.GetOrdinal("requested_by_user_id"))
                    ? null
                    : reader.GetInt32("requested_by_user_id"),
                RequestedByName = ReadString(reader, "requested_by_name"),
                RequestedByRole = ReadString(reader, "requested_by_role"),
                Reason = ReadString(reader, "reason"),
                SourceArea = ReadString(reader, "source_area"),
                OrderId = ReadNullableString(reader, "order_id"),
                OrderNumber = ReadNullableString(reader, "order_number"),
                TableSessionId = reader.IsDBNull(reader.GetOrdinal("table_session_id"))
                    ? null
                    : reader.GetInt32("table_session_id"),
                TableNumber = ReadNullableString(reader, "table_number"),
                DeviceType = ReadString(reader, "device_type"),
                PrinterId = reader.IsDBNull(reader.GetOrdinal("printer_id"))
                    ? null
                    : reader.GetInt32("printer_id"),
                PrinterName = ReadString(reader, "printer_name"),
                PrinterIp = ReadString(reader, "printer_ip"),
                PrinterPort = reader.IsDBNull(reader.GetOrdinal("printer_port"))
                    ? null
                    : reader.GetInt32("printer_port"),
                Success = reader.GetBoolean("success"),
                ErrorMessage = ReadString(reader, "error_message")
            });
        }

        return entries;
    }

    public async Task EnsureTableExistsAsync()
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync();
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var connection = await _databaseService.GetConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS cash_drawer_events (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    requested_by_user_id INT NULL,
                    requested_by_name VARCHAR(150) NULL,
                    requested_by_role VARCHAR(20) NULL,
                    reason VARCHAR(255) NULL,
                    source_area VARCHAR(80) NULL,
                    order_id VARCHAR(100) NULL,
                    order_number VARCHAR(50) NULL,
                    table_session_id INT NULL,
                    table_number VARCHAR(50) NULL,
                    device_type VARCHAR(50) NOT NULL DEFAULT 'receipt_printer_rj11',
                    printer_id INT NULL,
                    printer_name VARCHAR(100) NULL,
                    printer_ip VARCHAR(45) NULL,
                    printer_port INT NULL,
                    success TINYINT(1) NOT NULL DEFAULT 0,
                    error_message TEXT NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_cash_drawer_event_at (event_at),
                    INDEX idx_cash_drawer_user_time (requested_by_user_id, event_at),
                    INDEX idx_cash_drawer_success_time (success, event_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync();

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task<NetworkPrinter?> GetConfiguredReceiptDrawerAsync()
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id,
                   name,
                   ip_address,
                   port,
                   brand,
                   paper_width,
                   has_cash_drawer,
                   is_enabled
            FROM network_printers
            WHERE printer_type = 'receipt'
              AND is_enabled = TRUE
              AND has_cash_drawer = TRUE
            ORDER BY display_order, id
            LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new NetworkPrinter
        {
            Id = reader.GetInt32("id"),
            Name = reader.GetString("name"),
            IpAddress = reader.GetString("ip_address"),
            Port = reader.GetInt32("port"),
            Brand = Enum.TryParse<PrinterBrand>(reader.GetString("brand"), true, out var brand)
                ? brand
                : PrinterBrand.Epson,
            PrinterType = NetworkPrinterType.Receipt,
            PaperWidth = reader.GetString("paper_width") == "58mm" ? PaperWidth.Mm58 : PaperWidth.Mm80,
            HasCashDrawer = reader.GetBoolean("has_cash_drawer"),
            IsEnabled = reader.GetBoolean("is_enabled")
        };
    }

    private async Task<int> InsertAuditAsync(
        CashDrawerOpenRequest request,
        NetworkPrinter? printer,
        bool success,
        string? errorMessage)
    {
        var currentUser = _authenticationService.CurrentUser;

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO cash_drawer_events
                (event_at,
                 requested_by_user_id,
                 requested_by_name,
                 requested_by_role,
                 reason,
                 source_area,
                 order_id,
                 order_number,
                 table_session_id,
                 table_number,
                 device_type,
                 printer_id,
                 printer_name,
                 printer_ip,
                 printer_port,
                 success,
                 error_message)
            VALUES
                (NOW(),
                 @userId,
                 @userName,
                 @userRole,
                 @reason,
                 @sourceArea,
                 @orderId,
                 @orderNumber,
                 @tableSessionId,
                 @tableNumber,
                 @deviceType,
                 @printerId,
                 @printerName,
                 @printerIp,
                 @printerPort,
                 @success,
                 @errorMessage);
            SELECT LAST_INSERT_ID();";

        command.Parameters.AddWithValue("@userId", currentUser?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", GetCurrentUserDisplayName(currentUser));
        command.Parameters.AddWithValue("@userRole", currentUser?.Role.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@reason", NormalizeText(request.Reason, "Manual open"));
        command.Parameters.AddWithValue("@sourceArea", NormalizeText(request.SourceArea, "pos"));
        command.Parameters.AddWithValue("@orderId", string.IsNullOrWhiteSpace(request.OrderId) ? DBNull.Value : request.OrderId.Trim());
        command.Parameters.AddWithValue("@orderNumber", string.IsNullOrWhiteSpace(request.OrderNumber) ? DBNull.Value : request.OrderNumber.Trim());
        command.Parameters.AddWithValue("@tableSessionId", request.TableSessionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tableNumber", string.IsNullOrWhiteSpace(request.TableNumber) ? DBNull.Value : request.TableNumber.Trim());
        command.Parameters.AddWithValue("@deviceType", DeviceType);
        command.Parameters.AddWithValue("@printerId", printer?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@printerName", printer?.Name ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@printerIp", printer?.IpAddress ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@printerPort", printer?.Port ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@success", success);
        command.Parameters.AddWithValue("@errorMessage", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage.Trim());

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static string GetCurrentUserDisplayName(User? user)
    {
        if (user == null)
        {
            return "Unknown";
        }

        return !string.IsNullOrWhiteSpace(user.Name)
            ? user.Name.Trim()
            : user.Username.Trim();
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ReadString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
