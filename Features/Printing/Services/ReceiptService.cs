using POS_in_NET.Models;
using POS_in_NET.Models.Api;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

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
        foreach (var item in order.Items.Where(item => !IsTastingMenuCourseItem(item.MenuItemId)))
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

        if (order.DiscountAmount > 0)
            receipt.AppendLine($"Discount:       -£{order.DiscountAmount:F2}");
        
        if (order.DeliveryFee > 0)
            receipt.AppendLine($"Delivery Fee:    £{order.DeliveryFee:F2}");

        if (string.Equals(order.ServiceChargeStatus, "applied", StringComparison.OrdinalIgnoreCase) && order.ServiceChargeAmount > 0)
            receipt.AppendLine($"Service Charge ({order.ServiceChargePercentage:0.##}%): £{order.ServiceChargeAmount:F2}");
        else if (string.Equals(order.ServiceChargeStatus, "removed", StringComparison.OrdinalIgnoreCase))
            receipt.AppendLine($"Service Charge ({order.ServiceChargePercentage:0.##}%): Removed");
        else if (string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase))
            receipt.AppendLine("Service charge not included");
            
        if (order.CashTipAmount > 0)
            receipt.AppendLine($"Cash Tip:        £{order.CashTipAmount:F2}");
        if (order.CardTipAmount > 0)
            receipt.AppendLine($"Card Tip:        £{order.CardTipAmount:F2}");
        if (order.TaxAmount > 0)
            receipt.AppendLine($"VAT:             £{order.TaxAmount:F2}");

        receipt.AppendLine("----------------------------------------");
        receipt.AppendLine($"TOTAL:           £{order.TotalAmount:F2}");
        receipt.AppendLine("(VAT included in item prices)");

        // Payment & Special Instructions
        receipt.AppendLine();
        var paymentMethod = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod);
        var isPaid = order.LocalLifecycleState == LocalLifecycleState.Paid
            || order.PaidAt.HasValue
            || OnlineOrderPaymentHelper.IsPaidFromSource(order.PaymentMethod, order.PaymentStatusRaw);
        receipt.AppendLine($"Payment: {paymentMethod}");
        var paymentStatus = isPaid ? "paid" : order.PaymentStatusRaw;
        receipt.AppendLine($"Status: {OnlineOrderPaymentHelper.GetStatusDisplay(order.PaymentMethod, paymentStatus)}");
        if (isPaid)
        {
            receipt.AppendLine($"Amount Paid: £{(order.AmountPaid ?? order.TotalAmount):F2}");
        }
        else
        {
            receipt.AppendLine($"Amount Due: £{Math.Max(0m, order.TotalAmount - (order.AmountPaid ?? 0m)):F2}");
        }
        AppendStoredPaymentDetails(
            receipt,
            order.PaymentMethod,
            order.PaymentProvider,
            order.TransactionId,
            order.VoucherCode,
            order.PromoCode,
            order.GiftCardNumberMasked,
            order.GiftCardRemainingBalance,
            order.LoyaltyPointsEarned,
            order.LoyaltyPointsRedeemed,
            order.LoyaltyPointsDiscount,
            order.LoyaltyBalanceAfter);

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
        foreach (var item in order.Items.Where(item => !IsTastingMenuCourseItem(item.MenuItemId)))
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
        if (decimal.TryParse(order.DiscountAmount, out var discount) && discount > 0)
            receipt.AppendLine($"Discount: -£{discount:F2}");
        if (decimal.TryParse(order.DeliveryFee, out var delivery))
            receipt.AppendLine($"Delivery: £{delivery:F2}");
        if (decimal.TryParse(order.ServiceChargeAmount, out var serviceCharge) && serviceCharge > 0)
            receipt.AppendLine($"Service Charge: £{serviceCharge:F2}");
        if (decimal.TryParse(order.CashTips, out var cashTip) && cashTip > 0)
            receipt.AppendLine($"Cash Tip: £{cashTip:F2}");
        if (decimal.TryParse(order.CardTips, out var cardTip) && cardTip > 0)
            receipt.AppendLine($"Card Tip: £{cardTip:F2}");
        if (decimal.TryParse(order.Tax, out var tax) && tax > 0)
            receipt.AppendLine($"VAT: £{tax:F2}");
        if (decimal.TryParse(order.Total, out var total))
            receipt.AppendLine($"Total: £{total:F2}");
        receipt.AppendLine("(VAT included in item prices)");
        
        var paymentMethod = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod);
        var cloudPaymentPaid = OnlineOrderPaymentHelper.IsPaidFromSource(order.PaymentMethod, order.PaymentStatus);
        receipt.AppendLine($"Payment: {paymentMethod}");
        receipt.AppendLine($"Status: {OnlineOrderPaymentHelper.GetStatusDisplay(order.PaymentMethod, order.PaymentStatus)}");
        var cloudAmountPaid = decimal.TryParse(order.AmountPaid, out var parsedAmountPaid) ? parsedAmountPaid : (decimal?)null;
        if (cloudPaymentPaid)
        {
            receipt.AppendLine($"Amount Paid: £{(cloudAmountPaid ?? total):F2}");
        }
        else
        {
            receipt.AppendLine($"Amount Due: £{Math.Max(0m, total - (cloudAmountPaid ?? 0m)):F2}");
        }
        AppendStoredPaymentDetails(
            receipt,
            order.PaymentMethod,
            order.PaymentProvider,
            order.PaymentReference,
            order.VoucherCode,
            order.PromoCode,
            order.GiftCard?.CardNumberMasked,
            decimal.TryParse(order.GiftCard?.RemainingBalance, out var giftBalance) ? giftBalance : null,
            order.Loyalty?.PointsEarned ?? 0,
            order.Loyalty?.PointsRedeemed ?? 0,
            decimal.TryParse(order.Loyalty?.PointsDiscount, out var loyaltyDiscount) ? loyaltyDiscount : 0m,
            order.Loyalty?.BalanceAfter);
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

    private static void AppendStoredPaymentDetails(
        StringBuilder receipt,
        string? paymentMethod,
        string? provider,
        string? reference,
        string? voucherCode,
        string? promoCode,
        string? giftCardNumberMasked,
        decimal? giftCardRemainingBalance,
        int loyaltyPointsEarned,
        int loyaltyPointsRedeemed,
        decimal loyaltyPointsDiscount,
        int? loyaltyBalanceAfter)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            receipt.AppendLine($"Provider: {provider.Trim()}");
        }

        var printableReference = OnlineOrderPaymentHelper.FormatReceiptReference(reference);
        if (printableReference != null)
        {
            receipt.AppendLine($"Reference: {printableReference}");
        }

        var maskedVoucher = OnlineOrderPaymentHelper.MaskVoucherCode(voucherCode);
        if (maskedVoucher != null)
        {
            var label = OnlineOrderPaymentHelper.NormalizeMethod(paymentMethod) == "gift_card" ? "Gift Card" : "Voucher";
            receipt.AppendLine($"{label}: {maskedVoucher}");
        }

        if (!string.IsNullOrWhiteSpace(promoCode)) receipt.AppendLine($"Promo Code: {promoCode.Trim()}");
        var safeGiftCardNumber = OnlineOrderPaymentHelper.MaskVoucherCode(giftCardNumberMasked);
        if (safeGiftCardNumber != null) receipt.AppendLine($"Gift Card: {safeGiftCardNumber}");
        if (giftCardRemainingBalance.HasValue) receipt.AppendLine($"Gift Card Balance: £{giftCardRemainingBalance.Value:F2}");
        if (loyaltyPointsEarned > 0) receipt.AppendLine($"Loyalty Earned: {loyaltyPointsEarned} points");
        if (loyaltyPointsRedeemed > 0) receipt.AppendLine($"Loyalty Redeemed: {loyaltyPointsRedeemed} points");
        if (loyaltyPointsDiscount > 0) receipt.AppendLine($"Loyalty Discount: £{loyaltyPointsDiscount:F2}");
        if (loyaltyBalanceAfter.HasValue) receipt.AppendLine($"Loyalty Balance: {loyaltyBalanceAfter.Value} points");
    }

    /// <summary>
    /// Reprint a saved order with the same configured customer-receipt template
    /// used by the live POS, including logo, business header and footer.
    /// </summary>
    public async Task<bool> PrintFullCustomerReceiptAsync(Order order)
    {
        try
        {
            await _printerDatabaseService.EnsureTablesExistAsync();
            await _printQueueService.EnsureTableExistsAsync();

            var routingService = ServiceHelper.GetService<PrinterRoutingService>();
            var receiptPrinter = routingService != null
                ? await routingService.ResolvePrinterAsync(
                    NetworkPrinterType.Receipt,
                    NetworkPrinterType.Online,
                    NetworkPrinterType.Takeaway,
                    NetworkPrinterType.Kitchen)
                : (await _printerDatabaseService.GetPrintersByTypeAsync(NetworkPrinterType.Receipt))
                    .FirstOrDefault(printer => printer.IsEnabled);

            if (receiptPrinter == null)
            {
                return false;
            }

            var printOrder = await BuildReceiptOrderAsync(order);
            var receiptKind = ResolveReceiptKind(order);
            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var receiptData = await CollectionReceiptTemplateService.BuildLocalReceiptAsync(
                printOrder,
                businessInfo,
                receiptPrinter,
                receiptKind,
                string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderId : order.OrderNumber,
                order.CustomerName,
                order.CustomerPhone ?? string.Empty,
                receiptKind == CustomerReceiptKind.Delivery ? order.CustomerAddress : null,
                receiptKind == CustomerReceiptKind.Delivery ? order.SpecialInstructions : null,
                order.TotalAmount,
                order.CashTipAmount + order.CardTipAmount);

            var jobId = await _printQueueService.EnqueueAsync(
                receiptPrinter.Id,
                receiptData,
                "customer_receipt_reprint",
                order.OrderId);

            System.Diagnostics.Debug.WriteLine($" Full customer receipt queued to {receiptPrinter.Name} as print job #{jobId}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Full customer receipt reprint failed: {ex.Message}");
            return false;
        }
    }

    private static CustomerReceiptKind ResolveReceiptKind(Order order)
    {
        var orderType = (order.OrderType ?? string.Empty).Trim().ToLowerInvariant();
        return orderType switch
        {
            "delivery" or "del" => CustomerReceiptKind.Delivery,
            "table" or "tbl" or "dine_in" or "dine-in" => CustomerReceiptKind.TablePayment,
            _ => CustomerReceiptKind.Collection
        };
    }

    private static bool IsTastingMenuCourseItem(string? menuItemId) =>
        (menuItemId ?? string.Empty).StartsWith("tasting-course:", StringComparison.OrdinalIgnoreCase);

    private static TableServiceChargeStatus ParseServiceChargeStatus(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "applied" => TableServiceChargeStatus.Applied,
            "removed" => TableServiceChargeStatus.Removed,
            _ => TableServiceChargeStatus.NotConfigured
        };
    }

    private async Task<TableOrder> BuildReceiptOrderAsync(Order order)
    {
        var isTableOrder = ResolveReceiptKind(order) == CustomerReceiptKind.TablePayment;
        var printOrder = new TableOrder
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            Notes = order.SpecialInstructions,
            OrderMode = isTableOrder ? "dine_in" : "takeaway",
            StartTime = order.CreatedAt,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Subtotal = order.SubtotalAmount,
            Discount = order.DiscountAmount,
            DeliveryFee = order.DeliveryFee,
            ServiceChargePercent = order.ServiceChargePercentage,
            ServiceChargeStatus = ParseServiceChargeStatus(order.ServiceChargeStatus),
            VAT = order.TaxAmount,
            Total = order.TotalAmount,
            TipAmount = order.CashTipAmount + order.CardTipAmount,
            DeclaredPaymentMethod = order.PaymentMethod,
            DeclaredPaymentStatus = order.PaymentStatusRaw,
            DeclaredAmountPaid = order.AmountPaid,
            DeclaredPaymentProvider = order.PaymentProvider,
            DeclaredPaymentReference = order.TransactionId,
            DeclaredVoucherCode = order.VoucherCode,
            DeclaredPromoCode = order.PromoCode,
            DeclaredGiftCardNumberMasked = order.GiftCardNumberMasked,
            DeclaredGiftCardRemainingBalance = order.GiftCardRemainingBalance,
            DeclaredLoyaltyPointsEarned = order.LoyaltyPointsEarned,
            DeclaredLoyaltyPointsRedeemed = order.LoyaltyPointsRedeemed,
            DeclaredLoyaltyPointsDiscount = order.LoyaltyPointsDiscount,
            DeclaredLoyaltyBalanceAfter = order.LoyaltyBalanceAfter,
            Status = order.LocalLifecycleState == LocalLifecycleState.Voided
                ? TableOrderStatus.Voided
                : TableOrderStatus.Paid
        };

        foreach (var item in order.Items.Where(item => !IsTastingMenuCourseItem(item.MenuItemId)))
        {
            var printItem = new TableOrderItem
            {
                Id = item.ClientItemId ?? item.Id.ToString(),
                OrderId = order.OrderId,
                MenuItemId = item.MenuItemId ?? string.Empty,
                VariantId = item.VariantId,
                VariantName = item.VariantName,
                DisplayName = item.DisplayName,
                Name = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.ItemPrice ?? 0m,
                TotalPriceWithVat = item.TotalPrice,
                Notes = item.SpecialInstructions,
                PrintGroupId = item.PrintGroupId,
                PrintInRed = item.PrintInRed,
                CourseType = item.CourseType,
                FiredAt = item.FiredAt,
                FiredBy = item.FiredBy
            };
            printItem.SelectedAddons = new ObservableCollection<SelectedAddon>(item.Addons.Select(addon => new SelectedAddon
            {
                Id = addon.AddonId ?? addon.Id.ToString(),
                Name = addon.AddonName,
                Price = addon.AddonPrice ?? 0m
            }));
            printOrder.Items.Add(printItem);
        }

        var orderService = ServiceHelper.GetService<OrderService>() ?? new OrderService();
        var approvedPayments = (await orderService.GetOrderPaymentsAsync(order.Id))
            .Where(payment => string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase)
                              && payment.Amount > 0m);
        foreach (var payment in approvedPayments)
        {
            printOrder.Payments.Add(new TableOrderPayment
            {
                Id = payment.Id.ToString(),
                OrderId = order.OrderId,
                Method = ParsePaymentMethod(payment.PaymentMethod),
                Amount = payment.Amount,
                AmountReceived = GetMetadataAmount(payment.MetadataJson, "amountReceived", payment.Amount),
                Change = GetMetadataAmount(payment.MetadataJson, "change", 0m),
                TipAmount = payment.TipAmount,
                Reference = payment.Reference,
                CreatedAt = payment.CreatedAt,
                StaffName = payment.CreatedBy ?? string.Empty
            });
        }

        if (isTableOrder && order.TableSessionId.HasValue)
        {
            var session = await new TableSessionService().GetSessionByIdAsync(order.TableSessionId.Value);
            if (session != null)
            {
                printOrder.CoverCount = Math.Max(1, session.PartySize);
                var table = await new RestaurantTableService().GetTableByIdAsync(session.TableId);
                if (table != null && int.TryParse(table.TableNumber, out var tableNumber))
                {
                    printOrder.TableNumber = tableNumber;
                }
            }
        }

        printOrder.Total = order.TotalAmount;
        printOrder.VAT = order.TaxAmount;
        return printOrder;
    }

    private static PaymentMethodType ParsePaymentMethod(string? value)
    {
        return (value ?? string.Empty).Trim().Replace("_", string.Empty).ToLowerInvariant() switch
        {
            "card" => PaymentMethodType.Card,
            "giftcard" => PaymentMethodType.GiftCard,
            _ => PaymentMethodType.Cash
        };
    }

    private static decimal GetMetadataAmount(string? metadataJson, string propertyName, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var amount)
                ? amount
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
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
