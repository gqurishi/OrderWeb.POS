using System.Text;
using POS_in_NET.Models;
#if WINDOWS
using Windows.Graphics.Imaging;
using Windows.Storage;
#endif

namespace POS_in_NET.Services;

public static class CollectionReceiptTemplateService
{
    private const int LineWidth = 48;

    public static async Task<byte[]> BuildLocalReceiptAsync(
        TableOrder order,
        BusinessInfo? businessInfo,
        NetworkPrinter printer,
        CustomerReceiptKind receiptKind,
        string orderReference,
        string customerName,
        string customerPhone,
        string? deliveryAddress,
        string? customerNote,
        decimal total,
        decimal tip)
    {
        var builder = new EscPosBuilder(printer.Brand, PaperWidth.Mm80).Initialize();

        await TryPrintLogoAsync(builder, businessInfo);
        PrintHeader(builder, businessInfo);
        PrintOrderBlock(builder, receiptKind, order, orderReference, DateTime.Now, customerName, customerPhone, deliveryAddress);
        PrintItems(builder, order.Items.Where(item => !item.IsVoided));
        PrintTotals(builder, order, receiptKind, total, tip);

        if (receiptKind == CustomerReceiptKind.TablePayment)
        {
            PrintPaidPaymentBreakdown(builder, order.Payments);
        }
        else if (receiptKind is CustomerReceiptKind.Collection or CustomerReceiptKind.Delivery)
        {
            PrintPayment(builder, order.Payments);
        }

        if (receiptKind == CustomerReceiptKind.Delivery)
        {
            PrintCustomerNote(builder, customerNote);
        }

        PrintFooter(builder);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildLocalReceipt(
        TableOrder order,
        BusinessInfo? businessInfo,
        NetworkPrinter printer,
        string orderReference,
        string customerName,
        string customerPhone,
        decimal total,
        decimal tip)
    {
        return BuildLocalReceiptAsync(
                order,
                businessInfo,
                printer,
                CustomerReceiptKind.Collection,
                orderReference,
                customerName,
                customerPhone,
                null,
                null,
                total,
                tip)
            .GetAwaiter()
            .GetResult();
    }

    public static string BuildPreview(CustomerReceiptKind receiptKind = CustomerReceiptKind.Collection)
    {
        var builder = new StringBuilder();
        var isDelivery = receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = receiptKind == CustomerReceiptKind.TablePayment;
        var isTableReceipt = isTableBill || isTablePayment;

        builder.AppendLine(Center("[LOGO]"));
        builder.AppendLine();
        builder.AppendLine(Center("RESTAURANT NAME"));
        builder.AppendLine(Center("123 High Street"));
        builder.AppendLine(Center("London AB1 2CD"));
        builder.AppendLine(Center("Tel: 01234 567890"));
        builder.AppendLine(Center("VAT No: GB123456789"));
        builder.AppendLine();
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine(Center(GetReceiptTitle(receiptKind)));
        if (isTablePayment)
        {
            builder.AppendLine(Center("PAID"));
        }
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine("Order #: 1042");
        builder.AppendLine("Date: 06 Jul 2026                    Time: 14:25");
        builder.AppendLine();
        if (isTableReceipt)
        {
            builder.AppendLine("Table: 12");
            builder.AppendLine("Covers: 4");
        }
        else
        {
            builder.AppendLine("Customer: Sara Young");
            builder.AppendLine("Phone: 07123 456789");
        }
        if (isDelivery)
        {
            builder.AppendLine();
            builder.AppendLine("Delivery Address:");
            builder.AppendLine("10 Spring Street");
            builder.AppendLine("Flat 5B");
            builder.AppendLine("London");
            builder.AppendLine("AB1 2CD");
        }
        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine("2x Chicken Pakora                         \u00A38.50");
        builder.AppendLine("   No salad");
        builder.AppendLine("   Extra sauce");
        builder.AppendLine();
        builder.AppendLine("1x Lamb Curry                            \u00A311.95");
        builder.AppendLine("   Medium hot");
        builder.AppendLine();
        builder.AppendLine("2x Pilau Rice                             \u00A37.00");
        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine("Subtotal:                                \u00A327.45");
        if (isDelivery)
        {
            builder.AppendLine("Delivery/Fee:                             \u00A32.50");
        }
        else if (isTablePayment)
        {
            builder.AppendLine("Service Charge:                           \u00A32.75");
        }
        builder.AppendLine("Discount:                                -\u00A32.00");
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine(isDelivery
            ? "TOTAL:                                   \u00A327.95"
            : isTablePayment
                ? "TOTAL:                                   \u00A328.20"
            : "TOTAL:                                   \u00A325.45");
        if (isTableBill)
        {
            builder.AppendLine("Service charge not included");
        }
        builder.AppendLine();
        if (isTablePayment)
        {
            builder.AppendLine("Payment");
            builder.AppendLine("Cash:                                    \u00A310.00");
            builder.AppendLine("Card:                                    \u00A318.20");
            builder.AppendLine("----------------------------------------");
            builder.AppendLine("Paid Total:                              \u00A328.20");
            builder.AppendLine("Status: PAID");
        }
        else if (!isTableBill)
        {
            builder.AppendLine(isDelivery ? "Payment: Card" : "Payment: Cash");
        }
        if (isDelivery)
        {
            builder.AppendLine(new string('-', LineWidth));
            builder.AppendLine("Customer Note:");
            builder.AppendLine("Please call when outside.");
        }
        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine(Center("Thank you for your order"));
        builder.AppendLine(new string('=', LineWidth));

        return builder.ToString();
    }

    private static async Task TryPrintLogoAsync(EscPosBuilder builder, BusinessInfo? businessInfo)
    {
#if WINDOWS
        try
        {
            if (string.IsNullOrWhiteSpace(businessInfo?.LogoPath) || !File.Exists(businessInfo.LogoPath))
            {
                return;
            }

            var image = await DecodeLogoAsync(businessInfo.LogoPath);
            if (image == null)
            {
                return;
            }

            builder.SetAlign(TextAlign.Center)
                   .PrintRasterImage(image.Value.RasterData, image.Value.Width, image.Value.Height)
                   .FeedLines(1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Receipt logo print skipped: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

#if WINDOWS
    private static async Task<LogoRaster?> DecodeLogoAsync(string path)
    {
        var storageFile = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await storageFile.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        var originalWidth = Math.Max(1, (int)decoder.PixelWidth);
        var originalHeight = Math.Max(1, (int)decoder.PixelHeight);
        var targetWidth = Math.Min(384, originalWidth);
        var targetHeight = Math.Max(1, (int)Math.Round(originalHeight * (targetWidth / (double)originalWidth)));

        if (targetHeight > 160)
        {
            targetHeight = 160;
            targetWidth = Math.Max(1, (int)Math.Round(originalWidth * (targetHeight / (double)originalHeight)));
            targetWidth = Math.Min(384, targetWidth);
        }

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)targetWidth,
            ScaledHeight = (uint)targetHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return PackLogoPixels(pixelData.DetachPixelData(), targetWidth, targetHeight);
    }

    private static LogoRaster PackLogoPixels(byte[] pixels, int width, int height)
    {
        var widthBytes = (width + 7) / 8;
        var raster = new byte[widthBytes * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 4;
                var blue = pixels[index];
                var green = pixels[index + 1];
                var red = pixels[index + 2];
                var alpha = pixels[index + 3] / 255d;

                var blendedRed = (red * alpha) + (255 * (1 - alpha));
                var blendedGreen = (green * alpha) + (255 * (1 - alpha));
                var blendedBlue = (blue * alpha) + (255 * (1 - alpha));
                var luminance = (0.299 * blendedRed) + (0.587 * blendedGreen) + (0.114 * blendedBlue);

                if (luminance < 180)
                {
                    raster[(y * widthBytes) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return new LogoRaster(width, height, raster);
    }

    private readonly record struct LogoRaster(int Width, int Height, byte[] RasterData);
#endif

    private static void PrintHeader(EscPosBuilder builder, BusinessInfo? businessInfo)
    {
        var restaurantName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
            ? "RESTAURANT"
            : businessInfo.RestaurantName.Trim().ToUpperInvariant();

        builder.SetAlign(TextAlign.Center)
               .SetBold(true)
               .SetFontSize(2, 1)
               .PrintLine(restaurantName)
               .SetNormalSize()
               .SetBold(false);

        foreach (var line in BuildBusinessLines(businessInfo))
        {
            PrintCenteredWrapped(builder, line);
        }

        builder.FeedLines(1);
    }

    private static IEnumerable<string> BuildBusinessLines(BusinessInfo? businessInfo)
    {
        if (businessInfo == null)
        {
            yield break;
        }

        foreach (var line in SplitAddressLines(businessInfo.Address))
        {
            yield return line;
        }

        var cityLine = string.Join(" ", new[]
            {
                businessInfo.City,
                businessInfo.County,
                businessInfo.Postcode
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));

        if (!string.IsNullOrWhiteSpace(cityLine))
        {
            yield return cityLine;
        }

        if (!string.IsNullOrWhiteSpace(businessInfo.PhoneNumber))
        {
            yield return $"Tel: {businessInfo.PhoneNumber.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(businessInfo.VATNumber))
        {
            yield return $"VAT No: {businessInfo.VATNumber.Trim()}";
        }
    }

    private static void PrintOrderBlock(
        EscPosBuilder builder,
        CustomerReceiptKind receiptKind,
        TableOrder order,
        string orderReference,
        DateTime printedAt,
        string customerName,
        string customerPhone,
        string? deliveryAddress)
    {
        var title = GetReceiptTitle(receiptKind);

        builder.SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth))
               .SetAlign(TextAlign.Center)
               .SetBold(true)
               .SetFontSize(2, 1)
               .PrintLine(title)
               .SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth));

        if (receiptKind == CustomerReceiptKind.TablePayment)
        {
            builder.SetAlign(TextAlign.Center)
                   .SetBold(true)
                   .SetFontSize(2, 1)
                   .PrintLine("PAID")
                   .SetNormalSize()
                   .SetBold(false)
                   .SetAlign(TextAlign.Left);
        }

        PrintWrapped(builder, $"Order #: {orderReference}");
        builder.PrintColumns($"Date: {printedAt:dd MMM yyyy}", $"Time: {printedAt:HH:mm}");
        builder.FeedLines(1);

        if (receiptKind is CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
        {
            PrintWrapped(builder, order.TableNumber > 0 ? $"Table: {order.TableNumber}" : "Table");
            if (order.CoverCount > 0)
            {
                PrintWrapped(builder, $"Covers: {order.CoverCount}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(customerName))
        {
            PrintWrapped(builder, $"Customer: {customerName.Trim()}");
        }

        if (receiptKind is not (CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
            && !string.IsNullOrWhiteSpace(customerPhone))
        {
            PrintWrapped(builder, $"Phone: {customerPhone.Trim()}");
        }

        if (receiptKind == CustomerReceiptKind.Delivery && !string.IsNullOrWhiteSpace(deliveryAddress))
        {
            builder.FeedLines(1)
                   .SetBold(true)
                   .PrintLine("Delivery Address:")
                   .SetBold(false);

            foreach (var line in SplitAddressLines(deliveryAddress))
            {
                PrintWrapped(builder, line);
            }
        }

        builder.PrintLine(new string('-', LineWidth));
    }

    private static void PrintItems(EscPosBuilder builder, IEnumerable<TableOrderItem> items)
    {
        foreach (var item in items)
        {
            var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName;
            var itemTotal = item.TotalPriceWithVat > 0 ? item.TotalPriceWithVat : item.TotalPrice;
            PrintAmountLine(builder, $"{Math.Max(1, item.Quantity)}x {name}", itemTotal);

            if (!string.IsNullOrWhiteSpace(item.Modifiers))
            {
                foreach (var modifier in SplitDetails(item.Modifiers))
                {
                    PrintWrapped(builder, modifier, 3);
                }
            }

            foreach (var addon in item.SelectedAddons)
            {
                if (addon.Price > 0)
                {
                    PrintAmountLine(builder, $"   + {addon.Name}", addon.Price * item.Quantity);
                }
                else
                {
                    PrintWrapped(builder, $"+ {addon.Name}", 3);
                }
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                PrintWrapped(builder, item.Notes, 3);
            }

            builder.FeedLines(1);
        }
    }

    private static void PrintTotals(EscPosBuilder builder, TableOrder order, CustomerReceiptKind receiptKind, decimal total, decimal tip)
    {
        builder.PrintLine(new string('-', LineWidth));
        PrintAmountLine(builder, "Subtotal:", order.Subtotal);

        if (order.Discount > 0)
        {
            PrintAmountLine(builder, "Discount:", -order.Discount);
        }

        if (order.ServiceCharge > 0)
        {
            var serviceLabel = receiptKind switch
            {
                CustomerReceiptKind.Delivery => "Delivery/Fee:",
                CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment => "Service Charge:",
                _ => "Fee:"
            };
            PrintAmountLine(builder, serviceLabel, order.ServiceCharge);
        }

        if (tip > 0)
        {
            PrintAmountLine(builder, "Tip:", tip);
        }

        builder.PrintLine(new string('=', LineWidth))
               .SetBold(true)
               .SetFontSize(2, 1);
        PrintAmountLine(builder, "TOTAL:", total);
        builder.SetNormalSize()
               .SetBold(false);

        if (receiptKind is CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment
            && order.ServiceCharge <= 0)
        {
            PrintWrapped(builder, "Service charge not included");
        }

        builder.FeedLines(1);
    }

    private static void PrintPayment(EscPosBuilder builder, IEnumerable<TableOrderPayment> payments)
    {
        var paymentText = FormatPaymentMethods(payments);
        if (!string.IsNullOrWhiteSpace(paymentText))
        {
            PrintWrapped(builder, $"Payment: {paymentText}");
        }
    }

    private static void PrintPaidPaymentBreakdown(EscPosBuilder builder, IEnumerable<TableOrderPayment> payments)
    {
        var approvedPayments = payments
            .Where(payment => payment.Amount > 0)
            .ToList();

        if (approvedPayments.Count == 0)
        {
            return;
        }

        builder.PrintLine(new string('-', LineWidth))
               .SetBold(true)
               .PrintLine("Payment")
               .SetBold(false);

        foreach (var payment in approvedPayments)
        {
            PrintAmountLine(builder, $"{GetPaymentMethodLabel(payment.Method)}:", payment.Amount);
        }

        builder.PrintLine(new string('-', LineWidth));
        PrintAmountLine(builder, "Paid Total:", approvedPayments.Sum(payment => payment.Amount));
        PrintWrapped(builder, "Status: PAID");
    }

    private static void PrintCustomerNote(EscPosBuilder builder, string? customerNote)
    {
        if (string.IsNullOrWhiteSpace(customerNote))
        {
            return;
        }

        builder.PrintLine(new string('-', LineWidth))
               .SetBold(true)
               .PrintLine("Customer Note:")
               .SetBold(false);
        PrintWrapped(builder, customerNote.Trim());
    }

    private static void PrintFooter(EscPosBuilder builder)
    {
        builder.PrintLine(new string('-', LineWidth))
               .SetAlign(TextAlign.Center)
               .PrintLine("Thank you for your order")
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth))
               .FeedLines(3);
    }

    private static string FormatPaymentMethods(IEnumerable<TableOrderPayment> payments)
    {
        var methods = payments
            .Where(payment => payment.Amount > 0)
            .Select(payment => GetPaymentMethodLabel(payment.Method))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return methods.Count == 0 ? string.Empty : string.Join(" / ", methods);
    }

    private static string GetPaymentMethodLabel(PaymentMethodType method)
    {
        return method switch
        {
            PaymentMethodType.GiftCard => "Gift Card",
            PaymentMethodType.Card => "Card",
            _ => "Cash"
        };
    }

    private static string GetReceiptTitle(CustomerReceiptKind receiptKind)
    {
        return receiptKind switch
        {
            CustomerReceiptKind.Delivery => "DELIVERY",
            CustomerReceiptKind.TableBill => "TABLE BILL",
            CustomerReceiptKind.TablePayment => "PAYMENT RECEIPT",
            _ => "COLLECTION"
        };
    }

    private static void PrintAmountLine(EscPosBuilder builder, string label, decimal amount)
    {
        var amountText = FormatCurrency(amount);
        var maxLabelLength = LineWidth - amountText.Length - 1;

        if (label.Length <= maxLabelLength)
        {
            builder.PrintColumns(label, amountText);
            return;
        }

        foreach (var line in WrapText(label, maxLabelLength))
        {
            if (line == label || line.Length + amountText.Length + 1 > LineWidth)
            {
                builder.PrintLine(line);
            }
            else
            {
                builder.PrintColumns(line, amountText);
                amountText = string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(amountText))
        {
            builder.PrintColumns(string.Empty, amountText);
        }
    }

    private static void PrintCenteredWrapped(EscPosBuilder builder, string text)
    {
        foreach (var line in WrapText(text, LineWidth))
        {
            builder.SetAlign(TextAlign.Center).PrintLine(line);
        }
    }

    private static void PrintWrapped(EscPosBuilder builder, string? text, int indent = 0)
    {
        var prefix = new string(' ', Math.Clamp(indent, 0, LineWidth - 1));
        foreach (var line in WrapText(text, LineWidth - prefix.Length))
        {
            builder.PrintLine(prefix + line);
        }
    }

    private static IEnumerable<string> WrapText(string? text, int width)
    {
        width = Math.Max(8, width);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;

            foreach (var word in words)
            {
                if (word.Length > width)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        yield return line;
                        line = string.Empty;
                    }

                    for (var i = 0; i < word.Length; i += width)
                    {
                        yield return word.Substring(i, Math.Min(width, word.Length - i));
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(line))
                {
                    line = word;
                }
                else if (line.Length + 1 + word.Length <= width)
                {
                    line += " " + word;
                }
                else
                {
                    yield return line;
                    line = word;
                }
            }

            if (!string.IsNullOrEmpty(line))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> SplitAddressLines(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            yield break;
        }

        foreach (var line in address.Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line.Trim();
            }
        }
    }

    private static IEnumerable<string> SplitDetails(string details)
    {
        return details
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0);
    }

    private static string FormatCurrency(decimal amount)
    {
        var sign = amount < 0 ? "-" : string.Empty;
        return $"{sign}\u00A3{Math.Abs(amount):F2}";
    }

    private static string Center(string text)
    {
        if (text.Length >= LineWidth)
        {
            return text;
        }

        return new string(' ', (LineWidth - text.Length) / 2) + text;
    }
}

public enum CustomerReceiptKind
{
    Collection,
    Delivery,
    TableBill,
    TablePayment
}
