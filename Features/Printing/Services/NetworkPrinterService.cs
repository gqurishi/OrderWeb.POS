using System.Net.Sockets;
using System.Diagnostics;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Service for direct TCP communication with network thermal printers
/// Supports ESC/POS compatible printers (Epson, Star, etc.)
/// </summary>
public class NetworkPrinterService
{
    private const int DefaultTimeout = 5000; // 5 seconds
    private const int MaxSendAttempts = 3;

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
    /// Get printer status (online, paper, cover, errors)
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
                return status;
            }

            if (!client.Connected)
            {
                status.IsOnline = false;
                return status;
            }

            status.IsOnline = true;
            var stream = client.GetStream();

            // Request different status types
            // DLE EOT 1 = Printer status
            // DLE EOT 2 = Offline cause status
            // DLE EOT 3 = Error cause status
            // DLE EOT 4 = Paper roll sensor status

            // Request paper status (DLE EOT 4)
            byte[] paperStatusRequest = { 0x10, 0x04, 0x04 };
            await stream.WriteAsync(paperStatusRequest);
            await stream.FlushAsync();
            await Task.Delay(100);

            if (stream.DataAvailable)
            {
                byte[] buffer = new byte[16];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    // Parse paper status (bit 5-6 of response)
                    // Bit 5-6: 00 = paper present, 11 = paper end
                    var paperBits = (buffer[0] >> 5) & 0x03;
                    status.HasPaper = paperBits != 0x03;
                }
            }

            // Request error status (DLE EOT 3)
            byte[] errorStatusRequest = { 0x10, 0x04, 0x03 };
            await stream.WriteAsync(errorStatusRequest);
            await stream.FlushAsync();
            await Task.Delay(100);

            if (stream.DataAvailable)
            {
                byte[] buffer = new byte[16];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    // Bit 2: Recoverable error
                    // Bit 3: Auto-cutter error
                    // Bit 5: Unrecoverable error
                    // Bit 6: Auto-recoverable error
                    status.HasError = (buffer[0] & 0x6C) != 0;
                    if (status.HasError)
                    {
                        status.ErrorDescription = "Printer error detected";
                    }
                }
            }

            // Request offline cause (DLE EOT 2)
            byte[] offlineRequest = { 0x10, 0x04, 0x02 };
            await stream.WriteAsync(offlineRequest);
            await stream.FlushAsync();
            await Task.Delay(100);

            if (stream.DataAvailable)
            {
                byte[] buffer = new byte[16];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    // Bit 2: Cover open
                    status.CoverOpen = (buffer[0] & 0x04) != 0;
                }
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

    /// <summary>
    /// Check status for a NetworkPrinter
    /// </summary>
    public async Task<PrinterStatus> GetPrinterStatusAsync(NetworkPrinter printer)
    {
        return await GetPrinterStatusAsync(printer.IpAddress, printer.Port);
    }

    /// <summary>
    /// Send a test print to verify printer is working
    /// </summary>
    public async Task<bool> SendTestPrintAsync(NetworkPrinter printer)
    {
        var builder = new EscPosBuilder(printer.Brand, printer.PaperWidth);
        
        builder.Initialize()
               .SetAlign(TextAlign.Center)
               .SetBold(true)
               .SetFontSize(2, 2)
               .PrintLine("TEST PRINT")
               .SetFontSize(1, 1)
               .SetBold(false)
               .FeedLines(1)
               .PrintLine($"Printer: {printer.Name}")
               .PrintLine($"IP: {printer.IpAddress}:{printer.Port}")
               .PrintLine($"Brand: {printer.Brand}")
               .PrintLine($"Type: {printer.PrinterType}")
               .FeedLines(1)
               .PrintDivider()
               .PrintLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
               .PrintLine("Connection: OK")
               .PrintDivider()
               .FeedLines(2);

        if (printer.HasBuzzer)
        {
            builder.Buzzer();
        }

        builder.Cut();

        var data = builder.Build();
        return await SendToPrinterAsync(printer, data);
    }
}
