using POS_in_NET.Models;
using System.Text;

namespace POS_in_NET.Services;

/// <summary>
/// Text alignment for printing
/// </summary>
public enum TextAlign
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>
/// Builder for ESC/POS commands compatible with Epson, Star, and other thermal printers
/// Uses universal ESC/POS commands that work across brands
/// </summary>
public class EscPosBuilder
{
    private readonly List<byte> _buffer = new();
    private readonly PrinterBrand _brand;
    private readonly PaperWidth _paperWidth;
    private readonly int _lineWidth;

    // ESC/POS Command Constants
    private static class Commands
    {
        // Initialize
        public static readonly byte[] INIT = { 0x1B, 0x40 }; // ESC @
        
        // Text Formatting
        public static readonly byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };  // ESC E 1
        public static readonly byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 }; // ESC E 0
        public static readonly byte[] UNDERLINE_ON = { 0x1B, 0x2D, 0x01 };  // ESC - 1
        public static readonly byte[] UNDERLINE_OFF = { 0x1B, 0x2D, 0x00 }; // ESC - 0
        public static readonly byte[] DOUBLE_HEIGHT_ON = { 0x1B, 0x21, 0x10 };  // ESC ! 16
        public static readonly byte[] DOUBLE_WIDTH_ON = { 0x1B, 0x21, 0x20 };   // ESC ! 32
        public static readonly byte[] DOUBLE_SIZE_ON = { 0x1B, 0x21, 0x30 };    // ESC ! 48
        public static readonly byte[] NORMAL_SIZE = { 0x1B, 0x21, 0x00 };       // ESC ! 0
        
        // Alignment
        public static readonly byte[] ALIGN_LEFT = { 0x1B, 0x61, 0x00 };   // ESC a 0
        public static readonly byte[] ALIGN_CENTER = { 0x1B, 0x61, 0x01 }; // ESC a 1
        public static readonly byte[] ALIGN_RIGHT = { 0x1B, 0x61, 0x02 };  // ESC a 2
        
        // Paper
        public static readonly byte[] LINE_FEED = { 0x0A }; // LF
        public static readonly byte[] CARRIAGE_RETURN = { 0x0D }; // CR
        
        // Cut (Epson style - most compatible)
        public static readonly byte[] CUT_FULL = { 0x1D, 0x56, 0x00 };    // GS V 0 (full cut)
        public static readonly byte[] CUT_PARTIAL = { 0x1D, 0x56, 0x01 }; // GS V 1 (partial cut)
        public static readonly byte[] CUT_FEED_FULL = { 0x1D, 0x56, 0x41, 0x03 };    // GS V A 3 (feed & full cut)
        public static readonly byte[] CUT_FEED_PARTIAL = { 0x1D, 0x56, 0x42, 0x03 }; // GS V B 3 (feed & partial cut)
        
        // Star printer cut commands (alternative)
        public static readonly byte[] STAR_CUT_FULL = { 0x1B, 0x64, 0x02 };    // ESC d 2
        public static readonly byte[] STAR_CUT_PARTIAL = { 0x1B, 0x64, 0x03 }; // ESC d 3
        
        // Cash Drawer
        public static readonly byte[] CASH_DRAWER_PIN2 = { 0x1B, 0x70, 0x00, 0x19, 0xFA }; // ESC p 0 25 250
        public static readonly byte[] CASH_DRAWER_PIN5 = { 0x1B, 0x70, 0x01, 0x19, 0xFA }; // ESC p 1 25 250
        
        // Buzzer (beeper)
        public static readonly byte[] BUZZER = { 0x1B, 0x42, 0x05, 0x09 }; // ESC B 5 9 (5 beeps, 9*50ms)
        public static readonly byte[] BUZZER_SHORT = { 0x1B, 0x42, 0x02, 0x05 }; // 2 beeps, shorter
    }

    public EscPosBuilder(PrinterBrand brand = PrinterBrand.Epson, PaperWidth paperWidth = PaperWidth.Mm80)
    {
        _brand = brand;
        _paperWidth = paperWidth;
        _lineWidth = paperWidth == PaperWidth.Mm80 ? 48 : 32; // Characters per line
    }

    /// <summary>
    /// Initialize printer to default state
    /// </summary>
    public EscPosBuilder Initialize()
    {
        _buffer.AddRange(Commands.INIT);
        return this;
    }

    /// <summary>
    /// Set text bold on/off
    /// </summary>
    public EscPosBuilder SetBold(bool on)
    {
        _buffer.AddRange(on ? Commands.BOLD_ON : Commands.BOLD_OFF);
        return this;
    }

    /// <summary>
    /// Set text underline on/off
    /// </summary>
    public EscPosBuilder SetUnderline(bool on)
    {
        _buffer.AddRange(on ? Commands.UNDERLINE_ON : Commands.UNDERLINE_OFF);
        return this;
    }

    /// <summary>
    /// Set font size (1-8 for width and height)
    /// </summary>
    public EscPosBuilder SetFontSize(int width, int height)
    {
        width = Math.Clamp(width, 1, 8) - 1;
        height = Math.Clamp(height, 1, 8) - 1;
        
        // GS ! n - where n = (width << 4) | height
        byte size = (byte)((width << 4) | height);
        _buffer.AddRange(new byte[] { 0x1D, 0x21, size });
        return this;
    }

    /// <summary>
    /// Reset to normal font size
    /// </summary>
    public EscPosBuilder SetNormalSize()
    {
        _buffer.AddRange(Commands.NORMAL_SIZE);
        return this;
    }

    /// <summary>
    /// Set text alignment
    /// </summary>
    public EscPosBuilder SetAlign(TextAlign align)
    {
        var cmd = align switch
        {
            TextAlign.Left => Commands.ALIGN_LEFT,
            TextAlign.Center => Commands.ALIGN_CENTER,
            TextAlign.Right => Commands.ALIGN_RIGHT,
            _ => Commands.ALIGN_LEFT
        };
        _buffer.AddRange(cmd);
        return this;
    }

    /// <summary>
    /// Print text without line feed
    /// </summary>
    public EscPosBuilder PrintText(string text)
    {
        var bytes = Encoding.GetEncoding("IBM437").GetBytes(text);
        _buffer.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// Print text with line feed
    /// </summary>
    public EscPosBuilder PrintLine(string text = "")
    {
        if (!string.IsNullOrEmpty(text))
        {
            PrintText(text);
        }
        _buffer.AddRange(Commands.LINE_FEED);
        return this;
    }

    /// <summary>
    /// Print a line with left and right text (e.g., "Item    $10.00")
    /// </summary>
    public EscPosBuilder PrintColumns(string left, string right)
    {
        var spaces = _lineWidth - left.Length - right.Length;
        if (spaces < 1) spaces = 1;
        
        var line = left + new string(' ', spaces) + right;
        if (line.Length > _lineWidth)
        {
            line = line.Substring(0, _lineWidth);
        }
        
        return PrintLine(line);
    }

    /// <summary>
    /// Print 3 columns (left, center, right)
    /// </summary>
    public EscPosBuilder PrintColumns(string left, string center, string right)
    {
        var totalContent = left.Length + center.Length + right.Length;
        var totalSpaces = _lineWidth - totalContent;
        var leftSpaces = totalSpaces / 2;
        var rightSpaces = totalSpaces - leftSpaces;
        
        if (leftSpaces < 1) leftSpaces = 1;
        if (rightSpaces < 1) rightSpaces = 1;
        
        var line = left + new string(' ', leftSpaces) + center + new string(' ', rightSpaces) + right;
        if (line.Length > _lineWidth)
        {
            line = line.Substring(0, _lineWidth);
        }
        
        return PrintLine(line);
    }

    /// <summary>
    /// Print a divider line
    /// </summary>
    public EscPosBuilder PrintDivider(char character = '-')
    {
        return PrintLine(new string(character, _lineWidth));
    }

    /// <summary>
    /// Print a double divider line
    /// </summary>
    public EscPosBuilder PrintDoubleDivider()
    {
        return PrintDivider('=');
    }

    /// <summary>
    /// Feed specified number of lines
    /// </summary>
    public EscPosBuilder FeedLines(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _buffer.AddRange(Commands.LINE_FEED);
        }
        return this;
    }

    /// <summary>
    /// Feed paper by dots (1 dot ≈ 0.125mm)
    /// </summary>
    public EscPosBuilder FeedDots(int dots)
    {
        // ESC J n - feed n dots
        _buffer.AddRange(new byte[] { 0x1B, 0x4A, (byte)Math.Min(dots, 255) });
        return this;
    }

    /// <summary>
    /// Cut paper (full or partial)
    /// </summary>
    public EscPosBuilder Cut(bool partial = false)
    {
        if (_brand == PrinterBrand.Star)
        {
            _buffer.AddRange(partial ? Commands.STAR_CUT_PARTIAL : Commands.STAR_CUT_FULL);
        }
        else
        {
            // Epson and most others
            _buffer.AddRange(partial ? Commands.CUT_FEED_PARTIAL : Commands.CUT_FEED_FULL);
        }
        return this;
    }

    /// <summary>
    /// Open cash drawer
    /// </summary>
    public EscPosBuilder OpenCashDrawer(int pin = 0)
    {
        _buffer.AddRange(pin == 0 ? Commands.CASH_DRAWER_PIN2 : Commands.CASH_DRAWER_PIN5);
        return this;
    }

    /// <summary>
    /// Sound the buzzer/beeper (for kitchen printers)
    /// </summary>
    public EscPosBuilder Buzzer(bool shortBeep = false)
    {
        _buffer.AddRange(shortBeep ? Commands.BUZZER_SHORT : Commands.BUZZER);
        return this;
    }

    /// <summary>
    /// Print a QR code
    /// </summary>
    public EscPosBuilder PrintQRCode(string data, int size = 6)
    {
        size = Math.Clamp(size, 1, 16);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var len = dataBytes.Length + 3;
        
        // Function 165: QR Code model (Model 2)
        _buffer.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });
        
        // Function 167: QR Code size
        _buffer.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, (byte)size });
        
        // Function 169: QR Code error correction (L=48, M=49, Q=50, H=51)
        _buffer.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 }); // M level
        
        // Function 180: Store QR Code data
        _buffer.AddRange(new byte[] { 0x1D, 0x28, 0x6B, (byte)(len & 0xFF), (byte)(len >> 8), 0x31, 0x50, 0x30 });
        _buffer.AddRange(dataBytes);
        
        // Function 181: Print QR Code
        _buffer.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });
        
        return this;
    }

    /// <summary>
    /// Print a barcode (Code 128)
    /// </summary>
    public EscPosBuilder PrintBarcode(string data, int height = 50)
    {
        var dataBytes = Encoding.ASCII.GetBytes(data);
        
        // Set barcode height
        _buffer.AddRange(new byte[] { 0x1D, 0x68, (byte)Math.Min(height, 255) });
        
        // Set barcode width (2 = default)
        _buffer.AddRange(new byte[] { 0x1D, 0x77, 0x02 });
        
        // Print barcode (Code 128)
        _buffer.AddRange(new byte[] { 0x1D, 0x6B, 0x49, (byte)dataBytes.Length });
        _buffer.AddRange(dataBytes);
        
        return FeedLines(1);
    }

    /// <summary>
    /// Build and return the ESC/POS command buffer
    /// </summary>
    public byte[] Build()
    {
        return _buffer.ToArray();
    }

    /// <summary>
    /// Clear the buffer
    /// </summary>
    public EscPosBuilder Clear()
    {
        _buffer.Clear();
        return this;
    }

    /// <summary>
    /// Get buffer size in bytes
    /// </summary>
    public int Length => _buffer.Count;

    /// <summary>
    /// Get characters per line for current paper width
    /// </summary>
    public int LineWidth => _lineWidth;
}
