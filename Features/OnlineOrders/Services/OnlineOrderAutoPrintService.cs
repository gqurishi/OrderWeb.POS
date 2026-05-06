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
        DatabaseService databaseService)
    {
        _printerDbService = printerDbService;
        _printQueueService = printQueueService;
        _printerService = printerService;
        _escPosBuilder = escPosBuilder;
        _cloudOrderService = cloudOrderService;
        _databaseService = databaseService;
        
        Debug.WriteLine("OnlineOrderAutoPrintService initialized");
    }

    /// <summary>
    /// Auto-print an online order to designated printers.
    /// Prints customer receipt to Online printer and kitchen ticket to Takeaway printer.
    /// </summary>
    public async Task<AutoPrintResult> PrintOnlineOrderAsync(CloudOrderResponse order)
    {
        var result = new AutoPrintResult();
        var printStartTime = DateTime.UtcNow;
        
        try
        {
            Debug.WriteLine($"Auto-printing online order: {order.OrderNumber}");
            
            // Get designated printers
            var onlinePrinter = await GetDesignatedPrinterAsync(NetworkPrinterType.Online);
            var takeawayPrinter = await GetDesignatedPrinterAsync(NetworkPrinterType.Takeaway);
            
            if (onlinePrinter == null && takeawayPrinter == null)
            {
                result.ErrorMessage = "No Online or Takeaway printers configured";
                Debug.WriteLine($"No designated printers found for online orders");
                
                // Still send ACK as failed - no printers configured
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
                }
                else
                {
                    errors.Add($"Takeaway printer: {takeawayResult.Error}");
                    RaisePrintFailedEvent(order, "Takeaway", takeawayResult.Error ?? "Unknown error", takeawayResult.Attempts);
                }
            }

            // Determine overall result
            if (result.OnlinePrintJobId != null || result.TakeawayPrintJobId != null)
            {
                result.Success = true;
                PrintSucceeded?.Invoke(this, order.OrderNumber);
                
                // Send success ACK
                var printDuration = (int)(DateTime.UtcNow - printStartTime).TotalMilliseconds;
                await SendPrintAckAsync(order.Id, "printed", null, printStartTime, printDuration);
            }
            else
            {
                result.ErrorMessage = string.Join("; ", errors);
                
                // Send failure ACK
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

    /// <summary>
    /// Get the designated printer for a specific type (first enabled Online or Takeaway printer)
    /// </summary>
    private async Task<NetworkPrinter?> GetDesignatedPrinterAsync(NetworkPrinterType type)
    {
        try
        {
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
            var printData = BuildCustomerReceipt(order, printer);
            
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
            var printData = BuildKitchenTicket(order, printer);
            
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
    /// Build ESC/POS data for customer receipt
    /// </summary>
    private byte[] BuildCustomerReceipt(CloudOrderResponse order, NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        var lineWidth = printer.PaperWidth == PaperWidth.Mm80 ? 48 : 32;
        
        builder.Initialize();
        
        // Header - ONLINE ORDER
        builder.SetAlign(TextAlign.Center)
               .SetFontSize(2, 2)
               .SetBold(true)
               .PrintLine("ONLINE ORDER")
               .SetNormalSize()
               .SetBold(false)
               .FeedLines(1);

        // Order number and type
        builder.SetFontSize(2, 1)
               .SetBold(true)
               .PrintLine($"#{order.OrderNumber}")
               .SetNormalSize()
               .SetBold(false);

        // Order type badge
        var orderType = order.OrderType?.ToUpper() ?? "PICKUP";
        builder.PrintLine($"[ {orderType} ]")
               .FeedLines(1);

        // Separator
        builder.SetAlign(TextAlign.Left)
               .PrintLine(new string('=', lineWidth));

        // Date and time
        builder.PrintLine($"Date: {DateTime.Now:MMM dd, yyyy}")
               .PrintLine($"Time: {DateTime.Now:hh:mm tt}");

        // Scheduled time if exists
        if (order.ScheduledTime.HasValue)
        {
            builder.SetBold(true)
                   .PrintLine($"PICKUP: {order.ScheduledTime.Value:MMM dd hh:mm tt}")
                   .SetBold(false);
        }

        builder.PrintLine(new string('-', lineWidth));

        // Customer info
        if (!string.IsNullOrEmpty(order.CustomerName))
        {
            builder.PrintLine($"Customer: {order.CustomerName}");
        }
        if (!string.IsNullOrEmpty(order.CustomerPhone))
        {
            builder.PrintLine($"Phone: {order.CustomerPhone}");
        }
        if (!string.IsNullOrEmpty(order.Address) && orderType == "DELIVERY")
        {
            builder.PrintLine($"Address: {order.Address}");
        }

        builder.PrintLine(new string('=', lineWidth));

        // Items
        builder.SetBold(true)
               .PrintLine("ITEMS")
               .SetBold(false)
               .PrintLine(new string('-', lineWidth));

        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                // Item name with quantity and price
                var itemLine = $"{item.Quantity}x {item.Name}";
                var priceLine = $"${item.Price:F2}";
                
                builder.PrintColumns(itemLine, priceLine);

                // Modifiers/addons
                if (item.SelectedAddons != null && item.SelectedAddons.Any())
                {
                    foreach (var addon in item.SelectedAddons)
                    {
                        var addonText = $"   + {addon.Name}";
                        var addonPrice = addon.Price > 0 ? $"${addon.Price:F2}" : "";
                        builder.PrintColumns(addonText, addonPrice);
                    }
                }

                // Special instructions
                if (!string.IsNullOrEmpty(item.SpecialInstructions))
                {
                    builder.PrintLine($"   Note: {item.SpecialInstructions}");
                }
            }
        }

        builder.PrintLine(new string('-', lineWidth));

        // Totals
        decimal.TryParse(order.Subtotal, out var subtotal);
        decimal.TryParse(order.DeliveryFee, out var deliveryFee);
        decimal.TryParse(order.Total, out var total);

        builder.PrintColumns("Subtotal:", $"${subtotal:F2}");
        
        if (deliveryFee > 0)
            builder.PrintColumns("Delivery:", $"${deliveryFee:F2}");

        builder.PrintLine(new string('=', lineWidth))
               .SetFontSize(2, 1)
               .SetBold(true)
               .PrintColumns("TOTAL:", $"${total:F2}")
               .SetNormalSize()
               .SetBold(false)
               .PrintLine("(VAT included in item prices)");

        // Payment method
        builder.PrintLine(new string('-', lineWidth));
        var paymentMethod = order.PaymentMethod ?? "Online";
        builder.SetAlign(TextAlign.Center)
               .PrintLine($"Paid: {paymentMethod}")
               .FeedLines(1);

        // Special instructions for entire order
        if (!string.IsNullOrEmpty(order.SpecialInstructions))
        {
            builder.SetAlign(TextAlign.Left)
                   .PrintLine(new string('-', lineWidth))
                   .SetBold(true)
                   .PrintLine("ORDER NOTES:")
                   .SetBold(false)
                   .PrintLine(order.SpecialInstructions);
        }

        // Footer
        builder.FeedLines(1)
               .SetAlign(TextAlign.Center)
               .PrintLine("Thank you for your order!")
               .PrintLine("OrderWeb.net")
               .FeedLines(3);

        // Cut paper
        if (printer.HasCutter)
        {
            builder.Cut(true); // Partial cut
        }

        return builder.Build();
    }

    /// <summary>
    /// Build ESC/POS data for kitchen ticket
    /// </summary>
    private byte[] BuildKitchenTicket(CloudOrderResponse order, NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        var lineWidth = printer.PaperWidth == PaperWidth.Mm80 ? 48 : 32;
        
        builder.Initialize();

        // Sound buzzer for kitchen alert
        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        // Large header - ONLINE ORDER
        builder.SetAlign(TextAlign.Center)
               .SetFontSize(2, 2)
               .SetBold(true)
               .PrintLine("*** ONLINE ***")
               .SetNormalSize();

        // Order number - LARGE
        builder.SetFontSize(3, 3)
               .PrintLine($"#{order.OrderNumber}")
               .SetNormalSize()
               .SetBold(false)
               .FeedLines(1);

        // Order type
        var orderType = order.OrderType?.ToUpper() ?? "PICKUP";
        builder.SetFontSize(2, 1)
               .SetBold(true)
               .PrintLine($"[ {orderType} ]")
               .SetNormalSize()
               .SetBold(false);

        // Scheduled pickup time - prominent
        if (order.ScheduledTime.HasValue)
        {
            builder.SetFontSize(2, 2)
                   .SetBold(true)
                   .PrintLine($"PICKUP: {order.ScheduledTime.Value:MMM dd hh:mm tt}")
                   .SetNormalSize()
                   .SetBold(false);
        }

        builder.PrintLine(new string('=', lineWidth));

        // Time received
        builder.SetAlign(TextAlign.Left)
               .PrintLine($"Received: {DateTime.Now:hh:mm tt}");

        // Customer name for identification
        if (!string.IsNullOrEmpty(order.CustomerName))
        {
            builder.SetBold(true)
                   .PrintLine($"Customer: {order.CustomerName}")
                   .SetBold(false);
        }

        builder.PrintLine(new string('=', lineWidth));

        // Items - LARGE and clear for kitchen
        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                // Quantity prominently displayed
                builder.SetFontSize(2, 2)
                       .SetBold(true)
                       .PrintLine($"{item.Quantity}x {item.Name}")
                       .SetNormalSize()
                       .SetBold(false);

                // Modifiers
                if (item.SelectedAddons != null && item.SelectedAddons.Any())
                {
                    foreach (var addon in item.SelectedAddons)
                    {
                        builder.SetFontSize(1, 1)
                               .PrintLine($"    + {addon.Name}");
                    }
                }

                // Item special instructions
                if (!string.IsNullOrEmpty(item.SpecialInstructions))
                {
                    builder.SetBold(true)
                           .PrintLine($"    >> {item.SpecialInstructions}")
                           .SetBold(false);
                }

                builder.FeedLines(1);
            }
        }

        builder.PrintLine(new string('=', lineWidth));

        // Order special instructions - PROMINENT
        if (!string.IsNullOrEmpty(order.SpecialInstructions))
        {
            builder.SetFontSize(2, 1)
                   .SetBold(true)
                   .PrintLine("NOTES:")
                   .SetNormalSize()
                   .PrintLine(order.SpecialInstructions)
                   .SetBold(false);
        }

        // Footer
        builder.FeedLines(1)
               .SetAlign(TextAlign.Center)
               .PrintLine($"Order ID: {order.Id}")
               .FeedLines(3);

        // Cut paper
        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Send print acknowledgment to OrderWeb.net
    /// </summary>
    private async Task SendPrintAckAsync(string orderId, string status, string? errorReason, DateTime printStartTime, int? durationMs = null)
    {
        try
        {
            await _cloudOrderService.SendPrintAcknowledgmentAsync(
                orderId,
                status,
                errorReason,
                durationMs,
                printStartTime
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
