namespace POS_in_NET.Models;

/// <summary>
/// Certified label-printer model profiles. Administrators select a model;
/// protocol and hardware capabilities come from this application-owned catalog.
/// </summary>
public static class LabelPrinterProfiles
{
    public const string ToshibaManufacturer = "toshiba";
    public const string ToshibaBfv4dGs14Model = "Toshiba B-FV4D-GS14 LAN";
    public const string ToshibaBfv4dGs14Code = "b-fv4d-gs14";
    public const decimal ToshibaBfv4dMaximumPrintWidthMm = 108m;

    public const string XprinterManufacturer = "xprinter";
    public const string XprinterXp421bModel = "Xprinter XP-421B LAN";
    public const string XprinterXp421bCode = "xp-421b";
    public const string XprinterTsplModuleId = "xprinter_tspl";
    public const decimal XprinterXp421bMaximumPrintWidthMm = 108m;

    public const string BrotherManufacturer = "brother";
    public const string BrotherTd4420DnModel = "Brother TD-4420DN";
    public const string BrotherTd4420DnCode = "td-4420dn";
    public const string BrotherRasterModuleId = "brother_raster";
    public const decimal BrotherTd4420DnMaximumPrintWidthMm = 104m;

    public static void ApplyToshibaBfv4dGs14(NetworkPrinter printer)
    {
        printer.Brand = PrinterBrand.Toshiba;
        printer.PrinterType = NetworkPrinterType.Label;
        printer.Technology = "label";
        printer.Manufacturer = ToshibaManufacturer;
        printer.ModelCode = ToshibaBfv4dGs14Code;
        printer.Protocol = "TPCL";
        printer.ModuleIdentifier = "toshiba_tpcl";
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

    public static void ApplyXp421bLan(NetworkPrinter printer)
    {
        printer.Brand = PrinterBrand.Xprinter;
        printer.PrinterType = NetworkPrinterType.Label;
        printer.Technology = "label";
        printer.Manufacturer = XprinterManufacturer;
        printer.ModelCode = XprinterXp421bCode;
        printer.Protocol = "TSPL";
        printer.ModuleIdentifier = XprinterTsplModuleId;
        printer.ResolutionDpi = 203;
        printer.PrintingMethod = "Direct thermal";
        printer.MaximumPrintWidthMm = XprinterXp421bMaximumPrintWidthMm;
        printer.SupportedMedia = "Gap, black mark and continuous";
        printer.Transport = "Raw TCP/IP";
        printer.WindowsDriver = "Xprinter XP-421B setup tool (fallback only)";
        printer.HasCashDrawer = false;
        printer.HasBuzzer = false;
        printer.SupportsTwoColor = false;
        printer.HasCutter = printer.FinishingMode == LabelFinishingMode.Cutter;
    }

    public static void ApplyTd4420Dn(NetworkPrinter printer)
    {
        printer.Brand = PrinterBrand.Brother;
        printer.PrinterType = NetworkPrinterType.Label;
        printer.Technology = "label";
        printer.Manufacturer = BrotherManufacturer;
        printer.ModelCode = BrotherTd4420DnCode;
        printer.Protocol = "Brother Raster";
        printer.ModuleIdentifier = BrotherRasterModuleId;
        printer.ResolutionDpi = 203;
        printer.PrintingMethod = "Direct thermal";
        printer.MaximumPrintWidthMm = BrotherTd4420DnMaximumPrintWidthMm;
        printer.SupportedMedia = "Gap, black mark and continuous";
        printer.Transport = "Raw TCP/IP";
        printer.WindowsDriver = "Brother TD-4420DN setup tool (fallback only)";
        printer.HasCashDrawer = false;
        printer.HasBuzzer = false;
        printer.SupportsTwoColor = false;
        printer.HasCutter = printer.FinishingMode == LabelFinishingMode.Cutter;
    }

    public static string? MediaProfileId(PrinterBrand brand, int profileIndex) => (brand, profileIndex) switch
    {
        (PrinterBrand.Toshiba, 0) => "toshiba-60x40-container",
        (PrinterBrand.Toshiba, 1) => "toshiba-51x30-compact",
        (PrinterBrand.Toshiba, 2) => "toshiba-80x50-delivery",
        (PrinterBrand.Xprinter, 0) => "xprinter-60x40-container",
        (PrinterBrand.Xprinter, 1) => "xprinter-51x30-compact",
        (PrinterBrand.Xprinter, 2) => "xprinter-80x50-delivery",
        (PrinterBrand.Brother, 0) => "brother-60x40-container",
        (PrinterBrand.Brother, 1) => "brother-51x30-compact",
        (PrinterBrand.Brother, 2) => "brother-80x50-delivery",
        _ => null
    };
}
