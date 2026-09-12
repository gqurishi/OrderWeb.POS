namespace POS_in_NET.Models;

/// <summary>
/// Certified label-printer model profiles. Administrators select a model;
/// protocol and hardware capabilities come from this application-owned catalog.
/// </summary>
public static class LabelPrinterProfiles
{
    public const string ToshibaManufacturer = "Toshiba";
    public const string ToshibaBfv4dGs14Model = "Toshiba B-FV4D-GS14 LAN";
    public const string ToshibaBfv4dGs14Code = "B-FV4D-GS14-QM-R";
    public const decimal ToshibaBfv4dMaximumPrintWidthMm = 108m;

    public static void ApplyToshibaBfv4dGs14(NetworkPrinter printer)
    {
        printer.Brand = PrinterBrand.Toshiba;
        printer.PrinterType = NetworkPrinterType.Label;
        printer.Technology = "Label Printer";
        printer.Manufacturer = ToshibaManufacturer;
        printer.ModelCode = ToshibaBfv4dGs14Code;
        printer.Protocol = "TPCL";
        printer.ResolutionDpi = 203;
        printer.PrintingMethod = "Direct thermal";
        printer.MaximumPrintWidthMm = ToshibaBfv4dMaximumPrintWidthMm;
        printer.SupportedMedia = "Gap, black mark and continuous";
        printer.Transport = "Raw TCP/IP";
        printer.WindowsDriver = "Toshiba B-FV4 203 dpi TPCL";
        printer.HasCashDrawer = false;
        printer.HasBuzzer = false;
        printer.SupportsTwoColor = false;
        printer.HasCutter = printer.FinishingMode == LabelFinishingMode.Cutter;
    }
}
