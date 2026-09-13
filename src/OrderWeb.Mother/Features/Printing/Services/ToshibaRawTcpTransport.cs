using System.Collections.Concurrent;
using System.Net.Sockets;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Serialised raw-TCP transport for one Toshiba endpoint. It reports network
/// milestones independently and never equates a successful write with paper output.
/// </summary>
public sealed class ToshibaRawTcpTransport
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EndpointLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToshibaNetworkTimeouts _timeouts;

    public ToshibaRawTcpTransport(ToshibaNetworkTimeouts? timeouts = null) =>
        _timeouts = (timeouts ?? ToshibaNetworkTimeouts.Default).Validate();

    public async Task<ToshibaNetworkResult> SendJobAsync(
        string printerIpAddress,
        int port,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> statusQuery,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty) throw new ArgumentException("A TPCL payload is required.", nameof(payload));
        ValidateEndpoint(printerIpAddress, port);

        var endpoint = $"{printerIpAddress}:{port}";
        var endpointLock = EndpointLocks.GetOrAdd(endpoint, _ => new SemaphoreSlim(1, 1));
        var startedAt = DateTimeOffset.UtcNow;
        using var completeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        completeTimeout.CancelAfter(_timeouts.CompleteJobTimeout);

        try
        {
            await endpointLock.WaitAsync(completeTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            return ToshibaNetworkResult.Failed(endpoint, ToshibaNetworkPhase.EndpointQueue, startedAt, "Timed out waiting for exclusive access to the printer endpoint.");
        }

        try
        {
            using var client = new TcpClient { NoDelay = true };
            try
            {
                using var connectTimeout = StageTimeout(completeTimeout.Token, _timeouts.ConnectionTimeout);
                await client.ConnectAsync(printerIpAddress, port, connectTimeout.Token);
            }
            catch (SocketException ex)
            {
                var networkReachable = ex.SocketErrorCode == SocketError.ConnectionRefused;
                return ToshibaNetworkResult.Failed(endpoint, ToshibaNetworkPhase.Connect, startedAt, ex.Message, networkReachable);
            }
            catch (OperationCanceledException)
            {
                return ToshibaNetworkResult.Failed(endpoint, ToshibaNetworkPhase.Connect, startedAt, "Connection timeout expired.");
            }

            await using var stream = client.GetStream();
            try
            {
                using var sendTimeout = StageTimeout(completeTimeout.Token, _timeouts.DataSendTimeout);
                await stream.WriteAsync(payload, sendTimeout.Token);
                await stream.FlushAsync(sendTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                return ToshibaNetworkResult.Failed(endpoint, ToshibaNetworkPhase.Send, startedAt, "Data-send timeout expired.", true, true);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                return ToshibaNetworkResult.Failed(endpoint, ToshibaNetworkPhase.Send, startedAt, ex.Message, true, true);
            }

            if (statusQuery.IsEmpty)
                return ToshibaNetworkResult.DataSent(endpoint, startedAt);

            try
            {
                using var statusTimeout = StageTimeout(completeTimeout.Token, _timeouts.StatusResponseTimeout);
                await stream.WriteAsync(statusQuery, statusTimeout.Token);
                await stream.FlushAsync(statusTimeout.Token);
                var buffer = new byte[256];
                var count = await stream.ReadAsync(buffer, statusTimeout.Token);
                if (count == 0)
                    return ToshibaNetworkResult.DataSent(endpoint, startedAt, "Printer closed the connection without a status response.");
                var response = buffer[..count];
                return IsExpectedWbStatus(response)
                    ? ToshibaNetworkResult.ProtocolConfirmed(endpoint, startedAt, response)
                    : ToshibaNetworkResult.DataSent(endpoint, startedAt, "A response was received, but it was not a valid 23-byte Toshiba WB status block.", response);
            }
            catch (OperationCanceledException)
            {
                return ToshibaNetworkResult.DataSent(endpoint, startedAt, "Data was sent, but the status-response timeout expired.");
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                return ToshibaNetworkResult.DataSent(endpoint, startedAt, $"Data was sent, but status could not be read: {ex.Message}");
            }
        }
        finally
        {
            endpointLock.Release();
        }
    }

    public async Task SendAsync(string printerIpAddress, int port, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var result = await SendJobAsync(printerIpAddress, port, payload, ReadOnlyMemory<byte>.Empty, cancellationToken);
        if (!result.DataAccepted) throw new IOException(result.ErrorMessage ?? "The Toshiba label data was not accepted by the network transport.");
    }

    private static CancellationTokenSource StageTimeout(CancellationToken completeJobToken, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(completeJobToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static void ValidateEndpoint(string address, int port)
    {
        if (!System.Net.IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("A valid private IPv4 printer address is required.", nameof(address));
        if (!IsPrivate(parsed)) throw new ArgumentException("The label printer must use a private POS-network address.", nameof(address));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
    }

    private static bool IsPrivate(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsExpectedWbStatus(byte[] response) =>
        response.Length == 23
        && response[0] == 0x01
        && response[1] == 0x02
        && response[4] == 0x33
        && response[9] == (byte)'2'
        && response[10] == (byte)'3'
        && response[21] == 0x0D
        && response[22] == 0x0A;
}
