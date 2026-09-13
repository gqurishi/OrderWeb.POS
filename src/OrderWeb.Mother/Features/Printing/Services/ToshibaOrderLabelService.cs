using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Queues Toshiba TPCL labels for an accepted kitchen send. It never talks to
/// the printer itself and never treats a label failure as a kitchen-ticket failure.
/// </summary>
public sealed class ToshibaOrderLabelService
{
    public const string TemplateVersion = "toshiba-text-v1";

    private readonly NetworkPrinterDatabaseService _printers;
    private readonly LabelPrintDatabaseService _labels;
    private readonly LabelPrintQueueService _queue;
    private readonly MenuItemService _menuItems;

    public ToshibaOrderLabelService(
        NetworkPrinterDatabaseService printers,
        LabelPrintDatabaseService labels,
        LabelPrintQueueService queue,
        MenuItemService menuItems)
    {
        _printers = printers;
        _labels = labels;
        _queue = queue;
        _menuItems = menuItems;
    }

    public async Task<ToshibaOrderLabelResult> EnqueueForPrintedItemsAsync(
        TableOrder order,
        ISet<string> printedItemIds,
        string sourceTerminal,
        string? orderType,
        string? sourceChannel,
        string? reprintKey = null,
        CancellationToken cancellationToken = default)
    {
        if (printedItemIds.Count == 0)
            return ToshibaOrderLabelResult.None;

        var decision = LabelPrintRules.Decide(orderType, sourceChannel);
        if (!decision.ShouldPrint)
            return ToshibaOrderLabelResult.Skipped;

        var printer = await ResolveToshibaPrinterAsync();
        if (printer == null)
            return ToshibaOrderLabelResult.NotToshiba;

        await _labels.EnsureBuiltInProfilesAsync();
        if (string.IsNullOrWhiteSpace(printer.LabelMediaProfileId))
            return ToshibaOrderLabelResult.Skipped;

        var queued = 0;
        var failed = 0;
        var errors = new List<string>();
        var orderContext = BuildOrderContext(order, decision);

        foreach (var item in order.Items.Where(item =>
                     printedItemIds.Contains(item.Id)
                     && !item.IsVoided
                     && item.KitchenAction is KitchenChangeAction.New or KitchenChangeAction.Add))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copies = CopiesToPrint(item);
            if (copies < 1)
                continue;

            var menuItem = await _menuItems.GetItemByIdAsync(item.MenuItemId);
            if (menuItem == null || !HasLabelWork(menuItem))
                continue;
            if (menuItem.PrintComponentLabels && string.IsNullOrWhiteSpace(menuItem.ComponentLabelsJson))
                menuItem.Components = await _menuItems.GetItemComponentsAsync(menuItem.Id);

            try
            {
                if (menuItem.PrintComponentLabels)
                {
                    var components = ParseComponents(menuItem);
                    if (components.Count == 0)
                    {
                        failed++;
                        errors.Add($"{item.Name}: component labels are on, but none are configured.");
                        continue;
                    }

                    foreach (var component in components)
                    {
                        var componentCopies = Math.Clamp(component.Quantity * copies, 1, 99);
                        await EnqueueAsync(printer, order, item, LabelJobType.Component, component.Name, componentCopies, orderContext, menuItem.Name, sourceTerminal, reprintKey);
                        queued++;
                    }

                    if (!menuItem.AlsoPrintMainLabel)
                        continue;
                }

                if (string.IsNullOrWhiteSpace(menuItem.LabelText) && !menuItem.AlsoPrintMainLabel)
                    continue;

                await EnqueueAsync(printer, order, item, LabelJobType.Item, item.Name, copies, orderContext, menuItem.LabelText, sourceTerminal, reprintKey);
                queued++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{item.Name}: {ex.Message}");
            }
        }

        if (queued > 0)
            await _queue.ProcessQueueAsync(cancellationToken);

        return new ToshibaOrderLabelResult(true, queued, queued, failed, errors);
    }

    private async Task EnqueueAsync(
        NetworkPrinter printer,
        TableOrder order,
        TableOrderItem item,
        LabelJobType jobType,
        string name,
        int copies,
        string orderContext,
        string? fixedText,
        string sourceTerminal,
        string? reprintKey)
    {
        var modifiers = new List<string>();
        if (jobType == LabelJobType.Component && !string.IsNullOrWhiteSpace(item.Name))
            modifiers.Add(item.Name);
        if (!string.IsNullOrWhiteSpace(item.Notes))
            modifiers.Add(item.Notes.Trim());
        modifiers.Add("ALLERGY: CHECK ORDER DETAILS");

        var contentType = jobType == LabelJobType.Component ? "component" : "item";
        var job = new LabelPrintJob
        {
            PrinterId = printer.Id,
            MediaProfileId = printer.LabelMediaProfileId!,
            OrderId = item.DatabaseId > 0 ? item.DatabaseId : null,
            SourceTerminal = sourceTerminal,
            ClientRequestId = string.IsNullOrWhiteSpace(reprintKey)
                ? $"{order.Id}|{item.Id}|{jobType}"
                : $"{order.Id}|reprint|{reprintKey.Trim()}|{item.Id}|{jobType}",
            IdempotencyKey = IdempotencyKey(order, item, jobType, name, copies, printer.Id, reprintKey),
            SendRevision = Math.Max(1, item.PreviousQuantity + 1),
            JobType = jobType,
            Content = new LabelContentSnapshot(
                contentType,
                name.Trim(),
                copies,
                modifiers,
                orderContext,
                DateTimeOffset.Now,
                string.IsNullOrWhiteSpace(fixedText) ? null : fixedText.Trim()),
            LabelTemplateVersion = printer.ModuleIdentifier switch
            {
                LabelPrinterProfiles.XprinterTsplModuleId => "xprinter-tspl-v1",
                LabelPrinterProfiles.BrotherRasterModuleId => "brother-raster-v1",
                _ => TemplateVersion
            },
            QuantityCopies = copies,
            Status = LabelJobStatus.Pending
        };
        await _labels.EnqueueAsync(job);
    }

    private async Task<NetworkPrinter?> ResolveToshibaPrinterAsync()
    {
        var printers = await _printers.GetPrintersByTypeAsync(NetworkPrinterType.Label);
        return printers.FirstOrDefault(printer => printer.IsEnabled && printer.IsDefaultLabelPrinter && IsDurableLabelPrinter(printer))
            ?? printers.FirstOrDefault(printer => printer.IsEnabled && IsDurableLabelPrinter(printer));
    }

    private static bool IsDurableLabelPrinter(NetworkPrinter printer) =>
        string.Equals(printer.ModuleIdentifier, "toshiba_tpcl", StringComparison.OrdinalIgnoreCase)
        || string.Equals(printer.ModuleIdentifier, LabelPrinterProfiles.XprinterTsplModuleId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(printer.ModuleIdentifier, LabelPrinterProfiles.BrotherRasterModuleId, StringComparison.OrdinalIgnoreCase)
        || printer.Brand == PrinterBrand.Toshiba
        || printer.Brand == PrinterBrand.Brother
        || (printer.Brand == PrinterBrand.Xprinter && string.Equals(printer.ModelCode, LabelPrinterProfiles.XprinterXp421bCode, StringComparison.OrdinalIgnoreCase));

    private static bool HasLabelWork(FoodMenuItem item) =>
        !string.IsNullOrWhiteSpace(item.LabelText)
        || item.PrintComponentLabels;

    private static int CopiesToPrint(TableOrderItem item)
    {
        if (item.KitchenAction is not (KitchenChangeAction.New or KitchenChangeAction.Add))
            return 0;
        return LabelPrintRules.StickerCopies(item.Quantity);
    }

    private static string BuildOrderContext(TableOrder order, LabelPrintDecision decision)
    {
        var reference = string.IsNullOrWhiteSpace(order.OrderNumber)
            ? order.Id
            : order.OrderNumber.Trim();
        return LabelPrintRules.FormatSticker(decision, reference);
    }

    private static string IdempotencyKey(TableOrder order, TableOrderItem item, LabelJobType jobType, string name, int copies, int printerId, string? reprintKey)
    {
        var raw = string.Join("|",
            order.Id,
            item.Id,
            jobType,
            name.Trim(),
            copies,
            item.PreviousQuantity,
            printerId,
            TemplateVersion,
            reprintKey?.Trim() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static List<(string Name, int Quantity)> ParseComponents(FoodMenuItem item)
    {
        var labels = new List<(string Name, int Quantity)>();
        if (!string.IsNullOrWhiteSpace(item.ComponentLabelsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(item.ComponentLabelsJson);
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var legacy = element.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(legacy))
                            labels.Add((legacy, 1));
                    }
                    else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var nameProperty))
                    {
                        var name = nameProperty.GetString()?.Trim();
                        var quantity = element.TryGetProperty("quantity", out var quantityProperty)
                            && quantityProperty.TryGetInt32(out var parsed)
                            ? Math.Clamp(parsed, 1, 99)
                            : 1;
                        if (!string.IsNullOrWhiteSpace(name))
                            labels.Add((name, quantity));
                    }
                }
            }
            catch (JsonException)
            {
                labels.Clear();
            }
        }

        if (labels.Count == 0)
        {
            labels.AddRange(item.Components
                .Where(component => !string.IsNullOrWhiteSpace(component.ComponentName))
                .Select(component => (component.ComponentName.Trim(), 1)));
        }

        return labels
            .GroupBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }
}

public sealed record ToshibaOrderLabelResult(
    bool HandledByToshiba,
    int Attempted,
    int Queued,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public static ToshibaOrderLabelResult None { get; } = new(true, 0, 0, 0, Array.Empty<string>());
    public static ToshibaOrderLabelResult Skipped { get; } = new(true, 0, 0, 0, Array.Empty<string>());
    public static ToshibaOrderLabelResult NotToshiba { get; } = new(false, 0, 0, 0, Array.Empty<string>());
}
