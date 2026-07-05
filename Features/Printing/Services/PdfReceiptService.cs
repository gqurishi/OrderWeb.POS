using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using System.IO;
using PointF = Syncfusion.Drawing.PointF; // Resolve naming conflict with MAUI

namespace POS_in_NET.Services;

/// <summary>
/// Service for generating professional PDF receipts for orders
/// Uses Syncfusion PDF Library (Community License)
/// </summary>
public class PdfReceiptService
{
    /// <summary>
    /// Generate a professional PDF receipt for an order
    /// </summary>
    /// <param name="order">The order to generate receipt for</param>
    /// <param name="businessName">Your business name</param>
    /// <param name="businessAddress">Your business address</param>
    /// <returns>PDF as byte array</returns>
    public byte[] GenerateReceipt(
        dynamic order, 
        string businessName = "Your POS Business",
        string businessAddress = "123 Main Street, City, State 12345")
    {
        try
        {
            // Create a new PDF document
            using (PdfDocument document = new PdfDocument())
            {
                // Add a page
                PdfPage page = document.Pages.Add();
                PdfGraphics graphics = page.Graphics;
                
                // Define fonts
                PdfFont headerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 24, PdfFontStyle.Bold);
                PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
                PdfFont normalFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12);
                PdfFont boldFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
                
                float yPosition = 20;
                
                // Business Header
                graphics.DrawString(businessName, headerFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 30;
                
                graphics.DrawString(businessAddress, normalFont, PdfBrushes.Gray, new PointF(20, yPosition));
                yPosition += 25;
                
                // Draw separator line
                graphics.DrawLine(new PdfPen(PdfBrushes.LightGray, 1), new PointF(20, yPosition), new PointF(page.GetClientSize().Width - 20, yPosition));
                yPosition += 20;
                
                // Receipt Title
                graphics.DrawString("RECEIPT", titleFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 30;
                
                // Order Information
                graphics.DrawString($"Order #: {order.OrderId ?? "N/A"}", boldFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 20;
                
                graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 20;
                
                if (!string.IsNullOrEmpty(order.CustomerName))
                {
                    graphics.DrawString($"Customer: {order.CustomerName}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                    yPosition += 20;
                }
                
                if (!string.IsNullOrEmpty(order.TableName))
                {
                    graphics.DrawString($"Table: {order.TableName}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                    yPosition += 20;
                }
                
                yPosition += 10;
                
                // Draw separator
                graphics.DrawLine(new PdfPen(PdfBrushes.LightGray, 1), new PointF(20, yPosition), new PointF(page.GetClientSize().Width - 20, yPosition));
                yPosition += 20;
                
                // Items Header
                graphics.DrawString("ITEMS", boldFont, PdfBrushes.Black, new PointF(20, yPosition));
                graphics.DrawString("QTY", boldFont, PdfBrushes.Black, new PointF(300, yPosition));
                graphics.DrawString("PRICE", boldFont, PdfBrushes.Black, new PointF(400, yPosition));
                graphics.DrawString("TOTAL", boldFont, PdfBrushes.Black, new PointF(480, yPosition));
                yPosition += 25;
                
                // Items (if available)
                if (order.Items != null)
                {
                    foreach (var item in order.Items)
                    {
                        graphics.DrawString(!string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : item.Name ?? "Item", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                        graphics.DrawString(item.Quantity.ToString(), normalFont, PdfBrushes.Black, new PointF(300, yPosition));
                        graphics.DrawString($"£{item.Price:F2}", normalFont, PdfBrushes.Black, new PointF(400, yPosition));
                        graphics.DrawString($"£{item.Quantity * item.Price:F2}", normalFont, PdfBrushes.Black, new PointF(480, yPosition));
                        yPosition += 20;
                    }
                }
                
                yPosition += 10;
                
                // Draw separator
                graphics.DrawLine(new PdfPen(PdfBrushes.Black, 2), new PointF(20, yPosition), new PointF(page.GetClientSize().Width - 20, yPosition));
                yPosition += 20;
                
                // Totals
                decimal subtotal = order.Subtotal ?? order.Total ?? 0;
                decimal total = order.Total ?? 0;
                
                graphics.DrawString("Subtotal:", boldFont, PdfBrushes.Black, new PointF(350, yPosition));
                graphics.DrawString($"£{subtotal:F2}", normalFont, PdfBrushes.Black, new PointF(480, yPosition));
                yPosition += 20;
                
                graphics.DrawString("TOTAL:", titleFont, PdfBrushes.Black, new PointF(350, yPosition));
                graphics.DrawString($"£{total:F2}", titleFont, PdfBrushes.Black, new PointF(480, yPosition));
                yPosition += 30;

                graphics.DrawString("(VAT included in item prices)", normalFont, PdfBrushes.Gray, new PointF(350, yPosition));
                yPosition += 20;
                
                // Payment Method
                if (!string.IsNullOrEmpty(order.PaymentMethod))
                {
                    graphics.DrawString($"Payment: {order.PaymentMethod}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                    yPosition += 20;
                }
                
                yPosition += 20;
                
                // Footer
                graphics.DrawLine(new PdfPen(PdfBrushes.LightGray, 1), new PointF(20, yPosition), new PointF(page.GetClientSize().Width - 20, yPosition));
                yPosition += 15;
                
                graphics.DrawString("Thank you for your business!", normalFont, PdfBrushes.Gray, new PointF(20, yPosition));
                yPosition += 15;
                
                graphics.DrawString("Please come again!", normalFont, PdfBrushes.Gray, new PointF(20, yPosition));
                
                // Save to memory stream
                using (MemoryStream stream = new MemoryStream())
                {
                    document.Save(stream);
                    return stream.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" PDF Generation Error: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Save PDF receipt to file
    /// </summary>
    public async Task<string> SaveReceiptToFile(byte[] pdfData, string orderId)
    {
        try
        {
            // Save to Documents folder
            string folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "POS_Receipts"
            );
            
            // Create folder if it doesn't exist
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            
            // Generate filename
            string fileName = $"Receipt_{orderId}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(folderPath, fileName);
            
            // Save file
            await File.WriteAllBytesAsync(filePath, pdfData);
            
            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Save PDF Error: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Generate and save receipt in one step
    /// </summary>
    public async Task<string> GenerateAndSaveReceipt(
        dynamic order,
        string businessName = "Your POS Business",
        string businessAddress = "123 Main Street, City, State 12345")
    {
        byte[] pdfData = GenerateReceipt(order, businessName, businessAddress);
        string filePath = await SaveReceiptToFile(pdfData, order.OrderId?.ToString() ?? Guid.NewGuid().ToString());
        return filePath;
    }
    
    /// <summary>
    /// Generate end-of-day report PDF
    /// </summary>
    public byte[] GenerateEndOfDayReport(
        List<dynamic> orders,
        DateTime reportDate,
        string businessName = "Your POS Business")
    {
        try
        {
            using (PdfDocument document = new PdfDocument())
            {
                PdfPage page = document.Pages.Add();
                PdfGraphics graphics = page.Graphics;
                
                PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 20, PdfFontStyle.Bold);
                PdfFont normalFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12);
                PdfFont boldFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
                
                float yPosition = 20;
                
                // Title
                graphics.DrawString($"{businessName}", titleFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 30;
                
                graphics.DrawString("END OF DAY REPORT", boldFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 20;
                
                graphics.DrawString($"Date: {reportDate:yyyy-MM-dd}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 30;
                
                // Summary
                int totalOrders = orders.Count;
                decimal totalRevenue = orders.Sum(o => (decimal)(o.Total ?? 0));
                decimal avgOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;
                
                graphics.DrawString($"Total Orders: {totalOrders}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 20;
                
                graphics.DrawString($"Total Revenue: £{totalRevenue:F2}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 20;
                
                graphics.DrawString($"Average Order: £{avgOrderValue:F2}", normalFont, PdfBrushes.Black, new PointF(20, yPosition));
                yPosition += 30;
                
                // Save
                using (MemoryStream stream = new MemoryStream())
                {
                    document.Save(stream);
                    return stream.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Report Generation Error: {ex.Message}");
            throw;
        }
    }
}
