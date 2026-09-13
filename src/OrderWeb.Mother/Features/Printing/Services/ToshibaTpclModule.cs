using System.Globalization;
using System.Text;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Generates framed TPCL commands for a Toshiba B-FV4D-GS14 (203 dpi).
/// This module is intentionally independent from receipt/ESC-POS command builders.
/// Command forms are based on Toshiba's B-FV4 external-interface specification.
/// </summary>
public sealed class ToshibaTpclModule
{
    private const byte Escape = 0x1B;
    private const byte LineFeed = 0x0A;
    private const byte Null = 0x00;
    private static readonly Encoding CommandEncoding = Encoding.ASCII;

    public const int ResolutionDpi = 203;
    public const decimal MaximumPrintWidthMm = 108m;

    public byte[] BuildLabel(ToshibaTpclLabelContent content, LabelMediaProfile media, int copies)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(media);
        Validate(content, media, copies);

        var commands = new List<byte>();
        var pitch = ToTenthsOfMillimetre(media.HeightMm + media.GapMm);
        var width = ToTenthsOfMillimetre(media.WidthMm);
        var height = ToTenthsOfMillimetre(media.HeightMm);

        AddCommand(commands, $"D{pitch:0000},{width:0000},{height:0000}");
        AddCommand(commands, "C");
        AddCommand(commands, $"AX;{Signed(media.HorizontalOffsetMm)},{Signed(media.VerticalOffsetMm)},+00");
        AddCommand(commands, $"AY;{media.Darkness:+00;-00;+00},1");

        var layoutWidth = Math.Max(20m, media.WidthMm - 6m);
        var y = 30;
        var field = 1;

        foreach (var line in Wrap(content.ItemName, CharactersPerLine(layoutWidth, 2), 3))
            AddTextField(commands, field++, 30, y, 2, line, ref y, 55);

        AddTextField(commands, field++, 30, y, 1, $"QTY: {content.Quantity}", ref y, 35);

        foreach (var modifier in content.Modifiers.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var line in Wrap(modifier, CharactersPerLine(layoutWidth, 1), 2))
                AddTextField(commands, field++, 30, y, 1, line, ref y, 32);
        }

        if (!string.IsNullOrWhiteSpace(content.OrderContext))
        {
            foreach (var line in Wrap(content.OrderContext, CharactersPerLine(layoutWidth, 1), 2))
                AddTextField(commands, field++, 30, y, 1, line, ref y, 32);
        }

        if (content.PreparedAt is { } preparedAt)
            AddTextField(commands, field, 30, y, 1, preparedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), ref y, 32);

        if (y > height - 15)
            throw new InvalidOperationException("The label content does not fit in the selected media profile.");

        // I = batch issue. The fixed status/feed fields follow Toshiba's documented XS form.
        AddCommand(commands, $"XS;I,{copies:0000},0002C{media.SpeedIps}000");
        return commands.ToArray();
    }

    /// <summary>Creates Toshiba's documented printer-status request.</summary>
    public byte[] BuildStatusQuery()
    {
        var bytes = new List<byte>();
        AddCommand(bytes, "WB");
        return bytes.ToArray();
    }

    private static void AddTextField(List<byte> output, int field, int x, int y, int scale, string value, ref int nextY, int lineHeight)
    {
        if (field > 999) throw new InvalidOperationException("The label contains too many text fields.");
        var id = field.ToString("000", CultureInfo.InvariantCulture);
        AddCommand(output, $"PC{id};{x:0000},{y:0000},{scale},{scale},C,00,B");
        AddCommand(output, $"RC{id};{SanitizeText(value)}");
        nextY += lineHeight;
    }

    private static void AddCommand(List<byte> output, string command)
    {
        output.Add(Escape);
        output.AddRange(CommandEncoding.GetBytes(command));
        output.Add(LineFeed);
        output.Add(Null);
    }

    private static IEnumerable<string> Wrap(string value, int maximumCharacters, int maximumLines)
    {
        var words = SanitizeText(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var originalWord in words)
        {
            var remaining = originalWord;
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

    private static string SanitizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Printable label text cannot be empty.");
        var normalised = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalised.Any(character => character is < ' ' or > '~'))
            throw new ArgumentException("Toshiba TPCL v1 accepts printable ASCII text only.");
        return normalised
            .Replace(';', ' ')
            .Replace('|', ' ')
            .Replace('{', '(')
            .Replace('}', ')');
    }

    private static void Validate(ToshibaTpclLabelContent content, LabelMediaProfile media, int copies)
    {
        _ = SanitizeText(content.ItemName);
        foreach (var modifier in content.Modifiers ?? throw new ArgumentException("Modifiers cannot be null."))
            if (!string.IsNullOrWhiteSpace(modifier)) _ = SanitizeText(modifier);
        if (!string.IsNullOrWhiteSpace(content.OrderContext)) _ = SanitizeText(content.OrderContext);
        if (content.Quantity is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(content.Quantity));
        if (copies is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(copies));
        if (media.WidthMm is <= 0 or > MaximumPrintWidthMm) throw new ArgumentOutOfRangeException(nameof(media.WidthMm));
        if (media.HeightMm is < 10 or > 300) throw new ArgumentOutOfRangeException(nameof(media.HeightMm));
        if (media.GapMm is <= 0 or > 20) throw new ArgumentOutOfRangeException(nameof(media.GapMm));
        if (media.SensorType != LabelSensorType.Gap)
            throw new NotSupportedException("TPCL v1 supports calibrated gap media only; black-mark and continuous media are deferred.");
        if (media.FinishingMode != LabelFinishingMode.TearOff)
            throw new NotSupportedException("TPCL v1 supports tear-off mode only; cutter mode is deferred.");
        if (media.SpeedIps is < 2 or > 6) throw new ArgumentOutOfRangeException(nameof(media.SpeedIps));
        if (media.Darkness is < -10 or > 10) throw new ArgumentOutOfRangeException(nameof(media.Darkness));
    }

    private static int CharactersPerLine(decimal widthMm, int scale) => Math.Max(6, (int)(widthMm / (scale == 2 ? 4m : 2.2m)));
    private static int ToTenthsOfMillimetre(decimal value) => decimal.ToInt32(decimal.Round(value * 10m, 0, MidpointRounding.AwayFromZero));
    private static string Signed(decimal value) => $"{ToTenthsOfMillimetre(value):+000;-000;+000}";
}
