using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using System.Text;
using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Result of an auto-print operation
/// </summary>
public class AutoPrintResult
{
    public bool Success { get; set; }
    public string? OnlinePrintJobId { get; set; }
    public string? TakeawayPrintJobId { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptsMade { get; set; }
    public int LabelsQueued { get; set; }
}

/// <summary>
/// Event args for print failure warning
/// </summary>
public class PrintFailureEventArgs : EventArgs
{
    public string OrderNumber { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string PrinterType { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public int AttemptsMade { get; set; }
}

/// <summary>
/// Service for auto-printing online orders to designated printers.
/// Routes orders to Online (customer receipt) and Takeaway (kitchen ticket) printers.
/// Uses NetworkPrintQueueService for ESC/POS thermal printing.
/// </summary>
public class OnlineOrderAutoPrintService
{
    private readonly NetworkPrinterDatabaseService _printerDbService;
    private readonly NetworkPrintQueueService _printQueueService;
    private readonly NetworkPrinterService _printerService;
    private readonly EscPosBuilder _escPosBuilder;
    private readonly CloudOrderService _cloudOrderService;
    private readonly DatabaseService _databaseService;
    private readonly KitchenTemplateSettingsService _kitchenTemplateSettingsService;
    
    // Configuration
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int RETRY_INTERVAL_MINUTES = 3; // ~10 minutes total for 3 attempts
    
    // Events for UI notifications
    public event EventHandler<PrintFailureEventArgs>? PrintFailed;
    public event EventHandler<string>? PrintSucceeded;

    public OnlineOrderAutoPrintService(
        NetworkPrinterDatabaseService printerDbService,
        NetworkPrintQueueService printQueueService,
        NetworkPrinterService printerService,
        EscPosBuilder escPosBuilder,
        CloudOrderService cloudOrderService,
        DatabaseService databaseService,
        KitchenTemplateSettingsService kitchenTemplateSettingsService)
    {
        _printerDbService = printerDbService;
        _printQueueService = printQueueService;
        _printerService = printerService;
        _escPosBuilder = escPosBuilder;
        _cloudOrderService = cloudOrderService;
        _databaseService = databaseService;
        _kitchenTemplateSettingsService = kitchenTemplateSettingsService;

        _printQueueService.JobCompleted += OnPrintJobCompleted;
        _printQueueService.JobFailed += OnPrintJobFailed;
        
        Debug.WriteLine("OnlineOrderAutoPrintService initialized");
    }

    /// <summary>
    /// Auto-print an online order to designated printers.
    /// Prints customer receipt to Online printer and kitchen ticket to Takeaway printer.
    /// </summary>
    public async Task<AutoPrintResult> PrintOnlineOrderAsync(CloudOrderResponse order, string? staffReprintKey = null)
    {
        var result = new AutoPrintResult();
        var printStartTime = DateTime.UtcNow;
        
        try
        {
            Debug.WriteLine($"Auto-printing online order: {order.OrderNumber}");
            
            // Get designated printers
            var onlinePrinter = await GetDesignatedPrinterAsync(NetworkPrinterType.Online);
            var takeawayPrinter = await GetDesignatedPrinterAsync(NetworkPrinterType.Takeaway);
            
            if (onlinePrinter == null || takeawayPrinter == null)
            {
                var missing = new List<string>();
                if (onlinePrinter == null) missing.Add("Online Receipt printer");
                if (takeawayPrinter == null) missing.Add("Takeaway Kitchen printer");

                result.ErrorMessage = $"Missing required printer(s): {string.Join(", ", missing)}";
                Debug.WriteLine(result.ErrorMessage);
                
                // Configuration failure is terminal, but never report "printed" until physical printing succeeds.
                await SendPrintAckAsync(order.Id, "failed", result.ErrorMessage, printStartTime);
                return result;
            }

            var errors = new List<string>();
            
            // Print customer receipt to Online printer
            if (onlinePrinter != null)
            {
                var onlineResult = await PrintCustomerReceiptAsync(order, onlinePrinter);
                if (onlineResult.Success)
                {
                    result.OnlinePrintJobId = onlineResult.JobId;
                    Debug.WriteLine($"Online receipt queued: Job {onlineResult.JobId}");
                }
                else
                {
                    errors.Add($"Online printer: {onlineResult.Error}");
                    RaisePrintFailedEvent(order, "Online", onlineResult.Error ?? "Unknown error", onlineResult.Attempts);
                }
            }
            
            // Print kitchen ticket to Takeaway printer
            if (takeawayPrinter != null)
            {
                var takeawayResult = await PrintKitchenTicketAsync(order, takeawayPrinter);
                if (takeawayResult.Success)
                {
                    result.TakeawayPrintJobId = takeawayResult.JobId;
                    Debug.WriteLine($"Takeaway ticket queued: Job {takeawayResult.JobId}");
                    result.LabelsQueued = await TryQueueWebsiteLabelsAsync(order, staffReprintKey);
                }
                else
                {
                    errors.Add($"Takeaway printer: {takeawayResult.Error}");
                    RaisePrintFailedEvent(order, "Takeaway", takeawayResult.Error ?? "Unknown error", takeawayResult.Attempts);
                }
            }

            // Determine overall result
            if (result.OnlinePrintJobId != null && result.TakeawayPrintJobId != null)
            {
                result.Success = true;
                PrintSucceeded?.Invoke(this, order.OrderNumber);
                Debug.WriteLine($"Order {order.OrderNumber} queued for Online Receipt and Takeaway Kitchen printers");
            }
            else
            {
                result.ErrorMessage = string.Join("; ", errors);
                await SendPrintAckAsync(order.Id, "failed", result.ErrorMessage, printStartTime);
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error auto-printing order {order.OrderNumber}: {ex.Message}");
            result.ErrorMessage = ex.Message;
            
            // Send failure ACK
            await SendPrintAckAsync(order.Id, "failed", ex.Message, printStartTime);
            
            return result;
        }
    }

    private async void OnPrintJobCompleted(object? sender, PrintJobCompletedEventArgs e)
    {
        if (!IsOnlineOrderJob(e.JobType) || string.IsNullOrWhiteSpace(e.OrderId))
        {
            return;
        }

        try
        {
            if (!await AreAllOrderPrintJobsCompletedAsync(e.OrderId))
            {
                return;
            }

            var durationMs = await GetOrderPrintDurationMsAsync(e.OrderId);
            var printStartedAt = await GetOrderPrintStartedAtAsync(e.OrderId);
            var printerInfo = await GetOrderPrinterInfoAsync(e.OrderId);

            await SendPrintAckAsync(e.OrderId, "printed", null, printStartedAt ?? DateTime.UtcNow, durationMs, printerInfo);
            await UpdateLocalOrderPrintStatusAsync(e.OrderId, "printed");

            Debug.WriteLine($" Physical print complete for OrderWeb order {e.OrderId}; printed ACK sent");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error handling completed print job #{e.JobId}: {ex.Message}");
        }
    }

    private async void OnPrintJobFailed(object? sender, PrintJobFailedEventArgs e)
    {
        if (!IsOnlineOrderJob(e.JobType) || string.IsNullOrWhiteSpace(e.OrderId))
        {
            return;
        }

        try
        {
            var reason = string.IsNullOrWhiteSpace(e.PrinterName)
                ? e.ErrorMessage
                : $"{e.PrinterName}: {e.ErrorMessage}";

            var printStartedAt = await GetOrderPrintStartedAtAsync(e.OrderId);
            var printerInfo = await GetOrderPrinterInfoAsync(e.OrderId);

            await SendPrintAckAsync(e.OrderId, "failed", reason, printStartedAt ?? DateTime.UtcNow, null, printerInfo);
            await UpdateLocalOrderPrintStatusAsync(e.OrderId, "failed", reason);

            Debug.WriteLine($" Physical print failed for OrderWeb order {e.OrderId}; failed ACK sent");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error handling failed print job #{e.JobId}: {ex.Message}");
        }
    }

    private static bool IsOnlineOrderJob(string? jobType)
    {
        return string.Equals(jobType, "online_receipt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jobType, "takeaway_ticket", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> AreAllOrderPrintJobsCompletedAsync(string orderId)
    {
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                SUM(CASE WHEN latest.status = 'completed' THEN 1 ELSE 0 END) AS completed_count,
                COUNT(*) AS required_count
            FROM (
                SELECT q.job_type, q.status
                FROM network_print_queue q
                INNER JOIN (
                    SELECT job_type, MAX(id) AS latest_id
                    FROM network_print_queue
                    WHERE order_id = @orderId
                      AND job_type IN ('online_receipt', 'takeaway_ticket')
                    GROUP BY job_type
                ) latest_jobs ON latest_jobs.latest_id = q.id
            ) latest";
        command.Parameters.AddWithValue("@orderId", orderId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return false;
        }

        var completedCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
        var requiredCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));

        return requiredCount == 2 && completedCount == 2;
    }

    private async Task<int?> GetOrderPrintDurationMsAsync(string orderId)
    {
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT TIMESTAMPDIFF(MICROSECOND, MIN(started_at), MAX(completed_at)) / 1000
            FROM network_print_queue
            WHERE order_id = @orderId
              AND job_type IN ('online_receipt', 'takeaway_ticket')
              AND started_at IS NOT NULL
              AND completed_at IS NOT NULL";
        command.Parameters.AddWithValue("@orderId", orderId);

        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private async Task<DateTime?> GetOrderPrintStartedAtAsync(string orderId)
    {
        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT MIN(started_at)
            FROM network_print_queue
            WHERE order_id = @orderId
              AND job_type IN ('online_receipt', 'takeaway_ticket')
              AND started_at IS NOT NULL";
        command.Parameters.AddWithValue("@orderId", orderId);

        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToDateTime(result);
    }

    private async Task<Dictionary<string, object>> GetOrderPrinterInfoAsync(string orderId)
    {
        var printers = new List<Dictionary<string, object>>();

        using var connection = await _databaseService.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT q.job_type, q.status, q.retry_count, p.name, p.ip_address, p.port
            FROM network_print_queue q
            JOIN network_printers p ON p.id = q.printer_id
            INNER JOIN (
                SELECT job_type, MAX(id) AS latest_id
                FROM network_print_queue
                WHERE order_id = @orderId
                  AND job_type IN ('online_receipt', 'takeaway_ticket')
                GROUP BY job_type
            ) latest_jobs ON latest_jobs.latest_id = q.id
            ORDER BY q.job_type";
        command.Parameters.AddWithValue("@orderId", orderId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            printers.Add(new Dictionary<string, object>
            {
                ["job_type"] = reader["job_type"]?.ToString() ?? "",
                ["status"] = reader["status"]?.ToString() ?? "",
                ["retry_count"] = Convert.ToInt32(reader["retry_count"]),
                ["printer_name"] = reader["name"]?.ToString() ?? "",
                ["ip_address"] = reader["ip_address"]?.ToString() ?? "",
                ["port"] = Convert.ToInt32(reader["port"])
            });
        }

        return new Dictionary<string, object>
        {
            ["printers"] = printers
        };
    }

    private async Task UpdateLocalOrderPrintStatusAsync(string orderId, string status, string? error = null)
    {
        using var connection = await _databaseService.GetConnectionAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE orders
            SET print_status = @status,
                printed_at = CASE WHEN @status = 'printed' THEN NOW() ELSE printed_at END,
                print_error = @error
            WHERE order_id = @orderId OR cloud_order_id = @orderId";
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@orderId", orderId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get the designated printer for a specific type (first enabled Online or Takeaway printer)
    /// </summary>
    private async Task<NetworkPrinter?> GetDesignatedPrinterAsync(NetworkPrinterType type)
    {
        try
        {
            var routingService = ServiceHelper.GetService<PrinterRoutingService>();
            if (routingService != null)
            {
                return await routingService.ResolvePrinterAsync(type);
            }

            var printers = await _printerDbService.GetPrintersByTypeAsync(type);
            // Return first enabled printer of this type
            return printers.FirstOrDefault(p => p.IsEnabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting {type} printer: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Print customer receipt to Online printer
    /// </summary>
    private async Task<(bool Success, string? JobId, string? Error, int Attempts)> PrintCustomerReceiptAsync(
        CloudOrderResponse order, NetworkPrinter printer)
    {
        try
        {
            // Build ESC/POS receipt data
            var printData = await CollectionReceiptTemplateService.BuildOnlineReceiptAsync(order, printer);
            
            // Enqueue the print job
            var jobId = await _printQueueService.EnqueueAsync(
                printer.Id,
                printData,
                "online_receipt",
                order.Id
            );

            return (true, jobId.ToString(), null, 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error printing customer receipt: {ex.Message}");
            return (false, null, ex.Message, 1);
        }
    }

    /// <summary>
    /// Print kitchen ticket to Takeaway printer
    /// </summary>
    private async Task<(bool Success, string? JobId, string? Error, int Attempts)> PrintKitchenTicketAsync(
        CloudOrderResponse order, NetworkPrinter printer)
    {
        try
        {
            // Build ESC/POS kitchen ticket data
            var templateSettings = await _kitchenTemplateSettingsService.GetSettingsAsync();
            var printData = BuildKitchenTicket(order, printer, templateSettings);
            
            // Enqueue the print job
            var jobId = await _printQueueService.EnqueueAsync(
                printer.Id,
                printData,
                "takeaway_ticket",
                order.Id
            );

            return (true, jobId.ToString(), null, 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error printing kitchen ticket: {ex.Message}");
            return (false, null, ex.Message, 1);
        }
    }

    /// <summary>
    /// Stickers follow a queued website kitchen ticket. A missing printer, a
    /// dish with no menu label, or a second arrival must not change the ticket or the OrderWeb ACK.
    /// </summary>
    private async Task<int> TryQueueWebsiteLabelsAsync(CloudOrderResponse order, string? staffReprintKey)
    {
        try
        {
            if (!LabelPrintRules.Decide(order.OrderType, "web").ShouldPrint)
                return 0;

            var labels = ServiceHelper.GetService<ToshibaOrderLabelService>();
            var labelDb = ServiceHelper.GetService<LabelPrintDatabaseService>();
            if (labels == null)
                return 0;

            var staffReprint = !string.IsNullOrWhiteSpace(staffReprintKey);
            if (!staffReprint && labelDb != null && await labelDb.HasJobsForSourceRequestPrefixAsync("orderweb", $"{order.Id}|"))
            {
                Debug.WriteLine($"Website labels already queued for {order.OrderNumber}; not queueing again");
                return 0;
            }

            var printOrder = BuildWebsiteLabelOrder(order);
            if (printOrder.Items.Count == 0)
                return 0;

            var printedIds = printOrder.Items.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = await labels.EnqueueForPrintedItemsAsync(
                printOrder,
                printedIds,
                "orderweb",
                order.OrderType,
                "web",
                staffReprint ? staffReprintKey : null);
            if (!result.HandledByToshiba)
            {
                Debug.WriteLine($"Website labels skipped for {order.OrderNumber}: no label printer");
                return 0;
            }

            if (result.Failed > 0)
                Debug.WriteLine($"Website labels need attention for {order.OrderNumber}: {string.Join("; ", result.Errors)}");
            else if (result.Queued > 0)
                Debug.WriteLine($"Website labels queued for {order.OrderNumber}: {result.Queued}");
            return result.Queued;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Website labels skipped for {order.OrderNumber}: {ex.Message}");
            return 0;
        }
    }

    private static TableOrder BuildWebsiteLabelOrder(CloudOrderResponse order)
    {
        var printOrder = new TableOrder
        {
            Id = order.Id,
            OrderNumber = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber.Trim(),
            Notes = order.SpecialInstructions
        };

        var index = 0;
        foreach (var item in order.Items ?? new List<CloudOrderItem>())
        {
            index++;
            var name = !string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.DisplayName
                : item.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.SpecialInstructions))
                notes.Add(item.SpecialInstructions.Trim());
            if (item.SelectedAddons != null)
            {
                notes.AddRange(item.SelectedAddons
                    .Select(addon => addon.Name?.Trim())
                    .Where(addonName => !string.IsNullOrWhiteSpace(addonName))!);
            }

            printOrder.Items.Add(new TableOrderItem
            {
                Id = !string.IsNullOrWhiteSpace(item.Id) ? item.Id.Trim() : $"{item.MenuItemId}:{index}",
                OrderId = order.Id,
                MenuItemId = item.MenuItemId ?? string.Empty,
                Name = name.Trim(),
                DisplayName = item.DisplayName,
                Quantity = Math.Max(0, item.Quantity),
                Notes = notes.Count == 0 ? null : string.Join("; ", notes),
                KitchenAction = KitchenChangeAction.New,
                SendStatus = ItemSendStatus.NotSent
            });
        }

        return printOrder;
    }

    /// <summary>
    /// Build ESC/POS data for kitchen ticket
    /// </summary>
    private byte[] BuildKitchenTicket(
        CloudOrderResponse order,
        NetworkPrinter printer,
        KitchenTemplateSettings templateSettings)
    {
        return KitchenTicketTemplateService.BuildOnlineKitchenTicket(order, printer, templateSettings);
    }

    /// <summary>
    /// Send print acknowledgment to OrderWeb.net
    /// </summary>
    private async Task SendPrintAckAsync(
        string orderId,
        string status,
        string? errorReason,
        DateTime printStartTime,
        int? durationMs = null,
        Dictionary<string, object>? printerInfo = null)
    {
        try
        {
            await _cloudOrderService.SendPrintAcknowledgmentAsync(
                orderId,
                status,
                errorReason,
                durationMs,
                printStartTime,
                printerInfo
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending print ACK: {ex.Message}");
            // ACK will be retried by CloudOrderService's retry mechanism
        }
    }

    /// <summary>
    /// Raise the PrintFailed event for UI notification
    /// </summary>
    private void RaisePrintFailedEvent(CloudOrderResponse order, string printerType, string error, int attempts)
    {
        PrintFailed?.Invoke(this, new PrintFailureEventArgs
        {
            OrderNumber = order.OrderNumber,
            OrderId = order.Id,
            PrinterType = printerType,
            ErrorMessage = error,
            AttemptsMade = attempts
        });
    }

    /// <summary>
    /// Check if auto-print is enabled in configuration
    /// </summary>
    public async Task<bool> IsAutoPrintEnabledAsync()
    {
        try
        {
            var config = await _databaseService.GetCloudConfigAsync();
            return config.GetValueOrDefault("auto_print_enabled", "True") == "True";
        }
        catch
        {
            return true; // Default to enabled
        }
    }
}
