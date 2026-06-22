using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using POS_in_NET.Models;
using POS_in_NET.Pages;

namespace POS_in_NET.Services;

public sealed class OrderRoutingPrintResult
{
    public HashSet<string> PrintedItemIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> FailedRoutes { get; } = new();
    public List<PrintRouteFailure> FailedRouteDetails { get; } = new();

    public bool AnyPrinted => PrintedItemIds.Count > 0;
    public bool HasFailures => FailedRoutes.Count > 0;
}

public sealed class PrintRouteFailure
{
    public string RouteTarget { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RouteValidationIssue
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Routes menu items to the configured IP printer groups and prints ESC/POS kitchen/bar tickets.
/// Supports smart fallback: PDF if 0 printers, auto catch-all if 1 printer, or normal routing if 2+.
/// </summary>
public sealed class OrderRoutingPrintService
{
    private readonly PrintGroupService _printGroupService;
    private readonly NetworkPrinterService _printerService;
    private readonly PdfPrintService _pdfPrintService;

    public OrderRoutingPrintService()
    {
        _printGroupService = new PrintGroupService();
        _printerService = new NetworkPrinterService();
        _pdfPrintService = new PdfPrintService();
    }

    /// <summary>
    /// Detect the current printing mode based on configured printers
    /// </summary>
    private async Task<(PrintingMode Mode, PrintGroup? SinglePrinter, string Status)> DetectPrintingModeAsync()
    {
        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        var groupsWithIp = activeGroups.Where(g => !string.IsNullOrWhiteSpace(g.PrinterIp)).ToList();

        if (groupsWithIp.Count == 0)
        {
            return (PrintingMode.PdfFallback, null, "No printers configured - using PDF fallback");
        }

        if (groupsWithIp.Count == 1)
        {
            return (PrintingMode.SinglePrinterCatchAll, groupsWithIp[0], $"Single printer mode - all orders→{groupsWithIp[0].Name}");
        }

        return (PrintingMode.NormalRouting, null, "Normal routing mode - multiple printers configured");
    }

    private enum PrintingMode
    {
        PdfFallback,           // No printers - save as PDF
        SinglePrinterCatchAll, // 1 printer - catch all items
        NormalRouting          // 2+ printers - route per group
    }

    public async Task<List<RouteValidationIssue>> ValidateEnvironmentAsync()
    {
        var issues = new List<RouteValidationIssue>();
        var (mode, _, _) = await DetectPrintingModeAsync();

        // PDF fallback mode - no hard blocks, just informational
        if (mode == PrintingMode.PdfFallback)
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "pdf_fallback_mode",
                Message = "Test Mode: Orders will be saved as PDFs (no printers configured)"
            });
            return issues;
        }

        // Single printer mode - no hard blocks, just informational
        if (mode == PrintingMode.SinglePrinterCatchAll)
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "single_printer_mode",
                Message = "Single Printer Mode: All orders will print to the configured printer"
            });
            return issues;
        }

        // Normal routing mode - check for issues but be lenient
        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();

        if (activeGroups.Count == 0)
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "no_active_groups",
                Message = "No active print groups configured"
            });
            return issues;
        }

        // Check for missing IPs - warn but don't block
        foreach (var group in activeGroups.Where(group => string.IsNullOrWhiteSpace(group.PrinterIp)))
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "missing_printer_ip",
                Message = $"⚠️ {group.Name}: printer IP not configured"
            });
        }

        return issues;
    }

    public async Task<List<RouteValidationIssue>> ValidateOrderRoutesAsync(TableOrder order, string? routeTarget = null)
    {
        var issues = await ValidateEnvironmentAsync();
        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();

        var itemsToPrint = order.Items
            .Where(item => item.SendStatus == ItemSendStatus.NotSent)
            .Where(item => string.IsNullOrWhiteSpace(routeTarget) || string.Equals(item.PrintGroupId, routeTarget, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (itemsToPrint.Count == 0)
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "no_items_to_send",
                Message = "No unsent items available for this send action"
            });
            return issues;
        }

        var groupLookup = activeGroups.ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var item in itemsToPrint)
        {
            if (string.IsNullOrWhiteSpace(item.PrintGroupId))
            {
                issues.Add(new RouteValidationIssue
                {
                    Code = "missing_item_route",
                    Message = $"{item.Name}: no print group assigned"
                });
                continue;
            }

            if (!groupLookup.TryGetValue(item.PrintGroupId, out var group))
            {
                issues.Add(new RouteValidationIssue
                {
                    Code = "missing_group",
                    Message = $"{item.Name}: assigned print group not found ({item.PrintGroupId})"
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(group.PrinterIp))
            {
                issues.Add(new RouteValidationIssue
                {
                    Code = "missing_printer_ip",
                    Message = $"{item.Name}: printer IP not configured for {group.Name}"
                });
            }
        }

        return issues;
    }

    public async Task<OrderRoutingPrintResult> PrintOrderAsync(TableOrder order, string? routeTarget = null)
    {
        var result = new OrderRoutingPrintResult();

        if (order.Items.Count == 0)
        {
            result.FailedRoutes.Add("No items to print");
            return result;
        }

        // Detect printing mode
        var (mode, singlePrinter, modeStatus) = await DetectPrintingModeAsync();
        System.Diagnostics.Debug.WriteLine($"📍 Printing mode: {modeStatus}");

        var itemsToPrint = order.Items
            .Where(item => item.SendStatus == ItemSendStatus.NotSent)
            .Where(item => string.IsNullOrWhiteSpace(routeTarget) || string.Equals(item.PrintGroupId, routeTarget, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (itemsToPrint.Count == 0)
        {
            return result;
        }

        // ========== MODE 1: PDF FALLBACK (No printers configured) ==========
        if (mode == PrintingMode.PdfFallback)
        {
            return await HandlePdfFallbackAsync(order, itemsToPrint);
        }

        // ========== MODE 2: SINGLE PRINTER CATCH-ALL (Exactly 1 printer with IP) ==========
        if (mode == PrintingMode.SinglePrinterCatchAll && singlePrinter != null)
        {
            return await HandleSinglePrinterModeAsync(order, singlePrinter, itemsToPrint);
        }

        // ========== MODE 3: NORMAL ROUTING (2+ printers or no printers in any group) ==========
        return await HandleNormalRoutingAsync(order, itemsToPrint, routeTarget);
    }

    public async Task<OrderRoutingPrintResult> PrintTakeawayOrderAsync(TableOrder order, string orderType)
    {
        var result = new OrderRoutingPrintResult();

        var itemsToPrint = order.Items
            .Where(item => item.SendStatus == ItemSendStatus.NotSent)
            .ToList();

        if (itemsToPrint.Count == 0)
        {
            return result;
        }

        var printerDb = ServiceHelper.GetService<NetworkPrinterDatabaseService>();
        if (printerDb == null)
        {
            result.FailedRoutes.Add("Takeaway Kitchen printer service is not available");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = string.Empty,
                RouteName = "Takeaway Kitchen",
                Reason = "printer service is not available"
            });
            return result;
        }

        var takeawayPrinter = (await printerDb.GetPrintersByTypeAsync(NetworkPrinterType.Takeaway))
            .FirstOrDefault(printer => printer.IsEnabled);

        if (takeawayPrinter == null)
        {
            result.FailedRoutes.Add("Takeaway Kitchen printer is not configured");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = string.Empty,
                RouteName = "Takeaway Kitchen",
                Reason = "printer is not configured"
            });
            return result;
        }

        var ticketData = BuildTakeawayTicket(order, takeawayPrinter, itemsToPrint, orderType);
        var sent = await _printerService.SendRawDataAsync(takeawayPrinter.IpAddress, takeawayPrinter.Port, ticketData);

        if (sent)
        {
            foreach (var item in itemsToPrint)
            {
                result.PrintedItemIds.Add(item.Id);
            }
        }
        else
        {
            result.FailedRoutes.Add($"{takeawayPrinter.Name}: print failed");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = string.Empty,
                RouteName = takeawayPrinter.Name,
                Reason = "print failed"
            });
        }

        return result;
    }

    private async Task<OrderRoutingPrintResult> HandlePdfFallbackAsync(TableOrder order, List<TableOrderItem> itemsToPrint)
    {
        var result = new OrderRoutingPrintResult();
        
        System.Diagnostics.Debug.WriteLine($"📄 {itemsToPrint.Count} items → PDF fallback (no printers)");

        // Get an active print group to use as context (for grouping)
        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        if (activeGroups.Count == 0)
        {
            result.FailedRoutes.Add("No active print groups to determine output type");
            return result;
        }

        // Group items by their assigned group type for separate PDFs
        var groupedByType = itemsToPrint
            .GroupBy(item => DetermineItemGroupType(item, activeGroups))
            .ToList();

        foreach (var group in groupedByType)
        {
            var groupType = group.Key;
            var groupItems = group.ToList();

            var pdfGroup = activeGroups.FirstOrDefault(g => 
                string.Equals(g.PrinterType, groupType, StringComparison.OrdinalIgnoreCase))
                ?? activeGroups[0]; // Fallback to first group

            var (pdfSuccess, filePath, pdfMsg) = await _pdfPrintService.GeneratePrintAsync(order, pdfGroup, groupItems);

            if (pdfSuccess)
            {
                foreach (var item in groupItems)
                {
                    result.PrintedItemIds.Add(item.Id);
                }
                System.Diagnostics.Debug.WriteLine($"✅ PDF saved: {pdfMsg}");
            }
            else
            {
                result.FailedRoutes.Add($"Failed to save PDF: {pdfMsg}");
            }
        }

        return result;
    }

    private async Task<OrderRoutingPrintResult> HandleSinglePrinterModeAsync(TableOrder order, PrintGroup singlePrinter, List<TableOrderItem> itemsToPrint)
    {
        var result = new OrderRoutingPrintResult();

        System.Diagnostics.Debug.WriteLine($"🖨️ {itemsToPrint.Count} items → Single printer catch-all ({singlePrinter.Name})");

        if (string.IsNullOrWhiteSpace(singlePrinter.PrinterIp))
        {
            result.FailedRoutes.Add($"{singlePrinter.Name}: printer IP not configured");
            return result;
        }

        var ticketData = BuildTicket(order, singlePrinter, itemsToPrint);
        var sent = await _printerService.SendRawDataAsync(singlePrinter.PrinterIp, singlePrinter.PrinterPort, ticketData);

        if (sent)
        {
            foreach (var item in itemsToPrint)
            {
                result.PrintedItemIds.Add(item.Id);
            }
            System.Diagnostics.Debug.WriteLine($"✅ Printed to {singlePrinter.Name}");
        }
        else
        {
            result.FailedRoutes.Add($"{singlePrinter.Name}: print failed");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = singlePrinter.Id,
                RouteName = singlePrinter.Name,
                Reason = "TCP send failed"
            });
        }

        return result;
    }

    private async Task<OrderRoutingPrintResult> HandleNormalRoutingAsync(TableOrder order, List<TableOrderItem> itemsToPrint, string? routeTarget)
    {
        var result = new OrderRoutingPrintResult();

        System.Diagnostics.Debug.WriteLine($"🔀 {itemsToPrint.Count} items → Normal routing (2+ printers)");

        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        if (activeGroups.Count == 0)
        {
            result.FailedRoutes.Add("No active print groups configured");
            return result;
        }

        var resolvedItems = itemsToPrint
            .Select(item => new
            {
                Item = item,
                Group = ResolvePrintGroup(item, activeGroups)
            })
            .ToList();

        foreach (var unresolved in resolvedItems.Where(entry => entry.Group == null))
        {
            result.FailedRoutes.Add($"{unresolved.Item.Name}: no explicit print group mapping");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = unresolved.Item.PrintGroupId ?? string.Empty,
                RouteName = unresolved.Item.Name,
                Reason = "no explicit print group mapping"
            });
        }

        foreach (var groupBatch in resolvedItems.Where(entry => entry.Group != null).GroupBy(x => x.Group!.Id))
        {
            var group = groupBatch.First().Group!;
            var batchItems = groupBatch.Select(x => x.Item).ToList();

            if (string.IsNullOrWhiteSpace(group.PrinterIp))
            {
                result.FailedRoutes.Add($"⚠️ {group.Name}: printer IP not configured");
                result.FailedRouteDetails.Add(new PrintRouteFailure
                {
                    RouteTarget = group.Id,
                    RouteName = group.Name,
                    Reason = "printer IP not configured"
                });
                continue;
            }

            var ticketData = BuildTicket(order, group, batchItems);
            var sent = await _printerService.SendRawDataAsync(group.PrinterIp, group.PrinterPort, ticketData);

            if (sent)
            {
                foreach (var item in batchItems)
                {
                    result.PrintedItemIds.Add(item.Id);
                }
            }
            else
            {
                result.FailedRoutes.Add($"{group.Name}: print failed");
                result.FailedRouteDetails.Add(new PrintRouteFailure
                {
                    RouteTarget = group.Id,
                    RouteName = group.Name,
                    Reason = "print failed"
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Determine the printer group type for an item (kitchen, bar, etc.)
    /// Used for grouping items into separate PDFs
    /// </summary>
    private string DetermineItemGroupType(TableOrderItem item, List<PrintGroup> groups)
    {
        if (!string.IsNullOrWhiteSpace(item.PrintGroupId))
        {
            var assignedGroup = groups.FirstOrDefault(g => string.Equals(g.Id, item.PrintGroupId, StringComparison.OrdinalIgnoreCase));
            if (assignedGroup != null)
            {
                return assignedGroup.PrinterType ?? "kitchen";
            }
        }

        return "kitchen"; // Default
    }

    private static PrintGroup? ResolvePrintGroup(TableOrderItem item, List<PrintGroup> groups)
    {
        if (!string.IsNullOrWhiteSpace(item.PrintGroupId))
        {
            var assignedGroup = groups.FirstOrDefault(group => string.Equals(group.Id, item.PrintGroupId, StringComparison.OrdinalIgnoreCase));
            if (assignedGroup != null)
            {
                return assignedGroup;
            }
        }

        return null;
    }

    private byte[] BuildTicket(TableOrder order, PrintGroup group, List<TableOrderItem> items)
    {
        var printerType = string.IsNullOrWhiteSpace(group.PrinterType) ? "kitchen" : group.PrinterType.Trim().ToLowerInvariant();
        var headerText = printerType switch
        {
            "bar" => "BAR",
            "receipt" => "RECEIPT",
            _ => "KITCHEN"
        };

        var builder = new EscPosBuilder(PrinterBrand.Epson, PaperWidth.Mm80);
        var lineWidth = 48;

        builder.Initialize();
        if (printerType == "kitchen")
        {
            builder.Buzzer();
        }

        builder.SetAlign(TextAlign.Center)
               .SetFontSize(2, 2)
               .SetBold(true)
               .PrintLine(headerText)
               .SetNormalSize()
               .SetBold(false)
               .PrintLine(group.Name)
               .PrintLine($"Order #{(string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber)}")
               .PrintLine($"Table {order.TableNumber}")
               .PrintLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
               .PrintLine(new string('=', lineWidth))
               .SetAlign(TextAlign.Left);

        foreach (var item in items)
        {
            builder.SetBold(true)
                   .PrintLine($"{item.Quantity}x {item.Name}")
                   .SetBold(false);

            foreach (var addon in item.SelectedAddons)
            {
                builder.PrintLine($"   + {addon.Name}");
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                builder.PrintLine($"   Note: {item.Notes}");
            }

            builder.FeedLines(1);
        }

        builder.PrintLine(new string('=', lineWidth))
               .SetAlign(TextAlign.Center)
               .PrintLine($"{group.Name} • {headerText}")
               .FeedLines(2);

        if (string.Equals(printerType, "bar", StringComparison.OrdinalIgnoreCase) || string.Equals(printerType, "kitchen", StringComparison.OrdinalIgnoreCase))
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    private byte[] BuildTakeawayTicket(TableOrder order, NetworkPrinter printer, List<TableOrderItem> items, string orderType)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        var lineWidth = printer.PaperWidth == PaperWidth.Mm80 ? 48 : 32;
        var orderReference = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber;
        var typeLabel = NormalizeTakeawayOrderType(orderType);

        builder.Initialize();

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        builder.SetAlign(TextAlign.Center)
               .SetFontSize(2, 2)
               .SetBold(true)
               .PrintLine("TAKEAWAY")
               .SetNormalSize()
               .SetBold(false)
               .SetFontSize(2, 1)
               .SetBold(true)
               .PrintLine($"[ {typeLabel} ]")
               .SetNormalSize()
               .SetBold(false)
               .PrintLine($"Order #{orderReference}")
               .PrintLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
               .PrintLine(new string('=', lineWidth))
               .SetAlign(TextAlign.Left);

        foreach (var item in items)
        {
            builder.SetFontSize(2, 1)
                   .SetBold(true)
                   .PrintLine($"{item.Quantity}x {item.Name}")
                   .SetNormalSize()
                   .SetBold(false);

            foreach (var addon in item.SelectedAddons)
            {
                builder.PrintLine($"   + {addon.Name}");
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                builder.SetBold(true)
                       .PrintLine($"   >> {item.Notes}")
                       .SetBold(false);
            }

            builder.FeedLines(1);
        }

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            builder.PrintLine(new string('-', lineWidth))
                   .SetBold(true)
                   .PrintLine("ORDER NOTES:")
                   .SetBold(false)
                   .PrintLine(order.Notes);
        }

        builder.PrintLine(new string('=', lineWidth))
               .SetAlign(TextAlign.Center)
               .PrintLine($"{typeLabel} • TAKEAWAY KITCHEN")
               .FeedLines(2);

        if (printer.HasCutter)
        {
            builder.Cut(true);
        }

        return builder.Build();
    }

    private static string NormalizeTakeawayOrderType(string orderType)
    {
        return orderType.Trim().ToLowerInvariant() switch
        {
            "delivery" => "DELIVERY",
            "pickup" or "collection" => "COLLECTION",
            _ => "TAKEAWAY"
        };
    }
}
