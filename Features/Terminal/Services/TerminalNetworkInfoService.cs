using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace POS_in_NET.Services;

public static class TerminalNetworkInfoService
{
    public static string GetBestLocalIpAddress()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    IsPrivateLanAddress(address))
                .Select(address => address.ToString())
                .OrderBy(address => address.StartsWith("192.168.", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(address => address.StartsWith("10.", StringComparison.Ordinal) ? 0 : 1)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(addresses) ? "Mother LAN IP" : addresses;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not resolve local IP: {ex.Message}");
            return "Mother LAN IP";
        }
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}
