using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using System.Text;

namespace POS_in_NET.Services;

public class ReceiptService
{
    private readonly BusinessSettingsService _businessSettingsService;
    private readonly NetworkPrinterDatabaseService _printerDatabaseService;
    private readonly NetworkPrintQueueService _printQueueService;
    private CloudOrderService? _cloudOrderService;

    public ReceiptService(
        BusinessSettingsService businessSettingsService,
        NetworkPrinterDatabaseService printerDatabaseService,
        NetworkPrintQueueService printQueueService)
    {
        _businessSettingsService = businessSettingsService;
        _printerDatabaseService = printerDatabaseService;
        _printQueueService = printQueueService;
    }
    
    /// <summary>
    /// Set CloudOrderService for sending acknowledgments
    /// </summary>
    public void SetCloudOrderService(CloudOrderService cloudOrderService)
    {
        _cloudOrderService = cloudOrderService;
    }

    /// <summary>
    /// Generate receipt text for an order from Order model
    /// </summary>
    public async Task<string> GenerateReceiptTextAsync(Order order)
    {
        var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
        var receipt = new StringBuilder();

        // Header
        receipt.AppendLine("========================================");
        receipt.AppendLine($"    {businessInfo?.RestaurantName?.ToUpper() ?? "RESTAURANT"}");
        if (!string.IsNullOrEmpty(businessInfo?.Address))
            receipt.AppendLine($"    {businessInfo.Address}");
        if (!string.IsNullOrEmpty(businessInfo?.PhoneNumber))
            receipt.AppendLine($"    Tel: {businessInfo.PhoneNumber}");
        receipt.AppendLine("========================================");
        receipt.AppendLine();

        // Order Info
        receipt.AppendLine($"Order #: {order.OrderNumber ?? order.OrderId}");
        receipt.AppendLine($"Date: {order.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        receipt.AppendLine($"Customer: {order.CustomerName}");
        if (!string.IsNullOrEmpty(order.CustomerPhone))
            receipt.AppendLine($"Phone: {order.CustomerPhone}");
        if (!string.IsNullOrEmpty(order.OrderType))
            receipt.AppendLine($"Type: {order.OrderType}");
        receipt.AppendLine();

        // Items
        receipt.AppendLine("Items:");
        receipt.AppendLine("----------------------------------------");
        
        decimal itemsTotal = 0;
        foreach (var item in order.Items)
        {
            var itemPrice = item.ItemPrice ?? 0;
            var lineTotal = itemPrice * item.Quantity;
            itemsTotal += lineTotal;
            
            receipt.AppendLine($"{item.Quantity}x {item.ItemName}");
            if (itemPrice > 0)
                receipt.AppendLine($"    @ £{itemPrice:F2} each = £{lineTotal:F2}");
            
            // Add addons
            foreach (var addon in item.Addons)
            {
                var addonPrice = addon.AddonPrice ?? 0;
                var addonTotal = addonPrice * addon.Quantity;
                itemsTotal += addonTotal;
                
                receipt.AppendLine($"  + {addon.AddonName}");
                if (addonPrice > 0)
                    receipt.AppendLine($"    @ £{addonPrice:F2} = £{addonTotal:F2}");
            }
            
            if (!string.IsNullOrEmpty(item.SpecialInstructions))
                receipt.AppendLine($"    Note: {item.SpecialInstructions}");
            receipt.AppendLine();
        }

        // Totals
        receipt.AppendLine("----------------------------------------");
        receipt.AppendLine($"Subtotal:        £{order.SubtotalAmount:F2}");
        
        if (order.DeliveryFee > 0)
            receipt.AppendLine($"Delivery Fee:    £{order.DeliveryFee:F2}");
            
        receipt.AppendLine("----------------------------------------");
        receipt.AppendLine($"TOTAL:           £{order.TotalAmount:F2}");
        receipt.AppendLine("(VAT included in item prices)");

        // Payment & Special Instructions
        if (!string.IsNullOrEmpty(order.PaymentMethod))
        {
            receipt.AppendLine();
            var paymentMethod = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod);
            if (order.LocalLifecycleState == LocalLifecycleState.Paid || order.PaidAt.HasValue)
            {
                receipt.AppendLine("PAID");
                receipt.AppendLine($"Payment Method: {paymentMethod}");
            }
            else
            {
                receipt.AppendLine($"Payment Method: {paymentMethod}");
                receipt.AppendLine($"Amount Due: Â£{order.TotalAmount:F2}");
            }
        }

        if (!string.IsNullOrEmpty(order.SpecialInstructions))
        {
            receipt.AppendLine();
            receipt.AppendLine("Special Instructions:");
            receipt.AppendLine(order.SpecialInstructions);
        }

        // Footer
        receipt.AppendLine();
        receipt.AppendLine("========================================");
        receipt.AppendLine("    Thank you for your order!");
        receipt.AppendLine("========================================");

        return receipt.ToString();
    }

    /// <summary>
    /// Generate receipt text for an order from CloudOrderResponse
    /// </summary>
    public async Task<string> GenerateReceiptTextAsync(CloudOrderResponse order)
    {
        var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
        var receipt = new StringBuilder();

        // Header
        receipt.AppendLine("========================================");
        receipt.AppendLine($"    {businessInfo?.RestaurantName?.ToUpper() ?? "RESTAURANT"}");
        if (!string.IsNullOrEmpty(businessInfo?.Address))
            receipt.AppendLine($"    {businessInfo.Address}");
        if (!string.IsNullOrEmpty(businessInfo?.PhoneNumber))
            receipt.AppendLine($"    Tel: {businessInfo.PhoneNumber}");
        receipt.AppendLine("========================================");
        receipt.AppendLine();

        // Order Information
        receipt.AppendLine($"Order #: {order.OrderNumber}");
        receipt.AppendLine($"Date: {order.CreatedAt:dd/MM/yyyy HH:mm}");
        receipt.AppendLine($"Customer: {order.CustomerName}");
        if (!string.IsNullOrEmpty(order.CustomerPhone))
            receipt.AppendLine($"Phone: {order.CustomerPhone}");
        receipt.AppendLine($"Type: {order.OrderType?.ToUpper() ?? "UNKNOWN"}");
        if (!string.IsNullOrEmpty(order.Address) && order.Address != "Collection")
            receipt.AppendLine($"Address: {order.Address}");
        receipt.AppendLine("========================================");
        receipt.AppendLine();

        // Items
        foreach (var item in order.Items)
        {
            receipt.AppendLine($"{item.Quantity}x {(!string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : item.Name)}");
            
            // Show price if available
            if (item.Price.HasValue)
            {
                var itemTotal = item.GetTotalPrice();
                receipt.AppendLine($"    @ £{item.Price:F2} each = £{itemTotal:F2}");
            }
            else
            {
                receipt.AppendLine("    [Price pending API update]");
            }

            // Show addons
            foreach (var addon in item.SelectedAddons)
            {
                if (addon.Price.HasValue)
                {
                    receipt.AppendLine($"    + {addon.Name} (£{addon.Price:F2})");
                }
                else
                {
                    receipt.AppendLine($"    + {addon.Name}");
                }
            }

            // Special instructions for this item
            if (!string.IsNullOrEmpty(item.SpecialInstructions))
            {
                receipt.AppendLine($"    Note: {item.SpecialInstructions}");
            }
            
            receipt.AppendLine();
        }

        // Totals
        receipt.AppendLine("========================================");
        if (decimal.TryParse(order.Subtotal, out var subtotal))
            receipt.AppendLine($"Subtotal: £{subtotal:F2}");
        if (decimal.TryParse(order.DeliveryFee, out var delivery))
            receipt.AppendLine($"Delivery: £{delivery:F2}");
        if (decimal.TryParse(order.Total, out var total))
            receipt.AppendLine($"Total: £{total:F2}");
        receipt.AppendLine("(VAT included in item prices)");
        
        var paymentMethod = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod);
        if (OnlineOrderPaymentHelper.IsPaidFromSource(order.PaymentMethod, order.PaymentStatus))
        {
            receipt.AppendLine("PAID");
            receipt.AppendLine($"Payment: {paymentMethod}");
        }
        else
        {
            receipt.AppendLine($"Payment: {paymentMethod}");
            if (decimal.TryParse(order.Total, out var dueTotal))
                receipt.AppendLine($"Amount Due: Â£{dueTotal:F2}");
        }
        receipt.AppendLine("========================================");

        // Special Instructions
        if (!string.IsNullOrEmpty(order.SpecialInstructions))
        {
            receipt.AppendLine();
            receipt.AppendLine("Special Instructions:");
            receipt.AppendLine(order.SpecialInstructions);
            receipt.AppendLine("========================================");
        }

        // Footer
        receipt.AppendLine();
        receipt.AppendLine("Thank you for your order!");
        receipt.AppendLine();
        receipt.AppendLine($"Printed: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        
        return receipt.ToString();
    }

    /// <summary>
    /// Print receipt to system printer from Order model
    /// </summary>
    public async Task<bool> PrintReceiptAsync(Order order)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" Printing order: {order.OrderNumber}");
            
            // Update print status to "printing"
            order.PrintStatus = "printing";
            
            var receiptText = await GenerateReceiptTextAsync(order);
            
            // Print to system
            await PrintToSystemAsync(receiptText, order.OrderId);
            
            // Print successful - update status
            order.PrintStatus = "printed";
            order.PrintedAt = DateTime.UtcNow;
            order.PrintError = null;
            
            System.Diagnostics.Debug.WriteLine($" Print successful: {order.OrderNumber}");
            
            // Send ACK to cloud (NEW!)
            if (_cloudOrderService != null)
            {
                _ = _cloudOrderService.SendPrintAcknowledgmentAsync(order.OrderId, "printed");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Print failed: {order.OrderNumber} - {ex.Message}");
            
            // Print failed - update status
            order.PrintStatus = "failed";
            order.PrintError = ex.Message;
            
            // Send failure ACK (NEW!)
            if (_cloudOrderService != null)
            {
                _ = _cloudOrderService.SendPrintAcknowledgmentAsync(order.OrderId, "failed", ex.Message);
            }
            
            return false;
        }
    }

    /// <summary>
    /// Print receipt to system printer from CloudOrderResponse
    /// </summary>
    public async Task<bool> PrintReceiptAsync(CloudOrderResponse order)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($" Printing cloud order: {order.OrderNumber}");
            
            var receiptText = await GenerateReceiptTextAsync(order);
            
            // Print to system
            await PrintToSystemAsync(receiptText, order.Id);
            
            System.Diagnostics.Debug.WriteLine($" Print successful: {order.OrderNumber}");
            
            // Send ACK to cloud (NEW!)
            if (_cloudOrderService != null)
            {
                _ = _cloudOrderService.SendPrintAcknowledgmentAsync(order.OrderId, "printed");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Print failed: {order.OrderNumber} - {ex.Message}");
            
            // Send failure ACK (NEW!)
            if (_cloudOrderService != null)
            {
                _ = _cloudOrderService.SendPrintAcknowledgmentAsync(order.OrderId, "failed", ex.Message);
            }
            
            return false;
        }
    }

    public async Task<bool> PrintReceiptTextAsync(string receiptText, string? orderId = null)
    {
        if (string.IsNullOrWhiteSpace(receiptText))
        {
            return false;
        }

        try
        {
            await PrintToSystemAsync(receiptText, orderId);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Print text receipt failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Print to system default printer
    /// </summary>
    private async Task PrintToSystemAsync(string receiptText, string? orderId = null)
    {
        try
        {
            if (await TryQueueReceiptPrinterAsync(receiptText, orderId))
            {
                return;
            }

            // Platform-specific printing implementation
#if WINDOWS
            await PrintWindows(receiptText);
#elif MACCATALYST
            await PrintMacOS(receiptText);
#elif IOS
            await PrintiOS(receiptText);
#elif ANDROID
            await PrintAndroid(receiptText);
#else
            // Fallback - save to file for testing
            var path = Path.Combine(FileSystem.AppDataDirectory, $"receipt_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await File.WriteAllTextAsync(path, receiptText);
            System.Diagnostics.Debug.WriteLine($"Receipt saved to: {path}");
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"System printing failed: {ex.Message}");
            throw;
        }
    }

    private async Task<bool> TryQueueReceiptPrinterAsync(string receiptText, string? orderId)
    {
        try
        {
            await _printerDatabaseService.EnsureTablesExistAsync();
            await _printQueueService.EnsureTableExistsAsync();

            var routingService = ServiceHelper.GetService<PrinterRoutingService>();
            var receiptPrinter = routingService != null
                ? await routingService.ResolvePrinterAsync(NetworkPrinterType.Receipt, NetworkPrinterType.Online)
                : (await _printerDatabaseService.GetPrintersByTypeAsync(NetworkPrinterType.Receipt))
                    .FirstOrDefault(printer => printer.IsEnabled);

            if (routingService == null && receiptPrinter == null)
            {
                receiptPrinter = (await _printerDatabaseService.GetPrintersByTypeAsync(NetworkPrinterType.Online))
                    .FirstOrDefault(printer => printer.IsEnabled);
            }

            if (receiptPrinter == null)
            {
                return false;
            }

            var builder = new EscPosBuilder(receiptPrinter.Brand, receiptPrinter.PaperWidth)
                .Initialize()
                .SetAlign(TextAlign.Left);

            foreach (var line in receiptText.Replace("\r\n", "\n").Split('\n'))
            {
                builder.PrintLine(line);
            }

            builder.FeedLines(2);
            if (receiptPrinter.HasCutter)
            {
                builder.Cut(true);
            }

            var jobId = await _printQueueService.EnqueueAsync(
                receiptPrinter.Id,
                builder.Build(),
                "receipt",
                orderId);

            System.Diagnostics.Debug.WriteLine($" Receipt queued to {receiptPrinter.Name} as print job #{jobId}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shared receipt queue unavailable: {ex.Message}");
            return false;
        }
    }

#if WINDOWS
    private async Task PrintWindows(string receiptText)
    {
        // Windows printing implementation
        await Task.Run(() =>
        {
            // Use Windows printing API
            // For now, save to file
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                $"receipt_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, receiptText);
        });
    }
#endif

#if MACCATALYST
    private async Task PrintMacOS(string receiptText)
    {
        // macOS printing implementation
        await Task.Run(() =>
        {
            // Use macOS printing system
            // For now, save to Desktop
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                $"receipt_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, receiptText);
            
            // Open the file to trigger print dialog
            System.Diagnostics.Process.Start("open", $"\"{path}\"");
        });
    }
#endif

#if IOS
    private async Task PrintiOS(string receiptText)
    {
        // iOS printing implementation using UIPrintInteractionController
        await Task.CompletedTask;
    }
#endif

#if ANDROID
    private async Task PrintAndroid(string receiptText)
    {
        // Android printing implementation
        await Task.CompletedTask;
    }
#endif

    /// <summary>
    /// Get receipt preview text (for display in app)
    /// </summary>
    public async Task<string> GetReceiptPreviewAsync(CloudOrderResponse order)
    {
        return await GenerateReceiptTextAsync(order);
    }
}
