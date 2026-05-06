using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Brother QL-820NWB Label Printer Service
    /// Supports direct network printing via TCP/IP socket communication
    /// </summary>
    public class BrotherLabelPrinter
    {
        private readonly string _printerIpAddress;
        private readonly int _printerPort;
        private const int DefaultTimeout = 5000; // 5 seconds

        public BrotherLabelPrinter(string ipAddress, int port = 9100)
        {
            _printerIpAddress = ipAddress;
            _printerPort = port;
        }

        /// <summary>
        /// Print a simple text label
        /// </summary>
        public async Task<bool> PrintTextLabelAsync(string text, bool useRedInk = false, int copies = 1)
        {
            try
            {
                var labelData = GenerateTextLabel(text, useRedInk);
                
                for (int i = 0; i < copies; i++)
                {
                    await SendToPrinterAsync(labelData);
                    if (i < copies - 1)
                    {
                        await Task.Delay(100); // Small delay between copies
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Label printing failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Print a label with item name and custom text
        /// </summary>
        public async Task<bool> PrintItemLabelAsync(string itemName, string? customText = null, 
            bool useRedInk = false, string? additionalInfo = null)
        {
            try
            {
                var labelData = GenerateItemLabel(itemName, customText, useRedInk, additionalInfo);
                await SendToPrinterAsync(labelData);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Item label printing failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Print a component label for meal deals
        /// </summary>
        public async Task<bool> PrintComponentLabelAsync(string mealDealName, string componentName, 
            string componentType, int quantity = 1)
        {
            try
            {
                for (int i = 0; i < quantity; i++)
                {
                    var labelData = GenerateComponentLabel(mealDealName, componentName, componentType, i + 1, quantity);
                    await SendToPrinterAsync(labelData);
                    
                    if (i < quantity - 1)
                    {
                        await Task.Delay(100); // Delay between multiple labels
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Component label printing failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test printer connectivity
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_printerIpAddress, _printerPort);
                return client.Connected;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Printer connection test failed: {ex.Message}");
                return false;
            }
        }

        private byte[] GenerateTextLabel(string text, bool useRedInk)
        {
            var label = new List<byte>();
            
            // Initialize printer
            label.AddRange(ESCCommands.Initialize());
            
            // Set label size (62mm width - QL-820NWB default)
            label.AddRange(ESCCommands.SetLabelSize(62));
            
            // Set print quality (high quality)
            label.AddRange(ESCCommands.SetPrintQuality(true));
            
            // Set color (red or black)
            if (useRedInk)
            {
                label.AddRange(ESCCommands.SelectRedInk());
            }
            
            // Add text content
            label.AddRange(ESCCommands.PrintText(text, ESCCommands.FontSize.Large, ESCCommands.TextAlign.Center));
            
            // Print and feed
            label.AddRange(ESCCommands.PrintAndFeed());
            
            return label.ToArray();
        }

        private byte[] GenerateItemLabel(string itemName, string? customText, bool useRedInk, string? additionalInfo)
        {
            var label = new List<byte>();
            
            // Initialize
            label.AddRange(ESCCommands.Initialize());
            label.AddRange(ESCCommands.SetLabelSize(62));
            label.AddRange(ESCCommands.SetPrintQuality(true));
            
            // Print item name (large, bold)
            if (useRedInk)
            {
                label.AddRange(ESCCommands.SelectRedInk());
            }
            label.AddRange(ESCCommands.PrintText(itemName, ESCCommands.FontSize.Large, ESCCommands.TextAlign.Center, true));
            
            // Switch to black for details
            if (useRedInk)
            {
                label.AddRange(ESCCommands.SelectBlackInk());
            }
            
            // Print custom text if provided
            if (!string.IsNullOrEmpty(customText))
            {
                label.AddRange(ESCCommands.PrintText($"\"{customText}\"", ESCCommands.FontSize.Medium, ESCCommands.TextAlign.Center));
            }
            
            // Print additional info (table number, time, etc.)
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                label.AddRange(ESCCommands.PrintText(additionalInfo, ESCCommands.FontSize.Small, ESCCommands.TextAlign.Center));
            }
            
            // Print timestamp
            label.AddRange(ESCCommands.PrintText(DateTime.Now.ToString("HH:mm"), ESCCommands.FontSize.Small, ESCCommands.TextAlign.Right));
            
            label.AddRange(ESCCommands.PrintAndFeed());
            
            return label.ToArray();
        }

        private byte[] GenerateComponentLabel(string mealDealName, string componentName, string componentType, int currentQty, int totalQty)
        {
            var label = new List<byte>();
            
            // Initialize
            label.AddRange(ESCCommands.Initialize());
            label.AddRange(ESCCommands.SetLabelSize(62));
            label.AddRange(ESCCommands.SetPrintQuality(true));
            
            // Meal deal header (small)
            label.AddRange(ESCCommands.PrintText(mealDealName, ESCCommands.FontSize.Small, ESCCommands.TextAlign.Center));
            
            // Component name (large, bold)
            label.AddRange(ESCCommands.PrintText($"→ {componentName}", ESCCommands.FontSize.Large, ESCCommands.TextAlign.Left, true));
            
            // Component type and VAT info
            var typeInfo = componentType switch
            {
                "HotFood" => "(HOT - 20% VAT)",
                "ColdFood" => "(COLD - 0% VAT)",
                "HotBeverage" => "(HOT DRINK - 20% VAT)",
                "ColdBeverage" => "(COLD DRINK - 0% VAT)",
                "Alcohol" => "(ALCOHOL - 20% VAT)",
                _ => ""
            };
            label.AddRange(ESCCommands.PrintText(typeInfo, ESCCommands.FontSize.Small, ESCCommands.TextAlign.Left));
            
            // Quantity indicator
            if (totalQty > 1)
            {
                label.AddRange(ESCCommands.PrintText($"#{currentQty} of {totalQty}", ESCCommands.FontSize.Small, ESCCommands.TextAlign.Right));
            }
            
            // Timestamp
            label.AddRange(ESCCommands.PrintText(DateTime.Now.ToString("HH:mm"), ESCCommands.FontSize.Small, ESCCommands.TextAlign.Right));
            
            label.AddRange(ESCCommands.PrintAndFeed());
            
            return label.ToArray();
        }

        private async Task SendToPrinterAsync(byte[] data)
        {
            using var client = new TcpClient();
            client.SendTimeout = DefaultTimeout;
            client.ReceiveTimeout = DefaultTimeout;
            
            await client.ConnectAsync(_printerIpAddress, _printerPort);
            
            if (!client.Connected)
            {
                throw new Exception($"Failed to connect to printer at {_printerIpAddress}:{_printerPort}");
            }
            
            var stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
            
            System.Diagnostics.Debug.WriteLine($"[SUCCESS] Label sent to printer: {data.Length} bytes");
        }
    }

    /// <summary>
    /// ESC/P commands for Brother label printers
    /// Based on Brother QL-820NWB command reference
    /// </summary>
    internal static class ESCCommands
    {
        private const byte ESC = 0x1B;
        private const byte GS = 0x1D;
        private const byte LF = 0x0A;
        private const byte CR = 0x0D;

        public enum FontSize
        {
            Small = 0,
            Medium = 1,
            Large = 2
        }

        public enum TextAlign
        {
            Left = 0,
            Center = 1,
            Right = 2
        }

        public static byte[] Initialize()
        {
            return new byte[] { ESC, (byte)'@' }; // ESC @ - Initialize printer
        }

        public static byte[] SetLabelSize(int widthMm)
        {
            // Simplified - actual implementation would set precise label dimensions
            return new byte[] { };
        }

        public static byte[] SetPrintQuality(bool highQuality)
        {
            // Set print density/quality
            return new byte[] { };
        }

        public static byte[] SelectRedInk()
        {
            // Brother QL-820NWB red ink selection
            return new byte[] { ESC, (byte)'r', 1 };
        }

        public static byte[] SelectBlackInk()
        {
            // Back to black ink
            return new byte[] { ESC, (byte)'r', 0 };
        }

        public static byte[] PrintText(string text, FontSize size = FontSize.Medium, TextAlign align = TextAlign.Left, bool bold = false)
        {
            var commands = new List<byte>();
            
            // Set alignment
            commands.Add(ESC);
            commands.Add((byte)'a');
            commands.Add((byte)align);
            
            // Set font size
            commands.Add(GS);
            commands.Add((byte)'!');
            commands.Add(size switch
            {
                FontSize.Small => (byte)0x00,
                FontSize.Medium => (byte)0x11,
                FontSize.Large => (byte)0x22,
                _ => (byte)0x11
            });
            
            // Set bold
            if (bold)
            {
                commands.Add(ESC);
                commands.Add((byte)'E');
                commands.Add(1);
            }
            
            // Add text
            commands.AddRange(Encoding.UTF8.GetBytes(text));
            commands.Add(LF);
            
            // Reset bold
            if (bold)
            {
                commands.Add(ESC);
                commands.Add((byte)'E');
                commands.Add(0);
            }
            
            return commands.ToArray();
        }

        public static byte[] PrintAndFeed()
        {
            // Print and cut
            return new byte[] { GS, (byte)'V', 66, 0 }; // Cut after printing
        }
    }
}
