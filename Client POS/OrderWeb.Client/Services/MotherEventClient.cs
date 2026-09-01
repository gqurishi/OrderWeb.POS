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

public sealed class MotherEventClient : IAsyncDisposable
{
    private readonly ClientCacheService _cache;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public MotherEventClient(ClientCacheService cache)
    {
        _cache = cache;
    }

    public event EventHandler<MotherTerminalControlEventArgs>? TerminalControlReceived;
    public bool IsRunning => _receiveTask is { IsCompleted: false };

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

        _cts = new CancellationTokenSource();
        _webSocket = new ClientWebSocket();
        var uri = BuildWebSocketUri(settings);
        await _webSocket.ConnectAsync(uri, _cts.Token);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_webSocket, _cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_webSocket is { State: WebSocketState.Open })
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", CancellationToken.None);
            }
            catch
            {
                _webSocket.Abort();
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
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
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await StopAsync();
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
            await _cache.MarkTerminalDisabledAsync("This Client POS has been disabled by the Mother POS.");
            await _cache.ClearLoginSessionAsync();
            TerminalControlReceived?.Invoke(this, new MotherTerminalControlEventArgs(eventType, "This Client POS has been disabled by the Mother POS."));
            await StopAsync();
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

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
