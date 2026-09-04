using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OrderWeb.Client.Services;

public sealed class MotherTerminalControlEventArgs : EventArgs
{
    public MotherTerminalControlEventArgs(string eventType, string message)
    {
        EventType = eventType;
        Message = message;
    }

    public string EventType { get; }
    public string Message { get; }
}

public sealed class MotherConnectionChangedEventArgs : EventArgs
{
    public MotherConnectionChangedEventArgs(bool connected, string status)
    {
        Connected = connected;
        Status = status;
    }

    public bool Connected { get; }
    public string Status { get; }
}

public sealed class MotherEventClient : IAsyncDisposable
{
    private static readonly TimeSpan[] ReconnectDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    private readonly ClientCacheService _cache;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _connectionTask;
    private int _stopRequested;

    public MotherEventClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public event EventHandler<MotherTerminalControlEventArgs>? TerminalControlReceived;
    public event EventHandler<MotherConnectionChangedEventArgs>? ConnectionChanged;
    public bool IsRunning => _connectionTask is { IsCompleted: false };

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var settings = await _cache.GetMotherConnectionAsync();
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.WebSocketUrl) ||
            string.IsNullOrWhiteSpace(settings.TerminalId) ||
            string.IsNullOrWhiteSpace(settings.TerminalToken))
        {
            return;
        }

        Interlocked.Exchange(ref _stopRequested, 0);
        _cts = new CancellationTokenSource();
        _connectionTask = Task.Run(() => ConnectionLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _cts?.Cancel();

        var socket = _webSocket;
        if (socket is { State: WebSocketState.Open })
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", CancellationToken.None);
            }
            catch
            {
                socket.Abort();
            }
        }

        socket?.Dispose();
        _webSocket = null;
        var connectionTask = _connectionTask;
        if (connectionTask is not null)
        {
            try
            {
                await connectionTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _connectionTask = null;
        _cts?.Dispose();
        _cts = null;
        RaiseConnectionChanged(false, "Disconnected");
    }

    private async Task ConnectionLoopAsync(CancellationToken cancellationToken)
    {
        var retryNumber = 0;

        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _stopRequested) == 0)
        {
            var settings = await _cache.GetMotherConnectionAsync();
            if (settings is null ||
                string.IsNullOrWhiteSpace(settings.WebSocketUrl) ||
                string.IsNullOrWhiteSpace(settings.TerminalId) ||
                string.IsNullOrWhiteSpace(settings.TerminalToken))
            {
                RaiseConnectionChanged(false, "Not paired");
                return;
            }

            using var socket = new ClientWebSocket();
            _webSocket = socket;
            try
            {
                RaiseConnectionChanged(false, retryNumber == 0 ? "Connecting" : "Reconnecting");
                await socket.ConnectAsync(BuildWebSocketUri(settings), cancellationToken);
                retryNumber = 0;
                RaiseConnectionChanged(true, "Connected");
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                RaiseConnectionChanged(false, "Mother POS connection lost");
            }
            finally
            {
                if (ReferenceEquals(_webSocket, socket))
                {
                    _webSocket = null;
                }
            }

            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _stopRequested) != 0)
            {
                break;
            }

            var delay = ReconnectDelays[Math.Min(retryNumber, ReconnectDelays.Length - 1)];
            retryNumber++;
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        RaiseConnectionChanged(false, "Disconnected");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested &&
               Volatile.Read(ref _stopRequested) == 0 &&
               socket.State == WebSocketState.Open)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                await HandleMessageAsync(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    private async Task HandleMessageAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var eventType = typeElement.GetString() ?? string.Empty;
        if (string.Equals(eventType, "TERMINAL_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            await _cache.MarkTerminalDisabledAsync("This Client POS has been disabled by the Mother POS.");
            await _cache.ClearLoginSessionAsync();
            TerminalControlReceived?.Invoke(this, new MotherTerminalControlEventArgs(eventType, "This Client POS has been disabled by the Mother POS."));
            return;
        }

        if (string.Equals(eventType, "TERMINAL_FORCE_LOGOUT", StringComparison.OrdinalIgnoreCase))
        {
            await _cache.ClearLoginSessionAsync();
            TerminalControlReceived?.Invoke(this, new MotherTerminalControlEventArgs(eventType, "Mother POS forced this terminal to log out."));
        }
    }

    private static Uri BuildWebSocketUri(MotherConnectionSettings settings)
    {
        var separator = settings.WebSocketUrl.Contains('?') ? '&' : '?';
        var url = $"{settings.WebSocketUrl}{separator}terminalId={Uri.EscapeDataString(settings.TerminalId)}&token={Uri.EscapeDataString(settings.TerminalToken)}";
        return new Uri(url);
    }

    private void RaiseConnectionChanged(bool connected, string status)
    {
        ConnectionChanged?.Invoke(this, new MotherConnectionChangedEventArgs(connected, status));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
