namespace POS_in_NET.Models;

/// <summary>
/// Represents a network thermal printer configuration
/// </summary>
public class NetworkPrinter
{
    public int Id { get; set; }
    
    // Basic Info
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9100;
    
    // Configuration
    public PrinterBrand Brand { get; set; } = PrinterBrand.Epson;
    public NetworkPrinterType PrinterType { get; set; } = NetworkPrinterType.Receipt;
    public PaperWidth PaperWidth { get; set; } = PaperWidth.Mm80;
    public string? PrintGroupId { get; set; } // Link to print_groups table

    // Structured label-printer configuration. These values are ignored for
    // receipt/kitchen printers and are owned by Mother POS.
    public string? Technology { get; set; }
    public string? Manufacturer { get; set; }
    public string? ModelCode { get; set; }
    public string? Protocol { get; set; }
    public int? ResolutionDpi { get; set; }
    public string? PrintingMethod { get; set; }
    public decimal? MaximumPrintWidthMm { get; set; }
    public string? SupportedMedia { get; set; }
    public string? Transport { get; set; }
    public string? WindowsDriver { get; set; }
    public string? LabelProfile { get; set; }
    public decimal? MediaWidthMm { get; set; }
    public decimal? LabelWidthMm { get; set; }
    public decimal? LabelHeightMm { get; set; }
    public decimal? GapSizeMm { get; set; }
    public LabelSensorType? SensorType { get; set; }
    public int? PrintSpeed { get; set; }
    public int? PrintDarkness { get; set; }
    public decimal HorizontalOffsetMm { get; set; }
    public decimal VerticalOffsetMm { get; set; }
    public LabelFinishingMode FinishingMode { get; set; } = LabelFinishingMode.TearOff;
    public int NumberOfCopies { get; set; } = 1;
    public bool IsDefaultLabelPrinter { get; set; }
    
    // Features
    public bool HasCashDrawer { get; set; }
    public bool HasCutter { get; set; } = true;
    public bool HasBuzzer { get; set; }
    public bool SupportsTwoColor { get; set; }
    
    // Status
    public bool IsEnabled { get; set; } = true;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
    
    // UI
    public string ColorCode { get; set; } = "#6366F1";
    public int DisplayOrder { get; set; }
    public string? Notes { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Computed Properties
    public string ConnectionString => $"{IpAddress}:{Port}";
    
    public string StatusDisplay => IsOnline ? " Online" : " Offline";
    
    public string TypeDisplay => PrinterType switch
    {
        NetworkPrinterType.Receipt => "Receipt",
        NetworkPrinterType.Kitchen => "Kitchen",
        NetworkPrinterType.Bar => "Bar",
        NetworkPrinterType.Label => "Label",
        NetworkPrinterType.Online => "Online Receipt",
        NetworkPrinterType.Takeaway => "Takeaway Kitchen",
        _ => "Unknown"
    };
    
    public string BrandDisplay => Brand switch
    {
        PrinterBrand.Epson => "Epson",
        PrinterBrand.Star => "Star",
        PrinterBrand.Toshiba => "Toshiba",
        PrinterBrand.Other => "Other",
        _ => "Unknown"
    };
}

/// <summary>
/// Printer manufacturer brands
/// </summary>
public enum PrinterBrand
{
    Epson,
    Star,
    Toshiba,
    Other
}

public enum LabelSensorType
{
    Gap,
    BlackMark,
    Continuous
}

public enum LabelFinishingMode
{
    TearOff,
    Cutter
}

/// <summary>
/// Types of printers by purpose
/// </summary>
public enum NetworkPrinterType
{
    Receipt,   // Customer receipts, cash drawer
    Kitchen,   // Kitchen order tickets
    Bar,       // Bar/drinks orders
    Label,     // Food labels (different protocol)
    Online,    // OrderWeb customer receipts
    Takeaway   // OrderWeb kitchen/takeaway tickets
}

/// <summary>
/// Thermal paper width options
/// </summary>
public enum PaperWidth
{
    Mm58,  // 58mm (narrow)
    Mm80   // 80mm (standard)
}

/// <summary>
/// Print job for queue
/// </summary>
public class PrintJob
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public int? OrderId { get; set; }
    public PrintJobType JobType { get; set; }
    public byte[]? PrintData { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Navigation
    public NetworkPrinter? Printer { get; set; }
}

/// <summary>
/// Types of print jobs
/// </summary>
public enum PrintJobType
{
    Receipt,
    KitchenTicket,
    Test,
    CashDrawer
}

/// <summary>
/// Print job status
/// </summary>
public enum PrintJobStatus
{
    Pending,
    Printing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Result of a printer connection test
/// </summary>
public class PrinterConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ResponseTimeMs { get; set; }
    public string? PrinterModel { get; set; }
}

/// <summary>
/// Printer status from health check
/// </summary>
public class PrinterStatus
{
    public bool IsOnline { get; set; }
    public bool HasPaper { get; set; } = true;
    public bool CoverOpen { get; set; }
    public bool HasError { get; set; }
    public string? ErrorDescription { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
}
