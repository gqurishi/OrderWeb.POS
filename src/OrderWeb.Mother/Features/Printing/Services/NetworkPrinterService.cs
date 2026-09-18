using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Concurrent;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Service for direct TCP communication with network thermal printers
/// Sends ESC/POS bytes over the network. No Windows printer driver is installed or used.
/// </summary>
public class NetworkPrinterService
{
    private const int DefaultTimeout = 5000; // 5 seconds
    private const int MaxSendAttempts = 3;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EndpointLocks = new();

    /// <summary>
    /// Test if a printer is reachable at the given IP and port
    /// </summary>
    public async Task<PrinterConnectionResult> TestConnectionAsync(string ipAddress, int port = 9100)
    {
        var result = new PrinterConnectionResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            client.SendTimeout = DefaultTimeout;
            client.ReceiveTimeout = DefaultTimeout;

            // Try to connect
            var connectTask = client.ConnectAsync(ipAddress, port);
            var timeoutTask = Task.Delay(DefaultTimeout);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                result.Success = false;
                result.Message = "Connection timed out";
                return result;
            }

            if (!client.Connected)
            {
                result.Success = false;
                result.Message = "Failed to connect";
                return result;
            }

            stopwatch.Stop();
            result.Success = true;
            result.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            result.Message = $"Connected in {result.ResponseTimeMs}ms";

            // Try to get printer status (optional)
            try
            {
                var stream = client.GetStream();
                
                // Send status request (ESC/POS: DLE EOT n)
                // DLE = 0x10, EOT = 0x04, n = 1 (printer status)
                byte[] statusRequest = { 0x10, 0x04, 0x01 };
                await stream.WriteAsync(statusRequest);
                await stream.FlushAsync();

                // Brief wait for response
                await Task.Delay(100);

                if (stream.DataAvailable)
                {
                    byte[] buffer = new byte[16];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        result.Message += " • Status received";
                    }
                }
            }
            catch
            {
                // Status check failed, but connection is OK
            }

            Debug.WriteLine($" Printer connection test: {ipAddress}:{port} - {result.Message}");
        }
        catch (SocketException ex)
        {
            result.Success = false;
            result.Message = ex.SocketErrorCode switch
            {
                SocketError.HostNotFound => "Host not found",
                SocketError.ConnectionRefused => "Connection refused",
                SocketError.NetworkUnreachable => "Network unreachable",
                SocketError.TimedOut => "Connection timed out",
                _ => $"Socket error: {ex.SocketErrorCode}"
            };
            Debug.WriteLine($" Printer connection failed: {ipAddress}:{port} - {result.Message}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
            Debug.WriteLine($" Printer connection error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Send raw ESC/POS data directly to printer
    /// </summary>
    public async Task<bool> SendRawDataAsync(string ipAddress, int port, byte[] data)
    {
        var endpointKey = $"{ipAddress}:{port}";
        var endpointLock = EndpointLocks.GetOrAdd(endpointKey, _ => new SemaphoreSlim(1, 1));
        await endpointLock.WaitAsync();

        try
        {
            for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
            {
                try
                {
                    using var client = new TcpClient();
                    client.SendTimeout = DefaultTimeout;
                    client.ReceiveTimeout = DefaultTimeout;

                    var connectTask = client.ConnectAsync(ipAddress, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(DefaultTimeout)) != connectTask)
                    {
                        Debug.WriteLine($" Send attempt {attempt}/{MaxSendAttempts} timed out: {ipAddress}:{port}");
                        continue;
                    }

                    if (!client.Connected)
                    {
                        Debug.WriteLine($" Send attempt {attempt}/{MaxSendAttempts} failed to connect: {ipAddress}:{port}");
                        continue;
                    }

                    var stream = client.GetStream();
                    await stream.WriteAsync(data);
                    await stream.FlushAsync();

                    // Brief delay to ensure data is sent
                    await Task.Delay(50);

                    Debug.WriteLine($" Sent {data.Length} bytes to printer: {ipAddress}:{port} (attempt {attempt})");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($" Error sending to printer {ipAddress}:{port} on attempt {attempt}/{MaxSendAttempts}: {ex.Message}");
                }

                if (attempt < MaxSendAttempts)
                {
                    await Task.Delay(150 * attempt);
                }
            }

            Debug.WriteLine($" Failed to send data after {MaxSendAttempts} attempts: {ipAddress}:{port}");
            return false;
        }
        finally
        {
            endpointLock.Release();
        }
    }

    /// <summary>
    /// Send raw data to a NetworkPrinter object
    /// </summary>
    public async Task<bool> SendToPrinterAsync(NetworkPrinter printer, byte[] data)
    {
        return await SendRawDataAsync(printer.IpAddress, printer.Port, data);
    }

    /// <summary>
    /// Open cash drawer connected to printer
    /// </summary>
    public async Task<bool> OpenCashDrawerAsync(string ipAddress, int port = 9100)
    {
        try
        {
            // ESC/POS cash drawer command:
            // ESC p m t1 t2
            // ESC = 0x1B, p = 0x70
            // m = pin (0 = pin 2, 1 = pin 5)
            // t1/t2 = pulse timing (typically 25, 250)
            
            byte[] openDrawer = { 0x1B, 0x70, 0x00, 0x19, 0xFA };
            
            var result = await SendRawDataAsync(ipAddress, port, openDrawer);
            
            if (result)
            {
                Debug.WriteLine($" Cash drawer opened: {ipAddress}:{port}");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Error opening cash drawer: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Open cash drawer for a NetworkPrinter
    /// </summary>
    public async Task<bool> OpenCashDrawerAsync(NetworkPrinter printer)
    {
        if (!printer.HasCashDrawer)
        {
            Debug.WriteLine($" Printer {printer.Name} does not have cash drawer configured");
            return false;
        }

        return await OpenCashDrawerAsync(printer.IpAddress, printer.Port);
    }

    /// <summary>
    /// Get printer status (online, paper, cover, errors) via ESC/POS DLE EOT when the device replies.
    /// Weak printers that ignore DLE keep <see cref="PrinterStatus.StatusProbeSucceeded"/> false — honest target.
    /// </summary>
    public async Task<PrinterStatus> GetPrinterStatusAsync(string ipAddress, int port = 9100)
    {
        var status = new PrinterStatus();

        try
        {
            using var client = new TcpClient();
            client.SendTimeout = DefaultTimeout;
            client.ReceiveTimeout = DefaultTimeout;

            var connectTask = client.ConnectAsync(ipAddress, port);
            if (await Task.WhenAny(connectTask, Task.Delay(DefaultTimeout)) != connectTask)
            {
                status.IsOnline = false;
                status.ErrorDescription = "Connection timed out";
                status.CheckedAt = DateTime.Now;
                return status;
            }

            if (!client.Connected)
            {
                status.IsOnline = false;
                status.ErrorDescription = "Not connected";
                status.CheckedAt = DateTime.Now;
                return status;
            }

            status.IsOnline = true;
            var stream = client.GetStream();

            // DLE EOT 4 = Paper roll sensor
            if (await TryReadDleEotAsync(stream, n: 4) is { } paperByte)
            {
                status.StatusProbeSucceeded = true;
                status.PaperStatusKnown = true;
                // Bits 5–6: 11 = paper end
                var paperBits = (paperByte >> 5) & 0x03;
                status.HasPaper = paperBits != 0x03;
            }

            // DLE EOT 3 = Error cause
            if (await TryReadDleEotAsync(stream, n: 3) is { } errorByte)
            {
                status.StatusProbeSucceeded = true;
                status.HasError = (errorByte & 0x6C) != 0;
                if (status.HasError)
                {
                    status.ErrorDescription = "Printer error detected";
                }
            }

            // DLE EOT 2 = Offline cause (cover)
            if (await TryReadDleEotAsync(stream, n: 2) is { } offlineByte)
            {
                status.StatusProbeSucceeded = true;
                status.CoverOpen = (offlineByte & 0x04) != 0;
            }

            // DLE EOT 1 = Printer status — proves replies even if paper probe silent
            if (!status.StatusProbeSucceeded && await TryReadDleEotAsync(stream, n: 1) is not null)
            {
                status.StatusProbeSucceeded = true;
            }
        }
        catch (Exception ex)
        {
            status.IsOnline = false;
            status.HasError = true;
            status.ErrorDescription = ex.Message;
            Debug.WriteLine($" Error getting printer status: {ex.Message}");
        }

        status.CheckedAt = DateTime.Now;
        return status;
    }

    private static async Task<byte?> TryReadDleEotAsync(NetworkStream stream, byte n)
    {
        try
        {
            // Drain any leftover bytes so we read the reply for this request.
            while (stream.DataAvailable)
            {
                _ = stream.ReadByte();
            }

            byte[] request = { 0x10, 0x04, n };
            await stream.WriteAsync(request);
            await stream.FlushAsync();
            await Task.Delay(120);

            if (!stream.DataAvailable)
            {
                return null;
            }

            var buffer = new byte[16];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            return bytesRead > 0 ? buffer[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check status for a NetworkPrinter. Label printers skip ESC/POS DLE (no false paper blocks).
    /// </summary>
    public async Task<PrinterStatus> GetPrinterStatusAsync(NetworkPrinter printer)
    {
        if (printer.PrinterType == NetworkPrinterType.Label)
        {
            var reach = await TestConnectionAsync(printer.IpAddress, printer.Port);
            return new PrinterStatus
            {
                IsOnline = reach.Success,
                StatusProbeSucceeded = false,
                PaperStatusKnown = false,
                HasPaper = true,
                CheckedAt = DateTime.Now,
                ErrorDescription = reach.Success ? null : reach.Message
            };
        }

        return await GetPrinterStatusAsync(printer.IpAddress, printer.Port);
    }

    /// <summary>True when this printer type can use ESC/POS realtime status.</summary>
    public static bool SupportsEscPosRealtimeStatus(NetworkPrinter printer) =>
        printer.PrinterType != NetworkPrinterType.Label;

    /// <summary>
    /// Send a test print to verify printer is working
    /// </summary>
    public async Task<bool> SendTestPrintAsync(NetworkPrinter printer)
    {
        try
        {
            var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);

            builder.Initialize()
                   .SetAlign(TextAlign.Center)
                   .SetBold(true)
                   .SetFontSize(2, 2)
                   .PrintLine("TEST DONE")
                   .SetBold(false)
                   .SetNormalSize();

            if (printer.SupportsTwoColor)
            {
                builder.PrintLine("BLACK INK TEST")
                       .SetRedInk(true)
                       .SetBold(true)
                       .PrintLine("RED INK TEST")
                       .SetBold(false)
                       .SetRedInk(false);
            }

            builder.FeedLines(4);
            if (printer.HasCutter)
            {
                builder.Cut();
            }

            return await SendToPrinterAsync(printer, builder.Build());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" Test print failed for {printer.IpAddress}:{printer.Port}: {ex.Message}");
            return false;
        }
    }
}
