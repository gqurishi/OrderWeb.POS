using System.Globalization;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Builds Brother Raster jobs for the TD-4420DN. It does not use TPCL, TSPL,
/// or a Windows driver. Each job switches the printer into Raster mode first.
/// </summary>
public sealed class BrotherRasterModule
{
    public const int ResolutionDpi = 203;
    public const decimal MaximumPrintWidthMm = 104m;
    private const int LineBytes = 104;
    private const int HeadDots = 832;
    private const int SideInsetDots = 12;
    private const int DieCutEndMarginDots = 24;

    public byte[] BuildLabel(ToshibaTpclLabelContent content, LabelMediaProfile media, int copies)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(media);
        Validate(content, media, copies);

        var layout = Layout(media);
        var page = DrawPage(content, media, layout);
        var job = new List<byte>(page.Length * copies + 64);
        job.AddRange([0x1B, 0x40]);
        job.AddRange([0x1B, 0x69, 0x61, 0x01]);
        job.AddRange([0x1B, 0x69, 0x21, 0x01]);

        for (var copy = 0; copy < copies; copy++)
        {
            AddPrintInformation(job, media, layout.RasterLines, copy == 0);
            job.AddRange([0x1B, 0x69, 0x4D, (byte)(media.FinishingMode == LabelFinishingMode.Cutter ? 0x40 : 0x00)]);
            if (media.SensorType == LabelSensorType.Continuous)
                job.AddRange([0x1B, 0x69, 0x64, DieCutEndMarginDots, 0x00]);
            else
                job.AddRange([0x1B, 0x69, 0x64, 0x00, 0x00]);
            job.AddRange([0x4D, 0x00]);
            job.AddRange(page);
            job.Add(copy == copies - 1 ? (byte)0x1A : (byte)0x0C);
        }

        job.AddRange([0x1B, 0x69, 0x61, 0xFF]);
        return job.ToArray();
    }

    private readonly record struct RasterLayout(int LeftPin, int PrintWidthDots, int RasterLines);

    /// <summary>
    /// TD-4420DN raster lines are always 104 bytes / 832 pins. The label sits in the
    /// middle of that head, with 1.5 mm unprintable on each side. Die-cut print
    /// length excludes 3 mm at each end; the margin command stays 0.
    /// </summary>
    private static RasterLayout Layout(LabelMediaProfile media)
    {
        var mediaDots = Dots(media.WidthMm);
        var unused = HeadDots - mediaDots;
        var left = unused / 2 + SideInsetDots;
        var printWidth = mediaDots - SideInsetDots * 2;
        if (left < 0 || printWidth < 40 || left + printWidth > HeadDots)
            throw new ArgumentOutOfRangeException(nameof(media.WidthMm), "Label width does not fit the TD-4420DN print head.");

        var lengthDots = Dots(media.HeightMm);
        var rasterLines = media.SensorType == LabelSensorType.Continuous
            ? lengthDots
            : Math.Max(1, lengthDots - DieCutEndMarginDots * 2);
        return new RasterLayout(left, printWidth, rasterLines);
    }

    private static byte[] DrawPage(ToshibaTpclLabelContent content, LabelMediaProfile media, RasterLayout layout)
    {
        var lines = new byte[layout.RasterLines][];
        for (var row = 0; row < layout.RasterLines; row++)
            lines[row] = new byte[LineBytes];

        var x = Math.Clamp(layout.LeftPin + Dots(media.HorizontalOffsetMm), layout.LeftPin, layout.LeftPin + layout.PrintWidthDots - 8);
        var y = Math.Clamp(4 + Dots(media.VerticalOffsetMm), 0, layout.RasterLines - 8);
        var maxWidthDots = Math.Max(40, layout.PrintWidthDots - 8);

        foreach (var line in Wrap(content.ItemName, Math.Max(8, maxWidthDots / 12), 3))
            y = DrawText(lines, x, y, line, scale: 2) + 4;
        y = DrawText(lines, x, y, $"QTY: {content.Quantity}", scale: 1) + 3;

        foreach (var modifier in content.Modifiers.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var line in Wrap(modifier, Math.Max(10, maxWidthDots / 7), 2))
                y = DrawText(lines, x, y, line, scale: 1) + 2;
        }

        if (!string.IsNullOrWhiteSpace(content.OrderContext))
        {
            foreach (var line in Wrap(content.OrderContext, Math.Max(10, maxWidthDots / 7), 2))
                y = DrawText(lines, x, y, line, scale: 1) + 2;
        }

        if (content.PreparedAt is { } preparedAt)
            y = DrawText(lines, x, y, preparedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), scale: 1);

        if (y > layout.RasterLines - 4)
            throw new InvalidOperationException("The label content does not fit in the selected media profile.");

        var payload = new List<byte>(layout.RasterLines * (LineBytes + 3));
        foreach (var line in lines)
        {
            if (line.All(value => value == 0))
            {
                payload.Add(0x5A);
                continue;
            }

            payload.Add(0x67);
            payload.Add(0x00);
            payload.Add(LineBytes);
            payload.AddRange(line);
        }

        return payload.ToArray();
    }

    private static int DrawText(byte[][] lines, int x, int y, string text, int scale)
    {
        var cursor = x;
        foreach (var character in Sanitize(text))
        {
            var glyph = Glyph(character);
            for (var column = 0; column < 5; column++)
            {
                for (var row = 0; row < 7; row++)
                {
                    if ((glyph[column] & (1 << row)) == 0)
                        continue;
                    for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        Set(lines, cursor + column * scale + sx, y + row * scale + sy);
                }
            }

            cursor += (5 + 1) * scale;
        }

        return y + 7 * scale;
    }

    private static void Set(byte[][] lines, int x, int y)
    {
        if (y < 0 || y >= lines.Length || x < 0 || x >= HeadDots)
            return;
        lines[y][x / 8] |= (byte)(0x80 >> (x % 8));
    }

    private static void AddPrintInformation(List<byte> job, LabelMediaProfile media, int rasterLines, bool firstPage)
    {
        var width = (byte)Math.Clamp((int)decimal.Round(media.WidthMm), 1, 255);
        var length = (byte)Math.Clamp((int)decimal.Round(media.HeightMm), 1, 255);
        job.AddRange(
        [
            0x1B, 0x69, 0x7A,
            0x8E,
            media.SensorType == LabelSensorType.Continuous ? (byte)0x0A : (byte)0x0B,
            width,
            length,
            (byte)(rasterLines & 0xFF),
            (byte)((rasterLines >> 8) & 0xFF),
            (byte)((rasterLines >> 16) & 0xFF),
            (byte)((rasterLines >> 24) & 0xFF),
            firstPage ? (byte)0x00 : (byte)0x01,
            0x00
        ]);
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
        if (media.HeightMm is < 10 or > 255) throw new ArgumentOutOfRangeException(nameof(media.HeightMm));
        if (Dots(media.WidthMm) > HeadDots) throw new ArgumentOutOfRangeException(nameof(media.WidthMm));
        if (media.SensorType == LabelSensorType.Continuous && media.GapMm != 0)
            throw new ArgumentException("Continuous Brother media must use a 0 mm gap.");
        if (media.SensorType != LabelSensorType.Continuous && media.GapMm is <= 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(media.GapMm));
        if (media.SpeedIps is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(media.SpeedIps));
        if (media.Darkness is < -10 or > 10) throw new ArgumentOutOfRangeException(nameof(media.Darkness));
    }

    private static IEnumerable<string> Wrap(string value, int maximumCharacters, int maximumLines)
    {
        var words = Sanitize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var remaining = word;
            while (remaining.Length > maximumCharacters)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                    if (lines.Count == maximumLines) return lines;
                }
                lines.Add(remaining[..maximumCharacters]);
                remaining = remaining[maximumCharacters..];
                if (lines.Count == maximumLines) return lines;
            }

            if (current.Length > 0 && current.Length + 1 + remaining.Length > maximumCharacters)
            {
                lines.Add(current);
                current = "";
                if (lines.Count == maximumLines) return lines;
            }

            current = current.Length == 0 ? remaining : current + " " + remaining;
        }

        if (current.Length > 0 && lines.Count < maximumLines)
            lines.Add(current);
        return lines;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Printable label text cannot be empty.");
        var chars = value.Trim().ToUpperInvariant().Select(character => character is >= ' ' and <= '~' ? character : '?');
        var text = new string(chars.ToArray());
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Dots(decimal millimetres) =>
        decimal.ToInt32(decimal.Round(millimetres * ResolutionDpi / 25.4m, 0, MidpointRounding.AwayFromZero));

    private static byte[] Glyph(char character) => Font.TryGetValue(character, out var glyph) ? glyph : Font['?'];

    private static readonly Dictionary<char, byte[]> Font = BuildFont();

    private static Dictionary<char, byte[]> BuildFont()
    {
        var rows = new Dictionary<char, string>
        {
            [' '] = ".....",
            ['?'] = ".###. #...# ....# ..#.. ..#.. ..... ..#..",
            ['0'] = ".###. #...# #..## #.#.# ##..# #...# .###.",
            ['1'] = "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
            ['2'] = ".###. #...# ....# ..##. .#... #.... #####",
            ['3'] = ".###. #...# ....# ..##. ....# #...# .###.",
            ['4'] = "...#. ..##. .#.#. #..#. ##### ...#. ...#.",
            ['5'] = "##### #.... ####. ....# ....# #...# .###.",
            ['6'] = "..##. .#... #.... ####. #...# #...# .###.",
            ['7'] = "##### ....# ...#. ..#.. .#... .#... .#...",
            ['8'] = ".###. #...# #...# .###. #...# #...# .###.",
            ['9'] = ".###. #...# #...# .#### ....# ...#. .##..",
            ['A'] = ".###. #...# #...# ##### #...# #...# #...#",
            ['B'] = "####. #...# #...# ####. #...# #...# ####.",
            ['C'] = ".###. #...# #.... #.... #.... #...# .###.",
            ['D'] = "####. #...# #...# #...# #...# #...# ####.",
            ['E'] = "##### #.... #.... ####. #.... #.... #####",
            ['F'] = "##### #.... #.... ####. #.... #.... #....",
            ['G'] = ".###. #...# #.... #.### #...# #...# .###.",
            ['H'] = "#...# #...# #...# ##### #...# #...# #...#",
            ['I'] = ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",
            ['J'] = "..### ...#. ...#. ...#. #..#. #..#. .##..",
            ['K'] = "#...# #..#. #.#.. ##... #.#.. #..#. #...#",
            ['L'] = "#.... #.... #.... #.... #.... #.... #####",
            ['M'] = "#...# ##.## #.#.# #.#.# #...# #...# #...#",
            ['N'] = "#...# ##..# #.#.# #.#.# #.#.# #..## #...#",
            ['O'] = ".###. #...# #...# #...# #...# #...# .###.",
            ['P'] = "####. #...# #...# ####. #.... #.... #....",
            ['Q'] = ".###. #...# #...# #...# #.#.# #..#. .##.#",
            ['R'] = "####. #...# #...# ####. #.#.. #..#. #...#",
            ['S'] = ".#### #.... #.... .###. ....# ....# ####.",
            ['T'] = "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
            ['U'] = "#...# #...# #...# #...# #...# #...# .###.",
            ['V'] = "#...# #...# #...# #...# #...# .#.#. ..#..",
            ['W'] = "#...# #...# #...# #.#.# #.#.# ##.## #...#",
            ['X'] = "#...# #...# .#.#. ..#.. .#.#. #...# #...#",
            ['Y'] = "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..",
            ['Z'] = "##### ....# ...#. ..#.. .#... #.... #####",
            ['#'] = ".#.#. ##### .#.#. .#.#. ##### .#.#. .#.#.",
            [':'] = "..... ..#.. ..#.. ..... ..#.. ..#.. .....",
            ['-'] = "..... ..... ..... ##### ..... ..... .....",
            ['.'] = "..... ..... ..... ..... ..... ..#.. ..#..",
            [','] = "..... ..... ..... ..... ..#.. ..#.. .#...",
            ['/'] = "....# ...#. ..#.. .#... #.... ..... .....",
            ['+'] = "..#.. ..#.. ##### ..#.. ..#.. ..... .....",
            ['*'] = "..... #.#.# .###. ##### .###. #.#.# ....."
        };

        var font = new Dictionary<char, byte[]>();
        foreach (var (character, pattern) in rows)
        {
            var glyphs = pattern.Split(' ');
            var columns = new byte[5];
            for (var row = 0; row < 7; row++)
            {
                var bits = glyphs[row];
                for (var column = 0; column < 5; column++)
                {
                    if (bits[column] == '#')
                        columns[column] |= (byte)(1 << row);
                }
            }
            font[character] = columns;
        }

        return font;
    }
}
