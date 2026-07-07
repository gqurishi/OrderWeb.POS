using System.Text;
using MyFirstMauiApp.Models;
using POS_in_NET.Models;
using POS_in_NET.Models.Api;

namespace POS_in_NET.Services;

public static class KitchenTicketTemplateService
{
    private const int LineWidth = 48;
    private const string ThankYouText = "Thank you for your order";

    public static byte[] BuildTableSectionTickets(TableOrder order, PrintGroup group, IReadOnlyList<TableOrderItem> items)
    {
        var builder = new EscPosBuilder(PrinterBrand.Epson, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = GetOrderReference(order);
        var ticketTitle = order.TableNumber > 0 ? $"TABLE {order.TableNumber}" : "TABLE";

        var sections = GroupTableItems(items, group.Name);
        foreach (var section in sections)
        {
            PrintHeader(builder, ticketTitle, orderReference, printedAt, section.Title);

            foreach (var item in section.Items)
            {
                PrintTableItem(builder, item);
            }

            PrintOrderNotes(builder, order.Notes);
            PrintCheckedByFooter(builder);
            builder.FeedLines(2).Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildTakeawayKitchenTicket(
        TableOrder order,
        NetworkPrinter printer,
        IReadOnlyList<TableOrderItem> items,
        string orderType,
        IReadOnlyDictionary<string, string>? printGroupNames)
    {
        var builder = new EscPosBuilder(printer.Brand, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = GetOrderReference(order);
        var ticketTitle = NormalizeOrderType(orderType, order.TableNumber);

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        PrintHeader(builder, ticketTitle, orderReference, printedAt);

        foreach (var section in GroupTakeawayItems(items, printGroupNames))
        {
            PrintSectionTitle(builder, section.Title);
            foreach (var item in section.Items)
            {
                PrintTableItem(builder, item);
            }
        }

        PrintOrderNotes(builder, order.Notes);
        PrintCheckedByFooter(builder);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static byte[] BuildOnlineKitchenTicket(CloudOrderResponse order, NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, PaperWidth.Mm80).Initialize();
        var printedAt = DateTime.Now;
        var orderReference = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber;
        var ticketTitle = NormalizeOrderType(order.OrderType ?? "collection", 0);

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        PrintHeader(builder, ticketTitle, orderReference, printedAt);
        PrintSectionTitle(builder, "ITEMS");

        foreach (var item in order.Items)
        {
            PrintCloudItem(builder, item);
        }

        PrintOrderNotes(builder, order.SpecialInstructions);
        PrintCheckedByFooter(builder);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    public static string BuildPreview(KitchenTicketPreviewMode mode)
    {
        return mode switch
        {
            KitchenTicketPreviewMode.Table => BuildTablePreview(),
            KitchenTicketPreviewMode.Delivery => BuildTakeawayPreview("DELIVERY"),
            _ => BuildTakeawayPreview("COLLECTION")
        };
    }

    private static void PrintHeader(EscPosBuilder builder, string title, string orderReference, DateTime printedAt, string? sectionTitle = null)
    {
        builder.SetAlign(TextAlign.Left)
               .SetNormalSize()
               .SetBold(false)
               .PrintLine(new string('=', LineWidth))
               .SetAlign(TextAlign.Center)
               .SetFontSize(2, 2)
               .SetBold(true)
               .PrintLine(title)
               .SetNormalSize()
               .SetBold(false)
               .SetAlign(TextAlign.Left)
               .PrintLine(new string('=', LineWidth));

        PrintWrapped(builder, $"Order #: {orderReference}", 0);
        builder.PrintColumns($"Date: {printedAt:dd MMM yyyy}", $"Time: {printedAt:HH:mm}");

        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            PrintWrapped(builder, $"Section: {sectionTitle}", 0);
        }

        builder.PrintLine(new string('-', LineWidth)).FeedLines(1);
    }

    private static void PrintSectionTitle(EscPosBuilder builder, string title)
    {
        builder.SetBold(true)
               .PrintLine(title)
               .SetBold(false);
    }

    private static void PrintTableItem(EscPosBuilder builder, TableOrderItem item)
    {
        var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName;

        builder.SetBold(true);
        PrintWrapped(builder, $"{Math.Max(1, item.Quantity)}x {name}");
        builder.SetBold(false);

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

        builder.FeedLines(1);
    }

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

        builder.SetBold(true);
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

        builder.FeedLines(1);
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

    private static void PrintCheckedByFooter(EscPosBuilder builder)
    {
        builder.PrintLine(new string('-', LineWidth))
               .PrintLine("Checked by: ____________________")
               .FeedLines(1)
               .SetAlign(TextAlign.Center)
               .PrintLine(ThankYouText)
               .SetAlign(TextAlign.Left)
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

    private static string BuildTablePreview()
    {
        var builder = new StringBuilder();
        AppendPreviewHeader(builder, "TABLE 12", "STARTER");
        builder.AppendLine("2x Chicken Pakora");
        builder.AppendLine("   No salad");
        builder.AppendLine("   Extra sauce");
        builder.AppendLine();
        builder.AppendLine("1x Soup");
        builder.AppendLine("   Hot");
        AppendPreviewFooter(builder, includeCutMarker: true);
        builder.AppendLine();
        AppendPreviewHeader(builder, "TABLE 12", "MAIN");
        builder.AppendLine("1x Lamb Curry");
        builder.AppendLine("   Medium hot");
        builder.AppendLine();
        builder.AppendLine("2x Pilau Rice");
        AppendPreviewFooter(builder, includeCutMarker: true);
        return builder.ToString();
    }

    private static string BuildTakeawayPreview(string title)
    {
        var builder = new StringBuilder();
        AppendPreviewHeader(builder, title);
        builder.AppendLine("STARTER");
        builder.AppendLine("2x Chicken Pakora");
        builder.AppendLine("   No salad");
        builder.AppendLine();
        builder.AppendLine("MAIN");
        builder.AppendLine("1x Lamb Curry");
        builder.AppendLine("   Medium hot");
        builder.AppendLine();
        builder.AppendLine("TANDOORI");
        builder.AppendLine("1x Chicken Tikka");
        builder.AppendLine("   Well done");
        AppendPreviewFooter(builder, includeCutMarker: false);
        return builder.ToString();
    }

    private static void AppendPreviewHeader(StringBuilder builder, string title, string? section = null)
    {
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine(CenterPreviewText(title));
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine("Order #: 1042");
        builder.AppendLine("Date: 06 Jul 2026                    Time: 14:25");

        if (!string.IsNullOrWhiteSpace(section))
        {
            builder.AppendLine($"Section: {section}");
        }

        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine();
    }

    private static void AppendPreviewFooter(StringBuilder builder, bool includeCutMarker)
    {
        builder.AppendLine(new string('-', LineWidth));
        builder.AppendLine("Checked by: ____________________");
        builder.AppendLine();
        builder.AppendLine(CenterPreviewText(ThankYouText));
        builder.AppendLine(new string('=', LineWidth));

        if (includeCutMarker)
        {
            builder.AppendLine("                  -- CUT --");
        }
    }

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
