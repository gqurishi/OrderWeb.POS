using System.Globalization;
using System.Text;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Builds TSPL jobs for the Xprinter XP-421B LAN. It does not use Toshiba TPCL
/// or the receipt ESC/POS builder. Live tickets are raw TCP, not a Windows driver.
/// </summary>
public sealed class XprinterTsplModule
{
    public const int ResolutionDpi = 203;
    public const decimal MaximumPrintWidthMm = 108m;
    private const int DotsPerMm = 8;

    public byte[] BuildLabel(ToshibaTpclLabelContent content, LabelMediaProfile media, int copies)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(media);
        Validate(content, media, copies);

        var script = new StringBuilder();
        script.Append("SIZE ").Append(Mm(media.WidthMm)).Append(" mm, ").Append(Mm(media.HeightMm)).Append(" mm\r\n");
        script.Append(SensorCommand(media)).Append("\r\n");
        script.Append("DIRECTION 1\r\n");
        script.Append("REFERENCE ")
            .Append(Dots(media.HorizontalOffsetMm).ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(Dots(media.VerticalOffsetMm).ToString(CultureInfo.InvariantCulture))
            .Append("\r\n");
        script.Append("DENSITY ").Append(Density(media.Darkness).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        script.Append("SPEED ").Append(media.SpeedIps.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        script.Append("SET CUTTER ").Append(media.FinishingMode == LabelFinishingMode.Cutter ? "ON" : "OFF").Append("\r\n");
        script.Append("CLS\r\n");

        var x = 16;
        var y = 12;
        var widthDots = Dots(media.WidthMm) - 24;
        foreach (var line in Wrap(content.ItemName, Math.Max(8, widthDots / 16), 3))
        {
            Text(script, x, y, "3", 1, 1, line);
            y += 32;
        }

        Text(script, x, y, "2", 1, 1, $"QTY: {content.Quantity}");
        y += 26;

        foreach (var modifier in content.Modifiers.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var line in Wrap(modifier, Math.Max(10, widthDots / 12), 2))
            {
                Text(script, x, y, "2", 1, 1, line);
                y += 24;
            }
        }

        if (!string.IsNullOrWhiteSpace(content.OrderContext))
        {
            foreach (var line in Wrap(content.OrderContext, Math.Max(10, widthDots / 12), 2))
            {
                Text(script, x, y, "1", 1, 1, line);
                y += 20;
            }
        }

        if (content.PreparedAt is { } preparedAt)
        {
            Text(script, x, y, "1", 1, 1, preparedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            y += 20;
        }

        if (y > Dots(media.HeightMm) - 8)
            throw new InvalidOperationException("The label content does not fit in the selected media profile.");

        script.Append("PRINT ").Append(copies.ToString(CultureInfo.InvariantCulture)).Append(",1\r\n");
        return Encoding.ASCII.GetBytes(script.ToString());
    }

    private static string SensorCommand(LabelMediaProfile media) => media.SensorType switch
    {
        LabelSensorType.BlackMark => $"BLINE {Mm(media.GapMm)} mm, 0 mm",
        LabelSensorType.Continuous => "GAP 0 mm, 0 mm",
        _ => $"GAP {Mm(media.GapMm)} mm, 0 mm"
    };

    private static void Text(StringBuilder script, int x, int y, string font, int xMul, int yMul, string value)
    {
        script.Append("TEXT ")
            .Append(x.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(y.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append('"').Append(font).Append("\",0,")
            .Append(xMul.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(yMul.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append('"').Append(Escape(value)).Append("\"\r\n");
    }

    private static string Escape(string value) =>
        Sanitize(value).Replace("\\", " ").Replace("\"", "'");

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Printable label text cannot be empty.");
        var normalised = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var ascii = new StringBuilder(normalised.Length);
        foreach (var character in normalised)
            ascii.Append(character is >= ' ' and <= '~' ? character : '?');
        return ascii.ToString();
    }

    private static IEnumerable<string> Wrap(string value, int maximumCharacters, int maximumLines)
    {
        var words = Sanitize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            var remaining = word;
            while (remaining.Length > maximumCharacters)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    if (lines.Count == maximumLines) return lines;
                }
                lines.Add(remaining[..maximumCharacters]);
                remaining = remaining[maximumCharacters..];
                if (lines.Count == maximumLines) return lines;
            }

            if (current.Length > 0 && current.Length + 1 + remaining.Length > maximumCharacters)
            {
                lines.Add(current.ToString());
                current.Clear();
                if (lines.Count == maximumLines) return lines;
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(remaining);
        }

        if (current.Length > 0 && lines.Count < maximumLines) lines.Add(current.ToString());
        return lines;
    }

    private static void Validate(ToshibaTpclLabelContent content, LabelMediaProfile media, int copies)
    {
        _ = Sanitize(content.ItemName);
        foreach (var modifier in content.Modifiers ?? throw new ArgumentException("Modifiers cannot be null."))
            if (!string.IsNullOrWhiteSpace(modifier)) _ = Sanitize(modifier);
        if (!string.IsNullOrWhiteSpace(content.OrderContext)) _ = Sanitize(content.OrderContext);
        if (content.Quantity is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(content.Quantity));
        if (copies is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(copies));
        if (media.WidthMm is <= 0 or > MaximumPrintWidthMm) throw new ArgumentOutOfRangeException(nameof(media.WidthMm));
        if (media.HeightMm is < 10 or > 300) throw new ArgumentOutOfRangeException(nameof(media.HeightMm));
        if (media.SensorType == LabelSensorType.Continuous && media.GapMm != 0)
            throw new ArgumentException("Continuous TSPL media must use a 0 mm gap.");
        if (media.SensorType != LabelSensorType.Continuous && media.GapMm is <= 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(media.GapMm));
        if (media.SpeedIps is < 2 or > 6) throw new ArgumentOutOfRangeException(nameof(media.SpeedIps));
        if (media.Darkness is < -10 or > 10) throw new ArgumentOutOfRangeException(nameof(media.Darkness));
    }

    private static string Mm(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static int Dots(decimal millimetres) => decimal.ToInt32(decimal.Round(millimetres * DotsPerMm, 0, MidpointRounding.AwayFromZero));
    private static int Density(int darkness) => Math.Clamp((darkness + 10) * 15 / 20, 0, 15);
}
