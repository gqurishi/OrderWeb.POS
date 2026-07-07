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
        var templateSettings = receiptKind switch
        {
            CustomerReceiptKind.Collection => await LoadCollectionSettingsAsync(),
            CustomerReceiptKind.Delivery => await LoadDeliverySettingsAsync(),
            CustomerReceiptKind.TableBill => await LoadTableBillSettingsAsync(),
            CustomerReceiptKind.TablePayment => await LoadTablePaymentSettingsAsync(),
            _ => CollectionReceiptTemplateSettings.Default()
        };

        await TryPrintLogoAsync(builder, businessInfo);
        PrintHeader(builder, businessInfo, templateSettings);
        PrintOrderBlock(builder, receiptKind, order, orderReference, DateTime.Now, customerName, customerPhone, deliveryAddress, templateSettings);
        PrintItems(builder, order.Items.Where(item => !item.IsVoided));
        PrintTotals(builder, order, receiptKind, total, tip, templateSettings);

        if (receiptKind == CustomerReceiptKind.TablePayment)
        {
            PrintPaidPaymentBreakdown(builder, order.Payments, templateSettings);
        }
        else if (receiptKind is CustomerReceiptKind.Collection or CustomerReceiptKind.Delivery)
        {
            PrintPayment(builder, order.Payments, templateSettings, receiptKind is CustomerReceiptKind.Collection or CustomerReceiptKind.Delivery);
        }

        if (receiptKind == CustomerReceiptKind.Delivery)
        {
            PrintCustomerNote(builder, customerNote);
        }

        PrintFooter(builder, templateSettings);

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

    private static async Task<CollectionReceiptTemplateSettings> LoadCollectionSettingsAsync()
    {
        var service = ServiceHelper.GetService<CollectionReceiptTemplateSettingsService>();
        if (service != null)
        {
            return await service.GetSettingsAsync();
        }

        return await new CollectionReceiptTemplateSettingsService(new DatabaseService()).GetSettingsAsync();
    }

    private static async Task<CollectionReceiptTemplateSettings> LoadDeliverySettingsAsync()
    {
        var service = ServiceHelper.GetService<DeliveryReceiptTemplateSettingsService>();
        if (service != null)
        {
            return await service.GetSettingsAsync();
        }

        return await new DeliveryReceiptTemplateSettingsService(new DatabaseService()).GetSettingsAsync();
    }

    private static async Task<CollectionReceiptTemplateSettings> LoadTableBillSettingsAsync()
    {
        var service = ServiceHelper.GetService<TableBillReceiptTemplateSettingsService>();
        if (service != null)
        {
            return await service.GetSettingsAsync();
        }

        return await new TableBillReceiptTemplateSettingsService(new DatabaseService()).GetSettingsAsync();
    }

    private static async Task<CollectionReceiptTemplateSettings> LoadTablePaymentSettingsAsync()
    {
        var service = ServiceHelper.GetService<TablePaymentReceiptTemplateSettingsService>();
        if (service != null)
        {
            return await service.GetSettingsAsync();
        }

        return await new TablePaymentReceiptTemplateSettingsService(new DatabaseService()).GetSettingsAsync();
    }

    private static void PrintHeader(
        EscPosBuilder builder,
        BusinessInfo? businessInfo,
        CollectionReceiptTemplateSettings settings)
    {
        var restaurantName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
            ? "RESTAURANT"
            : businessInfo.RestaurantName.Trim().ToUpperInvariant();

        PrintStyledCentered(builder, restaurantName, settings.BusinessNameSize, settings.BusinessNameBold);

        foreach (var line in BuildAddressLines(businessInfo))
        {
            PrintStyledCentered(builder, line, settings.AddressSize, settings.AddressBold);
        }

        var phoneLine = BuildPhoneLine(businessInfo);
        if (!string.IsNullOrWhiteSpace(phoneLine))
        {
            PrintStyledCentered(builder, phoneLine, settings.PhoneSize, settings.PhoneBold);
        }

        var vatLine = BuildVatLine(businessInfo);
        if (!string.IsNullOrWhiteSpace(vatLine))
        {
            PrintStyledCentered(builder, vatLine, settings.VatSize, settings.VatBold);
        }

        builder.FeedLines(1);
    }

    private static IEnumerable<string> BuildAddressLines(BusinessInfo? businessInfo)
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
    }

    private static string BuildPhoneLine(BusinessInfo? businessInfo) =>
        string.IsNullOrWhiteSpace(businessInfo?.PhoneNumber)
            ? string.Empty
            : $"Tel: {businessInfo.PhoneNumber.Trim()}";

    private static string BuildVatLine(BusinessInfo? businessInfo) =>
        string.IsNullOrWhiteSpace(businessInfo?.VATNumber)
            ? string.Empty
            : $"VAT No: {businessInfo.VATNumber.Trim()}";

    private static void PrintOrderBlock(
        EscPosBuilder builder,
        CustomerReceiptKind receiptKind,
        TableOrder order,
        string orderReference,
        DateTime printedAt,
        string customerName,
        string customerPhone,
        string? deliveryAddress,
        CollectionReceiptTemplateSettings settings)
    {
        var title = GetReceiptTitle(receiptKind);

        builder.SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth))
               .SetAlign(TextAlign.Center);

        PrintStyledCentered(builder, title, settings.HeadingSize, settings.HeadingBold);

        builder.SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth));

        if (receiptKind == CustomerReceiptKind.TablePayment)
        {
            PrintStyledCentered(builder, "PAID", settings.PaidStatusSize, settings.PaidStatusBold);
        }

        PrintOrderInfo(builder, orderReference, printedAt, settings);
        builder.FeedLines(1);

        if (receiptKind is CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
        {
            PrintStyledWrapped(
                builder,
                order.TableNumber > 0 ? $"Table: {order.TableNumber}" : "Table",
                settings.TableNumberSize,
                settings.TableNumberBold);

            if (order.CoverCount > 0)
            {
                PrintWrapped(builder, $"Covers: {order.CoverCount}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(customerName))
        {
            PrintStyledWrapped(builder, $"Customer: {customerName.Trim()}", settings.CustomerNameSize, settings.CustomerNameBold);
        }

        if (receiptKind is not (CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
            && !string.IsNullOrWhiteSpace(customerPhone))
        {
            PrintStyledWrapped(builder, $"Phone: {customerPhone.Trim()}", settings.CustomerPhoneSize, settings.CustomerPhoneBold);
        }

        if (receiptKind == CustomerReceiptKind.Delivery && !string.IsNullOrWhiteSpace(deliveryAddress))
        {
            builder.FeedLines(1);
            PrintStyledWrapped(builder, "Delivery Address:", settings.DeliveryAddressSize, settings.DeliveryAddressBold);

            foreach (var line in SplitAddressLines(deliveryAddress))
            {
                PrintStyledWrapped(builder, line, settings.DeliveryAddressSize, settings.DeliveryAddressBold);
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

    private static void PrintTotals(
        EscPosBuilder builder,
        TableOrder order,
        CustomerReceiptKind receiptKind,
        decimal total,
        decimal tip,
        CollectionReceiptTemplateSettings settings)
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

            if (receiptKind is CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
            {
                PrintStyledAmountLine(builder, serviceLabel, order.ServiceCharge, settings.ServiceChargeSize, settings.ServiceChargeBold);
            }
            else
            {
                PrintAmountLine(builder, serviceLabel, order.ServiceCharge);
            }
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
            if (receiptKind is CustomerReceiptKind.TableBill or CustomerReceiptKind.TablePayment)
            {
                PrintStyledWrapped(builder, "Service charge not included", settings.ServiceChargeSize, settings.ServiceChargeBold);
            }
            else
            {
                PrintWrapped(builder, "Service charge not included");
            }
        }

        builder.FeedLines(1);
    }

    private static void PrintPayment(
        EscPosBuilder builder,
        IEnumerable<TableOrderPayment> payments,
        CollectionReceiptTemplateSettings settings,
        bool useSettings)
    {
        var paymentText = FormatPaymentMethods(payments);
        if (!string.IsNullOrWhiteSpace(paymentText))
        {
            if (useSettings)
            {
                PrintStyledWrapped(builder, $"Payment: {paymentText}", settings.PaymentSize, settings.PaymentBold);
            }
            else
            {
                PrintWrapped(builder, $"Payment: {paymentText}");
            }
        }
    }

    private static void PrintPaidPaymentBreakdown(
        EscPosBuilder builder,
        IEnumerable<TableOrderPayment> payments,
        CollectionReceiptTemplateSettings settings)
    {
        var approvedPayments = payments
            .Where(payment => payment.Amount > 0)
            .ToList();

        if (approvedPayments.Count == 0)
        {
            return;
        }

        builder.PrintLine(new string('-', LineWidth));
        PrintStyledWrapped(builder, "Payment", settings.PaymentSize, settings.PaymentBold);

        foreach (var payment in approvedPayments)
        {
            PrintStyledAmountLine(
                builder,
                $"{GetPaymentMethodLabel(payment.Method)}:",
                payment.Amount,
                settings.PaymentSize,
                settings.PaymentBold);
        }

        builder.PrintLine(new string('-', LineWidth));
        PrintStyledAmountLine(
            builder,
            "Paid Total:",
            approvedPayments.Sum(payment => payment.Amount),
            settings.PaymentSize,
            settings.PaymentBold);
        PrintStyledWrapped(builder, "Status: PAID", settings.PaidStatusSize, settings.PaidStatusBold);
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

    private static void PrintFooter(EscPosBuilder builder, CollectionReceiptTemplateSettings settings)
    {
        builder.PrintLine(new string('-', LineWidth))
               .SetAlign(TextAlign.Center);

        foreach (var footerLine in WrapText(settings.FooterText, 30).Take(2))
        {
            builder.PrintLine(footerLine);
        }

        builder.SetAlign(TextAlign.Left)
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

    private static void PrintStyledAmountLine(
        EscPosBuilder builder,
        string label,
        decimal amount,
        KitchenHeadingSize size,
        bool bold)
    {
        if (size == KitchenHeadingSize.Normal)
        {
            builder.SetBold(bold);
            PrintAmountLine(builder, label, amount);
            builder.SetBold(false);
            return;
        }

        PrintStyledWrapped(builder, $"{label} {FormatCurrency(amount)}", size, bold);
    }

    private static void PrintStyledCentered(EscPosBuilder builder, string text, KitchenHeadingSize size, bool bold)
    {
        var width = ApplyReceiptSize(builder, size);
        builder.SetAlign(TextAlign.Center)
               .SetBold(bold);

        foreach (var line in WrapText(text, width))
        {
            builder.PrintLine(line);
        }

        builder.SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left);
    }

    private static void PrintStyledWrapped(EscPosBuilder builder, string text, KitchenHeadingSize size, bool bold)
    {
        var width = ApplyReceiptSize(builder, size);
        builder.SetAlign(TextAlign.Left)
               .SetBold(bold);
        PrintWrapped(builder, text, 0, width);
        builder.SetNormalSize()
               .SetBold(false);
    }

    private static void PrintOrderInfo(
        EscPosBuilder builder,
        string orderReference,
        DateTime printedAt,
        CollectionReceiptTemplateSettings settings)
    {
        if (settings.OrderInfoSize != KitchenHeadingSize.Normal)
        {
            var width = ApplyReceiptSize(builder, settings.OrderInfoSize);
            builder.SetBold(settings.OrderInfoBold);
            PrintWrapped(builder, $"Order #: {orderReference}", 0, width);
            PrintWrapped(builder, $"Date: {printedAt:dd MMM yyyy}", 0, width);
            PrintWrapped(builder, $"Time: {printedAt:HH:mm}", 0, width);
            builder.SetNormalSize()
                   .SetBold(false);
            return;
        }

        builder.SetNormalSize()
               .SetBold(settings.OrderInfoBold);
        PrintWrapped(builder, $"Order #: {orderReference}");
        builder.PrintColumns($"Date: {printedAt:dd MMM yyyy}", $"Time: {printedAt:HH:mm}");
        builder.SetBold(false);
    }

    private static int ApplyReceiptSize(EscPosBuilder builder, KitchenHeadingSize size)
    {
        return size switch
        {
            KitchenHeadingSize.ExtraLarge => ApplyReceiptFontSize(builder, 3, 1),
            KitchenHeadingSize.Large => ApplyReceiptFontSize(builder, 2, 1),
            _ => ApplyReceiptFontSize(builder, 1, 1)
        };
    }

    private static int ApplyReceiptFontSize(EscPosBuilder builder, int widthMultiplier, int heightMultiplier)
    {
        widthMultiplier = Math.Clamp(widthMultiplier, 1, 8);
        heightMultiplier = Math.Clamp(heightMultiplier, 1, 8);
        builder.SetFontSize(widthMultiplier, heightMultiplier);
        return Math.Max(8, LineWidth / widthMultiplier);
    }

    private static void PrintCenteredWrapped(EscPosBuilder builder, string text)
    {
        foreach (var line in WrapText(text, LineWidth))
        {
            builder.SetAlign(TextAlign.Center).PrintLine(line);
        }
    }

    private static void PrintWrapped(EscPosBuilder builder, string? text, int indent = 0, int? widthOverride = null)
    {
        var prefix = new string(' ', Math.Clamp(indent, 0, LineWidth - 1));
        var width = Math.Max(8, (widthOverride ?? LineWidth) - prefix.Length);
        foreach (var line in WrapText(text, width))
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
