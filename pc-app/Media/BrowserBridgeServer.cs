using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Collections.Concurrent;

namespace NexusRemotePC.Media;

public sealed class BrowserBridgeServer : IDisposable
{
    public static BrowserBridgeServer Shared { get; } = new();

    private readonly BrowserSessionRegistry _registry;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new(StringComparer.Ordinal);
    private HttpListener? _listener;
    private Task? _loopTask;

    public BrowserBridgeServer()
        : this(BrowserSessionRegistry.Shared)
    {
    }

    public BrowserBridgeServer(BrowserSessionRegistry registry)
    {
        _registry = registry;
    }

    public int Port { get; } = 8767;
    public bool IsRunning { get; private set; }

    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        IsRunning = true;
        AppLogger.Info($"Browser bridge слушает ws://127.0.0.1:{Port}/browser-bridge");
        _loopTask = Task.Run(AcceptLoopAsync);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener != null)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context));
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка browser bridge accept loop.", ex);
                await Task.Delay(150, _cts.Token).ContinueWith(_ => { });
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            if (!IsLoopback(context.Request.RemoteEndPoint))
            {
                context.Response.StatusCode = 403;
                await WriteJsonAsync(context.Response, new { ok = false, error = "Loopback only" });
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/health")
            {
                context.Response.StatusCode = 200;
                await WriteJsonAsync(context.Response, new { ok = true, service = "Nexus Remote Browser Bridge", port = Port });
                return;
            }

            if (context.Request.Url?.AbsolutePath != "/browser-bridge" || !context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context.Response, new { ok = false, error = "Not found" });
                return;
            }

            var socketContext = await context.AcceptWebSocketAsync(null);
            AppLogger.Info("Browser extension connected to bridge.");
            await HandleSocketAsync(socketContext.WebSocket, _cts.Token);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Ошибка обработки browser bridge запроса.", ex);
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                    await WriteJsonAsync(context.Response, new { ok = false, error = ex.Message });
                }
            }
            catch
            {
                // Ignore response write failures after aborted sockets.
            }
        }
    }

    private async Task HandleSocketAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using (socket)
        {
            var clientId = Guid.NewGuid().ToString("N");
            _clients[clientId] = socket;
            var buffer = new byte[32 * 1024];

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult? result = null;

                    do
                    {
                        result = await socket.ReceiveAsync(buffer, cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", cancellationToken);
                            return;
                        }

                        if (result.MessageType != WebSocketMessageType.Text)
                        {
                            continue;
                        }

                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var payload = Encoding.UTF8.GetString(message.ToArray());
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        await HandleMessageAsync(payload, cancellationToken);
                    }
                }
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
            }
        }
    }

    public async Task<bool> SendCommandAsync(string commandType, string? sourceId, object payload, CancellationToken cancellationToken)
    {
        var sockets = _clients.Values
            .Where(socket => socket.State == WebSocketState.Open)
            .ToArray();

        if (sockets.Length == 0)
        {
            return false;
        }

        var message = JsonSerializer.Serialize(new
        {
            type = "command",
            command = new
            {
                type = commandType,
                sourceId,
                payload
            }
        }, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);
        var sent = false;

        foreach (var socket in sockets)
        {
            try
                {
                    await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
                    sent = true;
                }
            catch (Exception ex)
            {
                AppLogger.Warn($"Browser bridge command send failed: {ex.Message}");
            }
        }

        return sent;
    }

    public bool HasConnectedClients => _clients.Values.Any(socket => socket.State == WebSocketState.Open);

    private async Task HandleMessageAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? "" : "";

        switch (type)
        {
            case "hello":
                var browser = root.TryGetProperty("browser", out var browserNode) ? browserNode.GetString() ?? "unknown" : "unknown";
                AppLogger.Info($"Browser bridge hello: {browser}");
                break;

            case "media_state":
                if (root.TryGetProperty("session", out var sessionNode))
                {
                    var update = BrowserSessionUpdate.FromJson(sessionNode);
                    if (update != null)
                    {
                        await _registry.UpsertAsync(update, cancellationToken);
                    }
                }
                break;

            case "media_batch":
                if (root.TryGetProperty("sessions", out var sessionsNode) && sessionsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemNode in sessionsNode.EnumerateArray())
                    {
                        var update = BrowserSessionUpdate.FromJson(itemNode);
                        if (update != null)
                        {
                            await _registry.UpsertAsync(update, cancellationToken);
                        }
                    }
                }
                break;

            case "media_gone":
                var sourceId = root.TryGetProperty("sourceId", out var sourceIdNode) ? sourceIdNode.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(sourceId))
                {
                    _registry.Remove(sourceId);
                }
                break;
        }
    }

    private static bool IsLoopback(IPEndPoint? endpoint)
    {
        if (endpoint == null)
        {
            return false;
        }

        return IPAddress.IsLoopback(endpoint.Address);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonDefaults.Options));
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _cts.Dispose();
    }
}
