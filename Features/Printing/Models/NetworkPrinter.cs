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
    
    // Features
    public bool HasCashDrawer { get; set; }
    public bool HasCutter { get; set; } = true;
    public bool HasBuzzer { get; set; }
    
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
    
    public string StatusDisplay => IsOnline ? "🟢 Online" : "🔴 Offline";
    
    public string TypeDisplay => PrinterType switch
    {
        NetworkPrinterType.Receipt => "Receipt",
        NetworkPrinterType.Kitchen => "Kitchen",
        NetworkPrinterType.Bar => "Bar",
        NetworkPrinterType.Label => "Label",
        NetworkPrinterType.Online => "Online",
        NetworkPrinterType.Takeaway => "Takeaway",
        _ => "Unknown"
    };
    
    public string BrandDisplay => Brand switch
    {
        PrinterBrand.Epson => "Epson",
        PrinterBrand.Star => "Star",
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
    Other
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
    Online,    // Online order customer receipts
    Takeaway   // Online order kitchen/takeaway tickets
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
    Failed
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
