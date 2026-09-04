using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Models.FoodMenu;
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

public sealed class TastingCoursePrintResult
{
    public bool FoodPrinted { get; init; }
    public bool WinePrinted { get; init; }
    public bool WineRequested { get; init; }
    public bool WineRedirected { get; init; }
    public bool CombinedOnSinglePrinter { get; init; }
    public string? WineDestination { get; init; }
    public OrderRoutingPrintResult RoutingResult { get; init; } = new();
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
    private readonly KitchenTemplateSettingsService _kitchenTemplateSettingsService;
    private readonly NetworkPrinterDatabaseService? _printerDatabaseService;
    private readonly NetworkPrintQueueService? _printQueueService;

    public OrderRoutingPrintService()
    {
        _printGroupService = new PrintGroupService();
        _printerService = new NetworkPrinterService();
        _pdfPrintService = new PdfPrintService();
        _printerDatabaseService = ServiceHelper.GetService<NetworkPrinterDatabaseService>();
        _printQueueService = ServiceHelper.GetService<NetworkPrintQueueService>();
        _kitchenTemplateSettingsService = ServiceHelper.GetService<KitchenTemplateSettingsService>()
            ?? new KitchenTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
    }

    /// <summary>
    /// Detect the current printing mode based on configured printers
    /// </summary>
    private async Task<(PrintingMode Mode, PrintGroup? SinglePrinter, string Status)> DetectPrintingModeAsync()
    {
        var routingService = ServiceHelper.GetService<PrinterRoutingService>();
        if (routingService != null)
        {
            var settings = await routingService.GetSettingsAsync();
            if (settings.UseAllJobsPrinter)
            {
                var printer = await routingService.GetAllJobsPrinterAsync();
                if (printer == null)
                {
                    return (PrintingMode.MissingAllJobsPrinter, null, "The all-jobs printer is unavailable");
                }

                var destination = new PrintGroup
                {
                    Id = $"all-jobs-{printer.Id}",
                    Name = printer.Name,
                    PrinterIp = printer.IpAddress,
                    PrinterPort = printer.Port,
                    PrinterType = "kitchen",
                    IsActive = true
                };
                return (PrintingMode.SinglePrinterCatchAll, destination, $"All jobs printer - {printer.Name}");
            }
        }

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
        MissingAllJobsPrinter,
        PdfFallback,           // No printers - save as PDF
        SinglePrinterCatchAll, // 1 printer - catch all items
        NormalRouting          // 2+ printers - route per group
    }

    public async Task<List<RouteValidationIssue>> ValidateEnvironmentAsync()
    {
        var issues = new List<RouteValidationIssue>();
        var (mode, _, _) = await DetectPrintingModeAsync();

        if (mode == PrintingMode.MissingAllJobsPrinter)
        {
            issues.Add(new RouteValidationIssue
            {
                Code = "all_jobs_printer_unavailable",
                Message = "The selected all-jobs printer is unavailable or disabled"
            });
            return issues;
        }

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
                Message = $" {group.Name}: printer IP not configured"
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
        System.Diagnostics.Debug.WriteLine($" Printing mode: {modeStatus}");

        if (mode == PrintingMode.MissingAllJobsPrinter)
        {
            result.FailedRoutes.Add("The selected all-jobs printer is unavailable or disabled");
            return result;
        }

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

    public async Task<TastingCoursePrintResult> PrintTastingCourseAsync(
        TableOrder source,
        TableOrderItem package,
        TableOrderItem course)
    {
        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        var routableGroups = activeGroups
            .Where(group => !string.IsNullOrWhiteSpace(group.PrinterIp))
            .ToList();
        var availableGroups = routableGroups.Count > 0 ? routableGroups : activeGroups;
        var configuredFoodGroupId = TastingMenuNotesHelper.GetFoodPrintGroupId(package.Notes);
        var configuredWineGroupId = TastingMenuNotesHelper.GetWinePrintGroupId(package.Notes);
        var singleGroup = routableGroups.Count == 1 ? routableGroups[0] : null;
        var foodGroup = singleGroup
            ?? ResolvePreferredTastingGroup(availableGroups, configuredFoodGroupId, "kitchen")
            ?? availableGroups.FirstOrDefault();
        var wineGroup = singleGroup
            ?? ResolvePreferredTastingGroup(availableGroups, configuredWineGroupId, "bar")
            ?? foodGroup;
        var courseNumber = course.VariantName?.Split('/', StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var courseCount = course.VariantName?.Split('/', StringSplitOptions.TrimEntries).Skip(1).FirstOrDefault() ?? "";
        var fireTitle = !string.IsNullOrWhiteSpace(courseNumber) && !string.IsNullOrWhiteSpace(courseCount)
            ? $"FIRE COURSE {courseNumber} OF {courseCount}"
            : "FIRE TASTING COURSE";
        var foodItemId = $"{course.Id}::tasting-food";
        var wineItemId = $"{course.Id}::tasting-wine";
        var includesWine = TastingMenuIncludesWine(package);

        var fireOrder = new TableOrder
        {
            Id = source.Id,
            OrderNumber = source.OrderNumber,
            TableNumber = source.TableNumber,
            CoverCount = source.CoverCount,
            StaffName = source.StaffName,
            StaffId = source.StaffId,
            StartTime = source.StartTime,
            CreatedAt = source.CreatedAt,
            UpdatedAt = DateTime.Now,
            OrderMode = source.OrderMode,
            Status = source.Status,
            KitchenTicketType = fireTitle
        };

        fireOrder.Items.Add(new TableOrderItem
        {
            Id = foodItemId,
            OrderId = source.Id,
            MenuItemId = string.Empty,
            DisplayName = course.Name,
            Name = course.Name,
            Quantity = Math.Max(1, course.Quantity),
            UnitPrice = 0m,
            VatCategory = course.VatCategory,
            PrintGroupId = foodGroup?.Id ?? course.PrintGroupId,
            PrintInRed = course.PrintInRed,
            Notes = course.Notes,
            CourseType = fireTitle,
            SendStatus = ItemSendStatus.NotSent,
            KitchenAction = KitchenChangeAction.New,
            CreatedAt = DateTime.Now
        });

        if (includesWine)
        {
            fireOrder.Items.Add(new TableOrderItem
            {
                Id = wineItemId,
                OrderId = source.Id,
                MenuItemId = string.Empty,
                DisplayName = GetWinePairingName(course) ?? $"Wine Pairing - {course.Name}",
                Name = GetWinePairingName(course) ?? $"Wine Pairing - {course.Name}",
                Quantity = Math.Max(1, course.Quantity),
                UnitPrice = 0m,
                VatCategory = "Alcohol",
                PrintGroupId = wineGroup?.Id ?? foodGroup?.Id,
                Notes = package.DisplayName,
                CourseType = fireTitle,
                SendStatus = ItemSendStatus.NotSent,
                KitchenAction = KitchenChangeAction.New,
                CreatedAt = DateTime.Now
            });
        }

        var routing = await PrintOrderAsync(fireOrder);
        var foodPrinted = routing.PrintedItemIds.Contains(foodItemId);
        var winePrinted = !includesWine || routing.PrintedItemIds.Contains(wineItemId);
        var runtimeWineRedirected = false;
        if (includesWine
            && foodPrinted
            && !winePrinted
            && foodGroup != null
            && wineGroup != null
            && !string.Equals(foodGroup.Id, wineGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            var wineItem = fireOrder.Items.First(item => string.Equals(item.Id, wineItemId, StringComparison.OrdinalIgnoreCase));
            wineItem.PrintGroupId = foodGroup.Id;
            var fallbackOrder = new TableOrder
            {
                Id = source.Id,
                OrderNumber = source.OrderNumber,
                TableNumber = source.TableNumber,
                CoverCount = source.CoverCount,
                StaffName = source.StaffName,
                StaffId = source.StaffId,
                StartTime = source.StartTime,
                CreatedAt = source.CreatedAt,
                UpdatedAt = DateTime.Now,
                OrderMode = source.OrderMode,
                Status = source.Status,
                KitchenTicketType = fireTitle
            };
            fallbackOrder.Items.Add(wineItem);

            var fallbackRouting = await PrintOrderAsync(fallbackOrder);
            winePrinted = fallbackRouting.PrintedItemIds.Contains(wineItemId);
            runtimeWineRedirected = winePrinted;
            foreach (var printedId in fallbackRouting.PrintedItemIds)
            {
                routing.PrintedItemIds.Add(printedId);
            }
            if (!winePrinted)
            {
                routing.FailedRoutes.AddRange(fallbackRouting.FailedRoutes);
                routing.FailedRouteDetails.AddRange(fallbackRouting.FailedRouteDetails);
            }
        }

        var wineRedirected = includesWine
            && (runtimeWineRedirected
                || (singleGroup == null
                    && wineGroup != null
                    && (!string.IsNullOrWhiteSpace(configuredWineGroupId)
                        ? !string.Equals(wineGroup.Id, configuredWineGroupId, StringComparison.OrdinalIgnoreCase)
                        : !string.Equals(wineGroup.PrinterType, "bar", StringComparison.OrdinalIgnoreCase))));
        return new TastingCoursePrintResult
        {
            FoodPrinted = foodPrinted,
            WineRequested = includesWine,
            WinePrinted = winePrinted,
            WineRedirected = wineRedirected,
            CombinedOnSinglePrinter = includesWine && singleGroup != null,
            WineDestination = runtimeWineRedirected ? foodGroup?.Name : wineGroup?.Name,
            RoutingResult = routing
        };
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

        var routingService = ServiceHelper.GetService<PrinterRoutingService>();
        // Prefer the dedicated takeaway route. A normal kitchen printer is the
        // next-best destination, while receipt/online printers provide the
        // one-printer fallback used by smaller sites. The kitchen ticket has
        // its own cut command, so two copies remain physically separated when
        // the receipt and kitchen destinations are the same device.
        var takeawayPrinter = routingService != null
            ? await routingService.ResolvePrinterAsync(
                NetworkPrinterType.Takeaway,
                NetworkPrinterType.Kitchen,
                NetworkPrinterType.Receipt,
                NetworkPrinterType.Online)
            : await ResolveTakeawayFallbackPrinterAsync(printerDb);

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

        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        var printGroupNames = activeGroups
            .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        var kitchenTemplateSettings = await _kitchenTemplateSettingsService.GetSettingsAsync();
        var ticketData = BuildTakeawayTicket(order, takeawayPrinter, itemsToPrint, orderType, printGroupNames, kitchenTemplateSettings);
        var sent = await QueueKitchenTicketAsync(
            takeawayPrinter.Id,
            ticketData,
            "kitchen_takeaway",
            order.Id);

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
                Reason = "could not add ticket to the durable print queue"
            });
        }

        return result;
    }

    private async Task<OrderRoutingPrintResult> HandlePdfFallbackAsync(TableOrder order, List<TableOrderItem> itemsToPrint)
    {
        var result = new OrderRoutingPrintResult();
        
        System.Diagnostics.Debug.WriteLine($" {itemsToPrint.Count} items → PDF fallback (no printers)");

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
                System.Diagnostics.Debug.WriteLine($" PDF saved: {pdfMsg}");
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

        System.Diagnostics.Debug.WriteLine($" {itemsToPrint.Count} items → Single printer catch-all ({singlePrinter.Name})");

        if (string.IsNullOrWhiteSpace(singlePrinter.PrinterIp))
        {
            result.FailedRoutes.Add($"{singlePrinter.Name}: printer IP not configured");
            return result;
        }

        var kitchenTemplateSettings = await _kitchenTemplateSettingsService.GetSettingsAsync();
        var printerConfig = await ResolveNetworkPrinterAsync(singlePrinter.PrinterIp, singlePrinter.PrinterPort);
        var ticketData = BuildTicket(order, singlePrinter, itemsToPrint, kitchenTemplateSettings, printerConfig);
        var sent = await QueueKitchenTicketAsync(
            singlePrinter.PrinterIp,
            singlePrinter.PrinterPort,
            ticketData,
            ResolveKitchenJobType(singlePrinter.PrinterType),
            order.Id);

        if (sent)
        {
            foreach (var item in itemsToPrint)
            {
                result.PrintedItemIds.Add(item.Id);
            }
            System.Diagnostics.Debug.WriteLine($" Printed to {singlePrinter.Name}");
        }
        else
        {
            result.FailedRoutes.Add($"{singlePrinter.Name}: print failed");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = singlePrinter.Id,
                RouteName = singlePrinter.Name,
                Reason = "printer is not registered or the ticket could not be queued"
            });
        }

        return result;
    }

    private async Task<OrderRoutingPrintResult> HandleNormalRoutingAsync(TableOrder order, List<TableOrderItem> itemsToPrint, string? routeTarget)
    {
        var result = new OrderRoutingPrintResult();

        System.Diagnostics.Debug.WriteLine($" {itemsToPrint.Count} items → Normal routing (2+ printers)");

        var activeGroups = await _printGroupService.GetActivePrintGroupsAsync();
        if (activeGroups.Count == 0)
        {
            result.FailedRoutes.Add("No active print groups configured");
            return result;
        }

        var expandedItems = itemsToPrint
            .SelectMany(item => ExpandTastingMenuPrintItems(item, activeGroups, result))
            .ToList();
        var kitchenTemplateSettings = await _kitchenTemplateSettingsService.GetSettingsAsync();
        var splitPrintItemIds = expandedItems
            .Where(item => TryGetTastingMenuSourceItemId(item.Id, out _))
            .GroupBy(item =>
            {
                TryGetTastingMenuSourceItemId(item.Id, out var sourceItemId);
                return sourceItemId;
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        var resolvedItems = expandedItems
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
                result.FailedRoutes.Add($" {group.Name}: printer IP not configured");
                result.FailedRouteDetails.Add(new PrintRouteFailure
                {
                    RouteTarget = group.Id,
                    RouteName = group.Name,
                    Reason = "printer IP not configured"
                });
                continue;
            }

            var printerConfig = await ResolveNetworkPrinterAsync(group.PrinterIp, group.PrinterPort);
            var ticketData = BuildTicket(order, group, batchItems, kitchenTemplateSettings, printerConfig);
            var sent = await QueueKitchenTicketAsync(
                group.PrinterIp,
                group.PrinterPort,
                ticketData,
                ResolveKitchenJobType(group.PrinterType),
                order.Id);

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
                    Reason = "printer is not registered or the ticket could not be queued"
                });
            }
        }

        ConsolidateSplitPrintResults(result, splitPrintItemIds);
        return result;
    }

    private static async Task<NetworkPrinter?> ResolveTakeawayFallbackPrinterAsync(
        NetworkPrinterDatabaseService printerDb)
    {
        foreach (var printerType in new[]
                 {
                     NetworkPrinterType.Takeaway,
                     NetworkPrinterType.Kitchen,
                     NetworkPrinterType.Receipt,
                     NetworkPrinterType.Online
                 })
        {
            var printer = (await printerDb.GetPrintersByTypeAsync(printerType))
                .FirstOrDefault(candidate => candidate.IsEnabled);
            if (printer != null)
            {
                return printer;
            }
        }

        return null;
    }

    private async Task<bool> QueueKitchenTicketAsync(
        string printerIp,
        int printerPort,
        byte[] ticketData,
        string jobType,
        string? orderId)
    {
        if (_printerDatabaseService == null || _printQueueService == null)
        {
            return false;
        }

        var printer = (await _printerDatabaseService.GetAllPrintersAsync())
            .FirstOrDefault(candidate =>
                candidate.IsEnabled
                && string.Equals(candidate.IpAddress?.Trim(), printerIp.Trim(), StringComparison.OrdinalIgnoreCase)
                && candidate.Port == printerPort);

        return printer != null
            && await QueueKitchenTicketAsync(printer.Id, ticketData, jobType, orderId);
    }

    private async Task<bool> QueueKitchenTicketAsync(
        int printerId,
        byte[] ticketData,
        string jobType,
        string? orderId)
    {
        if (_printQueueService == null || printerId <= 0)
        {
            return false;
        }

        try
        {
            var jobId = await _printQueueService.EnqueueAsync(printerId, ticketData, jobType, orderId);
            return jobId > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Kitchen ticket queue failed for printer #{printerId}: {ex.Message}");
            return false;
        }
    }

    private static string ResolveKitchenJobType(string? printerType) =>
        string.Equals(printerType, "bar", StringComparison.OrdinalIgnoreCase)
            ? "bar"
            : "kitchen";

    private static IEnumerable<TableOrderItem> ExpandTastingMenuPrintItems(
        TableOrderItem item,
        List<PrintGroup> activeGroups,
        OrderRoutingPrintResult result)
    {
        if (IsMealDealOrderItem(item))
        {
            var mealKitchenGroup = FindFirstGroupByType(activeGroups, "kitchen");
            if (mealKitchenGroup == null)
            {
                result.FailedRoutes.Add($"{item.DisplayName}: kitchen print group not configured");
                result.FailedRouteDetails.Add(new PrintRouteFailure
                {
                    RouteTarget = string.Empty,
                    RouteName = item.DisplayName ?? item.Name,
                    Reason = "kitchen print group not configured"
                });
                yield return CreateTastingMenuStationItem(
                    item,
                    "__missing_kitchen__",
                    item.DisplayName ?? item.Name,
                    BuildMealDealKitchenNotes(item));
            }
            else
            {
                yield return CreateTastingMenuStationItem(
                    item,
                    mealKitchenGroup.Id,
                    item.DisplayName ?? item.Name,
                    BuildMealDealKitchenNotes(item));
            }

            yield break;
        }

        if (!IsTastingMenuOrderItem(item))
        {
            yield return item;
            yield break;
        }

        var configuredFoodGroupId = TastingMenuNotesHelper.GetFoodPrintGroupId(item.Notes);
        var kitchenGroup = ResolvePreferredTastingGroup(activeGroups, configuredFoodGroupId, "kitchen");
        if (kitchenGroup == null)
        {
            result.FailedRoutes.Add($"{item.DisplayName}: kitchen print group not configured");
            result.FailedRouteDetails.Add(new PrintRouteFailure
            {
                RouteTarget = string.Empty,
                RouteName = item.DisplayName ?? item.Name,
                Reason = "kitchen print group not configured"
            });
            yield return CreateTastingMenuStationItem(item, "__missing_kitchen__", item.DisplayName ?? item.Name, BuildTastingKitchenNotes(item));
        }
        else
        {
            yield return CreateTastingMenuStationItem(item, kitchenGroup.Id, item.DisplayName ?? item.Name, BuildTastingKitchenNotes(item));
        }

        // Wine packages are sent to the bar only when a specific course is fired.
        // The initial package ticket is a kitchen HOLD notice.
    }

    private static TableOrderItem CreateTastingMenuStationItem(TableOrderItem source, string printGroupId, string displayName, string? notes)
    {
        return new TableOrderItem
        {
            Id = $"{source.Id}::{NormalizePrintGroupSuffix(printGroupId)}",
            OrderId = source.OrderId,
            MenuItemId = source.MenuItemId,
            VariantId = source.VariantId,
            VariantName = source.VariantName,
            Name = displayName,
            DisplayName = displayName,
            Quantity = source.Quantity,
            UnitPrice = 0m,
            VatCategory = source.VatCategory,
            PrintGroupId = printGroupId,
            PrintInRed = source.PrintInRed,
            Notes = notes,
            SendStatus = source.SendStatus,
            SentAt = source.SentAt,
            FailureReason = source.FailureReason,
            CreatedAt = source.CreatedAt
        };
    }

    private static string NormalizePrintGroupSuffix(string printGroupId)
    {
        var safe = new string(printGroupId.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "route" : safe;
    }

    private static bool TryGetTastingMenuSourceItemId(string itemId, out string sourceItemId)
    {
        const string marker = "::";
        var markerIndex = itemId.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex > 0)
        {
            sourceItemId = itemId[..markerIndex];
            return true;
        }

        sourceItemId = string.Empty;
        return false;
    }

    private static void ConsolidateSplitPrintResults(OrderRoutingPrintResult result, Dictionary<string, List<string>> splitPrintItemIds)
    {
        if (splitPrintItemIds.Count == 0)
        {
            return;
        }

        var printedSnapshot = result.PrintedItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var splitItemId in splitPrintItemIds.Values.SelectMany(ids => ids))
        {
            result.PrintedItemIds.Remove(splitItemId);
        }

        foreach (var entry in splitPrintItemIds)
        {
            if (entry.Value.Count > 0 && entry.Value.All(printedSnapshot.Contains))
            {
                result.PrintedItemIds.Add(entry.Key);
            }
        }
    }

    private static bool IsTastingMenuOrderItem(TableOrderItem item) =>
        item.MenuItemId.StartsWith("tasting:", StringComparison.OrdinalIgnoreCase);

    private static bool IsMealDealOrderItem(TableOrderItem item) =>
        item.MenuItemId.StartsWith("mealdeal:", StringComparison.OrdinalIgnoreCase);

    private static string BuildMealDealKitchenNotes(TableOrderItem item)
    {
        var selections = (item.Notes ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(selections)
            ? "MEAL DEAL"
            : $"MEAL DEAL{Environment.NewLine}{selections}";
    }

    private static PrintGroup? FindFirstGroupByType(IEnumerable<PrintGroup> groups, string printerType) =>
        groups
            .Where(group => group.IsActive)
            .OrderBy(group => group.DisplayOrder)
            .FirstOrDefault(group => string.Equals(group.PrinterType, printerType, StringComparison.OrdinalIgnoreCase));

    private static PrintGroup? ResolvePreferredTastingGroup(
        IEnumerable<PrintGroup> groups,
        string? configuredGroupId,
        string defaultPrinterType)
    {
        var candidates = groups
            .Where(group => group.IsActive)
            .OrderBy(group => group.DisplayOrder)
            .ToList();
        if (!string.IsNullOrWhiteSpace(configuredGroupId))
        {
            var configured = candidates.FirstOrDefault(group =>
                string.Equals(group.Id, configuredGroupId, StringComparison.OrdinalIgnoreCase));
            if (configured != null)
            {
                return configured;
            }
        }

        return candidates.FirstOrDefault(group =>
            string.Equals(group.PrinterType, defaultPrinterType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TastingMenuIncludesWine(TableOrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.VariantName)
            && item.VariantName.Contains("wine", StringComparison.OrdinalIgnoreCase)
            && !item.VariantName.Contains("without wine", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (item.Notes ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line => line.Trim().Equals("Wine pairing: Yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTastingKitchenNotes(TableOrderItem item)
    {
        var noteLines = (item.Notes ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();
        var packageLine = noteLines
            .FirstOrDefault(line => line.StartsWith("Package:", StringComparison.OrdinalIgnoreCase));
        var orderNote = noteLines
            .FirstOrDefault(line => line.StartsWith("Order note:", StringComparison.OrdinalIgnoreCase));

        return string.Join(
            Environment.NewLine,
            new[] { packageLine, orderNote, "NEW TASTING MENU - HOLD", "WAIT FOR COURSES TO BE FIRED" }
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool IsWinePairingLine(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().StartsWith("Wine pairing:", StringComparison.OrdinalIgnoreCase);

    private static string? GetWinePairingName(TableOrderItem course)
    {
        if (!IsWinePairingLine(course.Notes))
        {
            return null;
        }

        var separator = course.Notes!.IndexOf(':');
        var wineName = separator >= 0 ? course.Notes[(separator + 1)..].Trim() : string.Empty;
        return string.IsNullOrWhiteSpace(wineName) ? null : wineName;
    }

    private static string BuildTastingBarNotes(TableOrderItem item)
    {
        var packageLine = (item.Notes ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Package:", StringComparison.OrdinalIgnoreCase));

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(packageLine))
        {
            parts.Add(packageLine);
        }

        parts.Add("Wine pairing required");
        return string.Join(Environment.NewLine, parts);
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

    private byte[] BuildTicket(
        TableOrder order,
        PrintGroup group,
        List<TableOrderItem> items,
        KitchenTemplateSettings kitchenTemplateSettings,
        NetworkPrinter? printerConfig)
    {
        if (!string.IsNullOrWhiteSpace(order.KitchenTicketType)
            && order.KitchenTicketType.StartsWith("FIRE ", StringComparison.OrdinalIgnoreCase))
        {
            return KitchenTicketTemplateService.BuildCourseFireCallTicket(order, items, printerConfig);
        }

        var printerType = string.IsNullOrWhiteSpace(group.PrinterType) ? "kitchen" : group.PrinterType.Trim().ToLowerInvariant();
        if (printerType == "kitchen")
        {
            return KitchenTicketTemplateService.BuildTableSectionTickets(order, group, items, kitchenTemplateSettings, printerConfig);
        }

        var defaultHeaderText = printerType switch
        {
            "bar" => "BAR",
            "receipt" => "RECEIPT",
            _ => "KITCHEN"
        };
        var headerText = !string.IsNullOrWhiteSpace(order.KitchenTicketType)
            && order.KitchenTicketType.StartsWith("FIRE ", StringComparison.OrdinalIgnoreCase)
                ? order.KitchenTicketType.ToUpperInvariant()
                : defaultHeaderText;
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
            var actionPrefix = item.KitchenAction switch
            {
                KitchenChangeAction.Void => "VOID ",
                _ => string.Empty
            };
            builder.SetBold(true)
                   .PrintLine($"{actionPrefix}{item.Quantity}x {item.DisplayName}")
                   .SetBold(false);

            if (item.KitchenAction == KitchenChangeAction.Void)
            {
                builder.FeedLines(1);
                continue;
            }

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

    private async Task<NetworkPrinter?> ResolveNetworkPrinterAsync(string printerIp, int printerPort)
    {
        if (_printerDatabaseService == null)
        {
            return null;
        }

        var printers = await _printerDatabaseService.GetAllPrintersAsync();
        return printers.FirstOrDefault(printer =>
            printer.IsEnabled
            && printer.Port == printerPort
            && string.Equals(printer.IpAddress, printerIp, StringComparison.OrdinalIgnoreCase));
    }

    private byte[] BuildTakeawayTicket(
        TableOrder order,
        NetworkPrinter printer,
        List<TableOrderItem> items,
        string orderType,
        IReadOnlyDictionary<string, string> printGroupNames,
        KitchenTemplateSettings kitchenTemplateSettings)
    {
        return KitchenTicketTemplateService.BuildTakeawayKitchenTicket(order, printer, items, orderType, printGroupNames, kitchenTemplateSettings);

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
