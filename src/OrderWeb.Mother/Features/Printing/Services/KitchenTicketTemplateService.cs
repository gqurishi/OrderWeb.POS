using System.Text;
using MyFirstMauiApp.Models;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;

namespace POS_in_NET.Services;

public static class KitchenTicketTemplateService
{
    private const int LineWidth = 48;

    public static byte[] BuildCourseFireCallTicket(
        TableOrder order,
        IReadOnlyList<TableOrderItem> items,
        NetworkPrinter? printer = null)
    {
        var builder = new EscPosBuilder(
            printer?.Brand ?? PrinterBrand.Epson,
            printer?.PaperWidth ?? PaperWidth.Mm80).Initialize();
        var fireText = string.IsNullOrWhiteSpace(order.KitchenTicketType)
            ? "FIRE COURSE"
            : order.KitchenTicketType.Trim().ToUpperInvariant();
        var tableText = order.TableNumber > 0 ? $"TABLE {order.TableNumber}" : "TABLE";

        builder.SetAlign(TextAlign.Center)
               .SetBold(true)
               .SetFontSize(2, 2)
               .PrintLine(fireText)
               .PrintLine(tableText)
               .SetNormalSize()
               .SetBold(false)
               .PrintLine(new string('-', LineWidth));

        foreach (var item in items)
        {
            PrintTableItem(builder, item, SupportsRedInk(printer));
        }

        builder.PrintLine(new string('-', LineWidth))
               .FeedLines(3);

        if (printer?.HasCutter != false)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildTableSectionTickets(
        TableOrder order,
        PrintGroup group,
        IReadOnlyList<TableOrderItem> items,
        KitchenTemplateSettings? settings = null,
        NetworkPrinter? printer = null)
    {
        settings = NormalizeSettings(settings);
        var builder = new EscPosBuilder(PrinterBrand.Epson, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = GetOrderReference(order);
        var tableTitle = order.TableNumber > 0 ? $"TABLE {order.TableNumber}" : "TABLE";
        var ticketTitle = !string.IsNullOrWhiteSpace(order.KitchenTicketType)
            && order.KitchenTicketType.StartsWith("FIRE ", StringComparison.OrdinalIgnoreCase)
                ? $"{order.KitchenTicketType.ToUpperInvariant()} - {tableTitle}"
                : tableTitle;

        if (items.Any(item => item.KitchenAction == KitchenChangeAction.Void))
        {
            builder.Buzzer();
        }

        var sections = GroupTableItems(items, group.Name);
        foreach (var section in sections)
        {
            PrintHeader(builder, ticketTitle, orderReference, printedAt, settings, section.Title);
            foreach (var item in section.Items)
            {
                PrintTableItem(builder, item, SupportsRedInk(printer));
            }

            PrintOrderNotes(builder, order.Notes);
            PrintCheckedByFooter(builder, settings);
            builder.FeedLines(2).Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildTakeawayKitchenTicket(
        TableOrder order,
        NetworkPrinter printer,
        IReadOnlyList<TableOrderItem> items,
        string orderType,
        IReadOnlyDictionary<string, string>? printGroupNames,
        KitchenTemplateSettings? settings = null)
    {
        settings = NormalizeSettings(settings);
        var builder = new EscPosBuilder(printer.Brand, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = GetOrderReference(order);
        var ticketTitle = NormalizeOrderType(orderType, order.TableNumber);

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        PrintHeader(builder, ticketTitle, orderReference, printedAt, settings);
        foreach (var section in GroupTakeawayItems(items, printGroupNames))
        {
            PrintSectionTitle(builder, section.Title, settings);
            foreach (var item in section.Items)
            {
                PrintTableItem(builder, item, SupportsRedInk(printer));
            }
        }

        PrintOrderNotes(builder, order.Notes);
        PrintCheckedByFooter(builder, settings);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildOnlineKitchenTicket(
        CloudOrderResponse order,
        NetworkPrinter printer,
        KitchenTemplateSettings? settings = null)
    {
        settings = NormalizeSettings(settings);
        var builder = new EscPosBuilder(printer.Brand, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber;
        var ticketTitle = NormalizeOrderType(order.OrderType ?? "collection", 0);

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        PrintHeader(builder, ticketTitle, orderReference, printedAt, settings);
        PrintSectionTitle(builder, "ITEMS", settings);

        foreach (var item in order.Items)
        {
            PrintCloudItem(builder, item);
        }

        PrintOrderNotes(builder, order.SpecialInstructions);
        PrintCheckedByFooter(builder, settings);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static string BuildPreview(KitchenTicketPreviewMode mode, KitchenTemplateSettings? settings = null)
    {
        settings = NormalizeSettings(settings);
        return mode switch
        {
            KitchenTicketPreviewMode.Table => BuildTablePreview(settings),
            KitchenTicketPreviewMode.Delivery => BuildTakeawayPreview("DELIVERY", settings),
            _ => BuildTakeawayPreview("COLLECTION", settings)
        };
    }

    private static void PrintHeader(
        EscPosBuilder builder,
        string title,
        string orderReference,
        DateTime printedAt,
        KitchenTemplateSettings settings,
        string? sectionTitle = null)
    {
        builder.SetAlign(TextAlign.Left)
               .SetNormalSize()
               .SetBold(false)
               .PrintLine(new string('=', LineWidth))
               .SetAlign(TextAlign.Center);

        PrintStyledCentered(builder, title, settings.HeadingSize, settings.HeadingBold);

        builder.SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth));

        PrintOrderInfo(builder, orderReference, printedAt, settings);

        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            PrintSectionTitle(builder, $"Section: {sectionTitle}", settings);
        }

        builder.PrintLine(new string('-', LineWidth)).FeedLines(1);
    }

    private static void PrintSectionTitle(EscPosBuilder builder, string title, KitchenTemplateSettings settings)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "ITEMS" : title.Trim();
        if (settings.SectionHeadingSize != KitchenHeadingSize.Normal)
        {
            var width = ApplyInlineSize(builder, settings.SectionHeadingSize);
            builder.SetBold(settings.SectionHeadingBold);
            PrintWrapped(builder, normalizedTitle.ToUpperInvariant(), 0, width);
            builder.SetNormalSize().SetBold(false);
            return;
        }

        builder.SetNormalSize()
               .SetBold(settings.SectionHeadingBold)
               .PrintLine(settings.SectionHeadingBold ? normalizedTitle.ToUpperInvariant() : normalizedTitle)
               .SetBold(false);
    }

    private static void PrintTableItem(EscPosBuilder builder, TableOrderItem item, bool supportsRedInk)
    {
        var useRedInk = supportsRedInk && item.PrintInRed;
        if (useRedInk)
        {
            builder.SetRedInk(true);
        }

        var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName;
        var actionPrefix = item.KitchenAction switch
        {
            KitchenChangeAction.Void => "VOID ",
            _ => string.Empty
        };

        builder.SetNormalSize().SetBold(true);
        PrintWrapped(builder, $"{actionPrefix}{Math.Max(1, item.Quantity)}x {name}");
        builder.SetBold(false);

        if (item.KitchenAction == KitchenChangeAction.Void)
        {
            builder.SetNormalSize().FeedLines(1);
            if (useRedInk)
            {
                builder.SetRedInk(false);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Modifiers))
        {
            foreach (var modifier in SplitDetails(item.Modifiers))
            {
                PrintWrapped(builder, modifier, 3);
            }
        }

        foreach (var addon in item.SelectedAddons)
        {
            PrintWrapped(builder, $"+ {addon.Name}", 3);
        }

        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            builder.SetBold(true);
            PrintWrapped(builder, $">> {item.Notes}", 3);
            builder.SetBold(false);
        }

        builder.SetNormalSize().SetBold(false).FeedLines(1);
        if (useRedInk)
        {
            builder.SetRedInk(false);
        }
    }

    private static bool SupportsRedInk(NetworkPrinter? printer) =>
        printer is { SupportsTwoColor: true, Brand: PrinterBrand.Epson };

    private static void PrintCloudItem(EscPosBuilder builder, CloudOrderItem item)
    {
        var name = !string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.DisplayName
            : !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : "Item";

        if (!string.IsNullOrWhiteSpace(item.VariantName) && !name.Contains(item.VariantName, StringComparison.OrdinalIgnoreCase))
        {
            name = $"{name} ({item.VariantName})";
        }

        builder.SetNormalSize().SetBold(true);
        PrintWrapped(builder, $"{Math.Max(1, item.Quantity)}x {name}");
        builder.SetBold(false);

        foreach (var addon in item.SelectedAddons)
        {
            if (!string.IsNullOrWhiteSpace(addon.Name))
            {
                PrintWrapped(builder, $"+ {addon.Name}", 3);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.SpecialInstructions))
        {
            builder.SetBold(true);
            PrintWrapped(builder, $">> {item.SpecialInstructions}", 3);
            builder.SetBold(false);
        }

        builder.SetNormalSize().SetBold(false).FeedLines(1);
    }

    private static void PrintOrderNotes(EscPosBuilder builder, string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        builder.PrintLine(new string('-', LineWidth))
               .SetBold(true)
               .PrintLine("ORDER NOTES:")
               .SetBold(false);
        PrintWrapped(builder, notes);
        builder.FeedLines(1);
    }

    private static void PrintCheckedByFooter(EscPosBuilder builder, KitchenTemplateSettings settings)
    {
        builder.SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('-', LineWidth));

        if (settings.ShowCheckedByLine)
        {
            builder.PrintLine("Checked by: ______________________________")
                   .FeedLines(2);
        }

        builder.SetAlign(TextAlign.Center);
        foreach (var footerLine in WrapText(settings.FooterText, 30).Take(2))
        {
            builder.PrintLine(footerLine);
        }

        builder.SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth))
               .FeedLines(2);
    }

    private static IEnumerable<KitchenTicketSection<TableOrderItem>> GroupTableItems(IReadOnlyList<TableOrderItem> items, string fallbackSection)
    {
        var sectionFallback = NormalizeSectionTitle(fallbackSection, "ITEMS");
        return items
            .GroupBy(item => NormalizeSectionTitle(item.CourseType, sectionFallback))
            .Select(group => new KitchenTicketSection<TableOrderItem>(group.Key, group.ToList()));
    }

    private static IEnumerable<KitchenTicketSection<TableOrderItem>> GroupTakeawayItems(
        IReadOnlyList<TableOrderItem> items,
        IReadOnlyDictionary<string, string>? printGroupNames)
    {
        return items
            .GroupBy(item =>
            {
                if (!string.IsNullOrWhiteSpace(item.PrintGroupId)
                    && printGroupNames != null
                    && printGroupNames.TryGetValue(item.PrintGroupId, out var groupName))
                {
                    return NormalizeSectionTitle(groupName, "ITEMS");
                }

                return "ITEMS";
            })
            .Select(group => new KitchenTicketSection<TableOrderItem>(group.Key, group.ToList()));
    }

    private static void PrintStyledCentered(EscPosBuilder builder, string title, KitchenHeadingSize headingSize, bool bold)
    {
        var effectiveWidth = ApplyHeadingSize(builder, headingSize);
        builder.SetBold(bold);
        foreach (var line in WrapText(title, effectiveWidth))
        {
            builder.PrintLine(line);
        }

        builder.SetNormalSize().SetBold(false);
    }

    private static int ApplyHeadingSize(EscPosBuilder builder, KitchenHeadingSize headingSize)
    {
        return headingSize switch
        {
            KitchenHeadingSize.ExtraLarge => ApplyFontSize(builder, 3, 2),
            KitchenHeadingSize.Large => ApplyFontSize(builder, 2, 2),
            _ => ApplyFontSize(builder, 1, 1)
        };
    }

    private static int ApplyFontSize(EscPosBuilder builder, int widthMultiplier, int heightMultiplier)
    {
        widthMultiplier = Math.Clamp(widthMultiplier, 1, 8);
        heightMultiplier = Math.Clamp(heightMultiplier, 1, 8);
        builder.SetFontSize(widthMultiplier, heightMultiplier);
        return Math.Max(8, LineWidth / widthMultiplier);
    }

    private static int ApplyInlineSize(EscPosBuilder builder, KitchenHeadingSize size)
    {
        return size switch
        {
            KitchenHeadingSize.ExtraLarge => ApplyFontSize(builder, 3, 1),
            KitchenHeadingSize.Large => ApplyFontSize(builder, 2, 1),
            _ => ApplyFontSize(builder, 1, 1)
        };
    }

    private static void PrintOrderInfo(EscPosBuilder builder, string orderReference, DateTime printedAt, KitchenTemplateSettings settings)
    {
        if (settings.OrderInfoSize != KitchenHeadingSize.Normal)
        {
            var width = ApplyInlineSize(builder, settings.OrderInfoSize);
            builder.SetBold(settings.OrderInfoBold);
            PrintWrapped(builder, $"Order #: {orderReference}", 0, width);
            PrintWrapped(builder, $"Date: {printedAt:dd MMM yyyy}", 0, width);
            PrintWrapped(builder, $"Time: {printedAt:HH:mm}", 0, width);
            builder.SetNormalSize().SetBold(false);
            return;
        }

        builder.SetNormalSize().SetBold(settings.OrderInfoBold);
        PrintWrapped(builder, $"Order #: {orderReference}", 0);
        builder.PrintColumns($"Date: {printedAt:dd MMM yyyy}", $"Time: {printedAt:HH:mm}");
        builder.SetBold(false);
    }

    private static KitchenTemplateSettings NormalizeSettings(KitchenTemplateSettings? settings) =>
        (settings ?? KitchenTemplateSettings.Default()).Normalized();

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

    private static IEnumerable<string> SplitDetails(string details)
    {
        return details
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0);
    }

    private static string GetOrderReference(TableOrder order)
    {
        return string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber;
    }

    private static string NormalizeOrderType(string orderType, int tableNumber)
    {
        var normalized = orderType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "delivery" or "del" => "DELIVERY",
            "pickup" or "collection" or "collect" or "col" or "takeaway" => "COLLECTION",
            "table" or "dine_in" or "dine-in" => tableNumber > 0 ? $"TABLE {tableNumber}" : "TABLE",
            _ => string.IsNullOrWhiteSpace(orderType) ? "COLLECTION" : orderType.Trim().ToUpperInvariant()
        };
    }

    private static string NormalizeSectionTitle(string? value, string fallback)
    {
        var title = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        title = title.Replace('_', ' ').Replace('-', ' ');
        return string.IsNullOrWhiteSpace(title) ? "ITEMS" : title.ToUpperInvariant();
    }

    private static string BuildTablePreview(KitchenTemplateSettings settings)
    {
        var builder = new StringBuilder();
        AppendPreviewHeader(builder, "TABLE 12", settings, "STARTER");
        builder.AppendLine("2x Chicken Pakora");
        builder.AppendLine("   No salad");
        builder.AppendLine("   Extra sauce");
        builder.AppendLine();
        builder.AppendLine("1x Soup");
        builder.AppendLine("   Hot");
        AppendPreviewFooter(builder, settings, includeCutMarker: true);
        builder.AppendLine();
        AppendPreviewHeader(builder, "TABLE 12", settings, "MAIN");
        builder.AppendLine("1x Lamb Curry");
        builder.AppendLine("   Medium hot");
        builder.AppendLine();
        builder.AppendLine("2x Pilau Rice");
        AppendPreviewFooter(builder, settings, includeCutMarker: true);
        return builder.ToString();
    }

    private static string BuildTakeawayPreview(string title, KitchenTemplateSettings settings)
    {
        var builder = new StringBuilder();
        AppendPreviewHeader(builder, title, settings);
        builder.AppendLine(FormatPreviewSectionTitle("STARTER", settings));
        builder.AppendLine("2x Chicken Pakora");
        builder.AppendLine("   No salad");
        builder.AppendLine();
        builder.AppendLine(FormatPreviewSectionTitle("MAIN", settings));
        builder.AppendLine("1x Lamb Curry");
        builder.AppendLine("   Medium hot");
        builder.AppendLine();
        builder.AppendLine(FormatPreviewSectionTitle("TANDOORI", settings));
        builder.AppendLine("1x Chicken Tikka");
        builder.AppendLine("   Well done");
        AppendPreviewFooter(builder, settings, includeCutMarker: false);
        return builder.ToString();
    }

    private static void AppendPreviewHeader(StringBuilder builder, string title, KitchenTemplateSettings settings, string? section = null)
    {
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine(CenterPreviewText(title));
        builder.AppendLine(new string('=', LineWidth));
        if (settings.OrderInfoSize != KitchenHeadingSize.Normal)
        {
            builder.AppendLine("Order #: 1042");
            builder.AppendLine("Date: 06 Jul 2026");
            builder.AppendLine("Time: 14:25");
        }
        else
        {
            builder.AppendLine("Order #: 1042");
            builder.AppendLine("Date: 06 Jul 2026                    Time: 14:25");
        }

        if (!string.IsNullOrWhiteSpace(section))
        {
        builder.AppendLine(FormatPreviewSectionTitle($"Section: {section}", settings));
        }

        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine();
    }

    private static void AppendPreviewFooter(StringBuilder builder, KitchenTemplateSettings settings, bool includeCutMarker)
    {
        builder.AppendLine(new string('-', LineWidth));
        if (settings.ShowCheckedByLine)
        {
            builder.AppendLine("Checked by: ______________________________");
            builder.AppendLine();
            builder.AppendLine();
        }

        foreach (var footerLine in WrapText(settings.FooterText, 30).Take(2))
        {
            builder.AppendLine(CenterPreviewText(footerLine));
        }

        builder.AppendLine(new string('=', LineWidth));

        if (includeCutMarker)
        {
            builder.AppendLine("                  -- CUT --");
        }
    }

    private static string FormatPreviewSectionTitle(string title, KitchenTemplateSettings settings) =>
        settings.SectionHeadingBold ? title.ToUpperInvariant() : title;

    private static string CenterPreviewText(string text)
    {
        if (text.Length >= LineWidth)
        {
            return text;
        }

        var left = (LineWidth - text.Length) / 2;
        return new string(' ', left) + text;
    }

    private sealed class KitchenTicketSection<T>
    {
        public KitchenTicketSection(string title, List<T> items)
        {
            Title = title;
            Items = items;
        }

        public string Title { get; }
        public List<T> Items { get; }
    }
}

public enum KitchenTicketPreviewMode
{
    Table,
    Collection,
    Delivery
}
