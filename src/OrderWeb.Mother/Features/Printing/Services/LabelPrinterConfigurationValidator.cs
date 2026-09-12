using System.Globalization;
using System.Net;
using System.Net.Sockets;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public static class LabelPrinterConfigurationValidator
{
    public static string? Validate(NetworkPrinter printer, bool cutterInstalled)
    {
        if (string.IsNullOrWhiteSpace(printer.Name))
            return "Enter a printer name.";

        if (!IsPrivateIpv4Address(printer.IpAddress))
            return "Enter a valid private IPv4 address (10.x.x.x, 172.16-31.x.x, or 192.168.x.x).";

        if (printer.Port is < 1 or > 65535)
            return "Enter a TCP port from 1 to 65535 (normally 9100).";

        if (string.IsNullOrWhiteSpace(printer.LabelProfile))
            return "Select a label profile.";

        if (printer.MediaWidthMm is null or <= 0 || printer.LabelWidthMm is null or <= 0 ||
            printer.LabelHeightMm is null or <= 0)
            return "Media width, label width, and label height must be greater than zero.";

        if (printer.LabelWidthMm > LabelPrinterProfiles.ToshibaBfv4dMaximumPrintWidthMm)
            return $"Label width cannot exceed {LabelPrinterProfiles.ToshibaBfv4dMaximumPrintWidthMm.ToString(CultureInfo.InvariantCulture)} mm for this printer.";

        if (printer.LabelWidthMm > printer.MediaWidthMm)
            return "Label width cannot be greater than media width.";

        if (printer.LabelWidthMm > 108m || printer.LabelHeightMm > 1000m || printer.MediaWidthMm > 112m)
            return "The entered label dimensions are outside the supported Toshiba B-FV4D range.";

        if (printer.SensorType is null)
            return "Select a media sensor type.";

        if (printer.SensorType == LabelSensorType.Continuous && printer.GapSizeMm.GetValueOrDefault() != 0)
            return "Gap size must be 0 mm for continuous media.";

        if (printer.SensorType != LabelSensorType.Continuous && printer.GapSizeMm is null or <= 0)
            return "Enter a gap/mark size greater than zero for the selected sensor.";

        if (printer.PrintSpeed is null or < 2 or > 6)
            return "Print speed must be between 2 and 6 inches per second.";

        if (printer.PrintDarkness is null or < -10 or > 10)
            return "Print darkness must be between -10 and 10.";

        if (printer.HorizontalOffsetMm is < -10m or > 10m || printer.VerticalOffsetMm is < -10m or > 10m)
            return "Horizontal and vertical offsets must be between -10 and 10 mm.";

        if (printer.NumberOfCopies is < 1 or > 99)
            return "Number of copies must be between 1 and 99.";

        if (printer.FinishingMode == LabelFinishingMode.Cutter && !cutterInstalled)
            return "Cutter cannot be selected because this model profile has no verified cutter installed.";

        return null;
    }

    public static bool IsPrivateIpv4Address(string? value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
