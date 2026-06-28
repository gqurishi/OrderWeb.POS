using System.Net;
using System.Text;
using System.Text.Json;

namespace POS_in_NET.Services;

/// <summary>
/// Inbound webhook listener for OrderWeb cloud push (reservation_created, order_created).
/// Register the till LAN URL in OrderWeb admin, e.g. http://192.168.1.10:8080/orderweb/webhook/
/// </summary>
public sealed class OrderWebWebhookListenerService : IDisposable
{
    private readonly OrderWebWebhookRouterService _webhookRouter;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _port;
    private bool _isRunning;

    public OrderWebWebhookListenerService(OrderWebWebhookRouterService webhookRouter)
    {
        _webhookRouter = webhookRouter;
    }

    public bool IsRunning => _isRunning;
    public string? ListeningUrl { get; private set; }

    public async Task StartAsync(int port = 8080)
    {
        if (_isRunning)
        {
            return;
        }

        _port = Math.Clamp(port, 1024, 65535);
        _listener = new HttpListener();

        try
        {
            _listener.Prefixes.Add($"http://+:{_port}/orderweb/webhook/");
            _listener.Start();
            ListeningUrl = $"http://+:{_port}/orderweb/webhook/";
        }
        catch (HttpListenerException)
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/orderweb/webhook/");
            _listener.Prefixes.Add($"http://localhost:{_port}/orderweb/webhook/");
            _listener.Start();
            ListeningUrl = $"http://127.0.0.1:{_port}/orderweb/webhook/";
            AppDiagnostics.Log("Webhook listener bound to localhost only. Use LAN IP + port forwarding for cloud push.");
        }

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _isRunning = true;
        AppDiagnostics.Log($"OrderWeb webhook listener started on {ListeningUrl}");
        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
        _isRunning = false;
        AppDiagnostics.Log("OrderWeb webhook listener stopped.");
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log($"Webhook listener error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(context, HttpStatusCode.MethodNotAllowed, "{\"success\":false}");
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var eventType = context.Request.Headers["X-Event-Type"]
                            ?? context.Request.Headers["x-event-type"];

            var result = await _webhookRouter.ProcessWebhookAsync(body, eventType);
            await WriteResponseAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                JsonSerializer.Serialize(new { success = result.Success, message = result.Message }));
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Webhook request failed: {ex.Message}");
            await WriteResponseAsync(context, HttpStatusCode.InternalServerError, "{\"success\":false}");
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, HttpStatusCode status, string json)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        var buffer = Encoding.UTF8.GetBytes(json);
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
