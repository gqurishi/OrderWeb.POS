using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyFirstMauiApp.Models;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Fallback service that generates PDF prints when no physical printers are configured.
/// Used for testing and offline mode.
/// </summary>
public sealed class PdfPrintService
{
    private readonly string _pdfOutputDirectory;

    public PdfPrintService()
    {
        _pdfOutputDirectory = Path.Combine(FileSystem.AppDataDirectory, "OrderPrints");
        Directory.CreateDirectory(_pdfOutputDirectory);
    }

    /// <summary>
    /// Generate a print-ready PDF for an order to a specific group
    /// Saves as text-based format that can be printed or imported into PDF converters
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> GeneratePrintAsync(
        TableOrder order, PrintGroup group, List<TableOrderItem> items)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var orderRef = string.IsNullOrWhiteSpace(order.OrderNumber) 
                ? order.Id 
                : order.OrderNumber;
            
            var fileName = $"Order_{orderRef}_{group.Name}_{timestamp}.txt";
            var filePath = Path.Combine(_pdfOutputDirectory, fileName);

            var content = BuildPrintContent(order, group, items);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine($" PDF print saved: {filePath}");
            
            return (true, filePath, $"Print saved: {fileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error generating PDF print: {ex.Message}");
            return (false, string.Empty, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all saved prints for review
    /// </summary>
    public List<(string FileName, DateTime Created, string Path)> GetSavedPrints()
    {
        try
        {
            if (!Directory.Exists(_pdfOutputDirectory))
            {
                return new List<(string, DateTime, string)>();
            }

            var prints = Directory.GetFiles(_pdfOutputDirectory, "Order_*.txt")
                .Select(filePath => {
                    var fileInfo = new FileInfo(filePath);
                    return (fileInfo.Name, fileInfo.CreationTime, filePath);
                })
                .OrderByDescending(x => x.CreationTime)
                .ToList();

            return prints;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting saved prints: {ex.Message}");
            return new List<(string, DateTime, string)>();
        }
    }

    /// <summary>
    /// Build print content as formatted text (ESC/POS-like format for reference)
    /// </summary>
    private string BuildPrintContent(TableOrder order, PrintGroup group, List<TableOrderItem> items)
    {
        var sb = new StringBuilder();
        var lineWidth = 48;
        var separator = new string('=', lineWidth);

        var printerType = string.IsNullOrWhiteSpace(group.PrinterType) 
            ? "KITCHEN" 
            : group.PrinterType.Trim().ToUpperInvariant();

        sb.AppendLine($"{"".PadCenter(lineWidth)}");
        sb.AppendLine($"{printerType.PadCenter(lineWidth)}");
        sb.AppendLine($"{group.Name.PadCenter(lineWidth)}");
        sb.AppendLine($"Order #{(string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id : order.OrderNumber)}");
        sb.AppendLine($"Table {order.TableNumber}");
        sb.AppendLine($"{DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine(separator);
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.AppendLine($"{item.Quantity}x {item.Name}");

            foreach (var addon in item.SelectedAddons)
            {
                sb.AppendLine($"   + {addon.Name}");
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                sb.AppendLine($"   Note: {item.Notes}");
            }

            sb.AppendLine();
        }

        sb.AppendLine(separator);
        sb.AppendLine($"{group.Name} • {printerType}");
        sb.AppendLine();
        sb.AppendLine("--- Test Mode (No Printer) ---");
        sb.AppendLine($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}");

        return sb.ToString();
    }
}

/// <summary>
/// Utility extension methods
/// </summary>
internal static class StringExtensions
{
    public static string PadCenter(this string text, int width)
    {
        if (text.Length >= width)
            return text.Substring(0, width);

        var paddingTotal = width - text.Length;
        var paddingLeft = paddingTotal / 2;
        var paddingRight = paddingTotal - paddingLeft;

        return new string(' ', paddingLeft) + text + new string(' ', paddingRight);
    }
}
