using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class ZReportPrintService
{
    private readonly ZReportService _zReportService;
    private readonly NetworkPrinterDatabaseService _printerDatabaseService;
    private readonly NetworkPrinterService _networkPrinterService;
    public ZReportPrintService(
        ZReportService zReportService,
        NetworkPrinterDatabaseService printerDatabaseService,
        NetworkPrinterService networkPrinterService)
    {
        _zReportService = zReportService;
        _printerDatabaseService = printerDatabaseService;
        _networkPrinterService = networkPrinterService;
    }

    public async Task<ZReportPrintResult> PrintAsync(
        ZReportSnapshot snapshot,
        bool includeDetailSlip,
        int? printedByUserId)
    {
        var printer = await ResolveReceiptPrinterAsync();
        if (printer == null)
        {
            var failure = new ZReportPrintResult
            {
                Success = false,
                Message = "No enabled receipt printer found. Configure a receipt printer in Printer Setup."
            };

            await _zReportService.LogPrintAsync(snapshot, null, includeDetailSlip, false, failure.Message, printedByUserId);
            return failure;
        }

        try
        {
            var summaryData = BuildSummarySlip(snapshot, printer);
            var summaryOk = await _networkPrinterService.SendToPrinterAsync(printer, summaryData);
            if (!summaryOk)
            {
                var failure = new ZReportPrintResult
                {
                    Success = false,
                    Message = $"Could not reach receipt printer '{printer.Name}'.",
                    PrinterName = printer.Name
                };

                await _zReportService.LogPrintAsync(snapshot, printer.Name, includeDetailSlip, false, failure.Message, printedByUserId);
                return failure;
            }

            var slipsPrinted = 1;

            if (includeDetailSlip && (snapshot.TopItems.Count > 0 || snapshot.ServiceChargeRemovals.Count > 0))
            {
                var detailData = BuildDetailSlip(snapshot, printer);
                var detailOk = await _networkPrinterService.SendToPrinterAsync(printer, detailData);
                if (detailOk)
                {
                    slipsPrinted++;
                }
            }

            var success = new ZReportPrintResult
            {
                Success = true,
                Message = slipsPrinted > 1
                    ? $"Z-Report printed to {printer.Name} (summary + detail)."
                    : $"Z-Report printed to {printer.Name}.",
                PrinterName = printer.Name,
                SlipsPrinted = slipsPrinted
            };

            await _zReportService.LogPrintAsync(snapshot, printer.Name, includeDetailSlip, true, null, printedByUserId);

            return success;
        }
        catch (Exception ex)
        {
            var failure = new ZReportPrintResult
            {
                Success = false,
                Message = ex.Message,
                PrinterName = printer.Name
            };

            await _zReportService.LogPrintAsync(snapshot, printer.Name, includeDetailSlip, false, ex.Message, printedByUserId);
            return failure;
        }
    }

    private async Task<NetworkPrinter?> ResolveReceiptPrinterAsync()
    {
        await _printerDatabaseService.EnsureTablesExistAsync();

        var routingService = ServiceHelper.GetService<PrinterRoutingService>();
        if (routingService != null)
        {
            return await routingService.ResolvePrinterAsync(
                NetworkPrinterType.Receipt,
                NetworkPrinterType.Online);
        }

        var receiptPrinters = await _printerDatabaseService.GetPrintersByTypeAsync(NetworkPrinterType.Receipt);
        var printer = receiptPrinters.FirstOrDefault(p => p.IsEnabled);
        if (printer != null)
        {
            return printer;
        }

        var onlinePrinters = await _printerDatabaseService.GetPrintersByTypeAsync(NetworkPrinterType.Online);
        return onlinePrinters.FirstOrDefault(p => p.IsEnabled);
    }

    private static byte[] BuildSummarySlip(ZReportSnapshot snapshot, NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        builder.Initialize()
            .SetAlign(TextAlign.Center)
            .SetBold(true)
            .SetFontSize(2, 2)
            .PrintLine("Z-REPORT")
            .SetNormalSize()
            .SetBold(false)
            .PrintLine(snapshot.BusinessName)
            .PrintLine(snapshot.DateDisplay)
            .PrintLine(snapshot.TerminalName)
            .PrintDivider('=');

        if (!string.IsNullOrWhiteSpace(snapshot.PrintedByName))
        {
            builder.SetAlign(TextAlign.Left)
                .PrintLine($"Printed by: {snapshot.PrintedByName}");
        }

        builder.PrintLine($"Printed: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
            .PrintLine($"Ref: {snapshot.ReportReference}");

        if (snapshot.IsReprint)
        {
            builder.PrintLine("*** REPRINT ***");
        }

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("SALES")
            .SetBold(false)
            .PrintColumns("Orders", snapshot.OrderCount.ToString())
            .PrintColumns("Gross", snapshot.GrossDisplay)
            .PrintColumns("Net", snapshot.NetDisplay)
            .PrintColumns("VAT", snapshot.VatDisplay)
            .PrintColumns("Item sales", FormatMoney(snapshot.ItemSales))
            .PrintColumns("Service charges", snapshot.ServiceChargeDisplay)
            .PrintColumns("Delivery fees", snapshot.DeliveryFeesDisplay)
            .PrintColumns("Average", snapshot.AvgDisplay)
            .PrintLine(snapshot.SalesVsYesterdayDisplay);

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("PAYMENTS")
            .SetBold(false)
            .PrintColumns("Cash", snapshot.CashDisplay)
            .PrintColumns("Card", snapshot.CardDisplay)
            .PrintColumns("Gift Card", snapshot.GiftCardDisplay)
            .PrintColumns("Cash tips", snapshot.CashTipsDisplay)
            .PrintColumns("Card tips", snapshot.CardTipsDisplay)
            .PrintColumns("Total tips", snapshot.TipsDisplay)
            .PrintColumns("Refunds", snapshot.RefundDisplay)
            .PrintColumns("Money collected", snapshot.FinalMoneyDisplay);

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("CHANNELS")
            .SetBold(false)
            .PrintColumns($"POS ({snapshot.PosOrderCount})", snapshot.PosDisplay)
            .PrintColumns($"Online ({snapshot.OnlineOrderCount})", snapshot.OnlineDisplay);

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("ORDER TYPES")
            .SetBold(false)
            .PrintColumns($"Table ({snapshot.TableOrderCount})", FormatMoney(snapshot.TableGrossSales))
            .PrintColumns($"Delivery ({snapshot.DeliveryOrderCount})", FormatMoney(snapshot.DeliveryGrossSales))
            .PrintColumns($"Pickup ({snapshot.PickupOrderCount})", FormatMoney(snapshot.PickupGrossSales));

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("TILL / PETTY CASH")
            .SetBold(false)
            .PrintColumns("Net out", snapshot.TillNetOutDisplay)
            .PrintColumns("Shopping", FormatMoney(snapshot.TillShoppingNet))
            .PrintColumns("Delivery", FormatMoney(snapshot.TillDeliveryTotal))
            .PrintColumns("Other", FormatMoney(snapshot.TillOtherTotal))
            .PrintColumns("Returned", FormatMoney(snapshot.TillCashReturned));

        if (snapshot.TillPendingShoppingCount > 0)
        {
            builder.PrintColumns(
                $"Pending ({snapshot.TillPendingShoppingCount})",
                FormatMoney(snapshot.TillPendingShoppingTotal));
        }

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("EXPECTED CASH")
            .SetBold(false)
            .PrintColumns("Opening float", FormatMoney(snapshot.OpeningTillFloat))
            .PrintColumns("Expected in till", snapshot.ExpectedCashDisplay);

        if (snapshot.LastCashCountAmount.HasValue)
        {
            builder.PrintColumns("Last count", FormatMoney(snapshot.LastCashCountAmount.Value));
            if (snapshot.LastCashCountAt.HasValue)
            {
                builder.PrintLine($"Counted: {snapshot.LastCashCountAt.Value:dd/MM HH:mm}");
            }

            if (snapshot.CashCountVariance.HasValue)
            {
                builder.PrintColumns("Variance", FormatMoney(snapshot.CashCountVariance.Value));
            }
        }

        builder.PrintDivider('-')
            .SetBold(true)
            .PrintLine("ADJUSTMENTS")
            .SetBold(false)
            .PrintColumns($"Discounts ({snapshot.DiscountEventCount})", snapshot.DiscountDisplay)
            .PrintColumns($"Service removed ({snapshot.RemovedServiceChargeCount})", snapshot.RemovedServiceChargeDisplay)
            .PrintColumns("Voids", snapshot.VoidCount.ToString());

        builder.PrintDivider('=')
            .SetAlign(TextAlign.Center)
            .PrintLine("END OF Z-REPORT")
            .PrintLine("POS-in-NET")
            .FeedLines(3)
            .Cut();

        return builder.Build();
    }

    private static byte[] BuildDetailSlip(ZReportSnapshot snapshot, NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        builder.Initialize()
            .SetAlign(TextAlign.Center)
            .SetBold(true)
            .PrintLine("Z-REPORT DETAIL")
            .SetNormalSize()
            .SetBold(false)
            .PrintLine(snapshot.DateDisplay)
            .PrintLine(snapshot.ReportReference)
            .PrintDivider('-')
            .SetAlign(TextAlign.Left)
            .SetBold(true)
            .PrintLine("TOP 5 ITEMS")
            .SetBold(false);

        foreach (var item in snapshot.TopItems)
        {
            builder.PrintColumns(item.DisplayLine, item.AmountDisplay);
        }

        if (snapshot.ServiceChargeRemovals.Count > 0)
        {
            builder.PrintDivider('-')
                .SetBold(true)
                .PrintLine("SERVICE CHARGE REMOVALS")
                .SetBold(false);

            foreach (var removal in snapshot.ServiceChargeRemovals)
            {
                builder.PrintColumns(
                    $"{removal.RemovedAt:HH:mm} {removal.OrderNumber}",
                    FormatMoney(removal.Amount));
                builder.PrintLine($"Reason: {removal.Reason}");
                builder.PrintLine($"Approved: {removal.ApprovedBy}");
            }
        }

        builder.PrintDivider('=')
            .SetAlign(TextAlign.Center)
            .PrintLine("DETAIL SLIP")
            .FeedLines(3)
            .Cut();

        return builder.Build();
    }

    private static string FormatMoney(decimal value) => $"£{value:F2}";
}
