using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexusRemotePC;

public sealed class NexusServer : IDisposable
{
    private readonly CompanionStore _store;
    private readonly Action _openProgramManager;
    private readonly Func<string, string, bool> _confirmPairing;
    private readonly Func<string, string, bool> _confirmDangerousCommand;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Queue<DateTime>> _commandRate = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private Task? _loop;

    public NexusServer(
        CompanionStore store,
        Action openProgramManager,
        Func<string, string, bool> confirmPairing,
        Func<string, string, bool> confirmDangerousCommand)
    {
        _store = store;
        _openProgramManager = openProgramManager;
        _confirmPairing = confirmPairing;
        _confirmDangerousCommand = confirmDangerousCommand;
    }

    public int Port { get; } = 8765;
    public bool IsRunning { get; private set; }

    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        IsRunning = true;
        AppLogger.Info($"HTTP сервер слушает порт {Port}.");
        _loop = Task.Run(AcceptLoopAsync);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка в цикле ожидания TCP-клиента.", ex);
                await Task.Delay(150, _cts.Token).ContinueWith(_ => { });
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint remote && !NetworkUtil.IsAllowedClient(remote.Address))
            {
                _store.AddEvent("Ошибка", $"Отклонено подключение не из локальной сети: {remote.Address}");
                await WriteJsonAsync(client, 403, new { ok = false, error = "Local network only" });
                return;
            }

            using var stream = client.GetStream();
            var request = await HttpRequest.ReadAsync(stream);
            if (request == null) return;

            try
            {
                var response = await HandleRequestAsync(request);
                await WriteJsonAsync(client, response.Status, response.Body);
            }
            catch (Exception ex)
            {
                _store.AddEvent("Ошибка", ex.Message);
                AppLogger.Error("Ошибка обработки HTTP-запроса.", ex);
                await WriteJsonAsync(client, 500, new { ok = false, error = ex.Message });
            }
        }
    }

    private async Task<HttpResponse> HandleRequestAsync(HttpRequest request)
    {
        if (request.Method == "OPTIONS")
        {
            return new HttpResponse(200, new { ok = true });
        }

        if (request.Path == "/health")
        {
            return new HttpResponse(200, new { ok = true, service = "Nexus Remote PC", port = Port });
        }

        if (request.Path == "/pair")
        {
            return await HandlePairAsync(request);
        }

        var device = GetAuthorizedDevice(request);
        if (device == null)
        {
            return new HttpResponse(401, new { ok = false, error = "Unauthorized" });
        }

        return request.Path switch
        {
            "/snapshot" => new HttpResponse(200, new { ok = true, snapshot = MetricsCollector.CreateSnapshot(_store) }),
            "/context" => new HttpResponse(200, new { ok = true, context = ContextCollector.CreateContext() }),
            "/command" => await HandleCommandAsync(request, device),
            _ => new HttpResponse(404, new { ok = false, error = "Not found" })
        };
    }

    private Task<HttpResponse> HandlePairAsync(HttpRequest request)
    {
        if (request.Method != "POST")
        {
            return Task.FromResult(new HttpResponse(405, new { ok = false, error = "POST required" }));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
        var root = doc.RootElement;
        var pairingToken = root.TryGetProperty("pairingToken", out var tokenElement) ? tokenElement.GetString() ?? "" : "";
        var deviceName = root.TryGetProperty("deviceName", out var nameElement) ? nameElement.GetString() ?? "Android" : "Android";

        if (!_store.IsPairingTokenValid(pairingToken))
        {
            _store.AddEvent("Ошибка", "Сопряжение отклонено: QR устарел или неверный.");
            return Task.FromResult(new HttpResponse(401, new { ok = false, error = "Pairing token expired" }));
        }

        var allowed = App.Current.Dispatcher.Invoke(() => _confirmPairing(deviceName, "Android просит доступ к этому ПК."));
        if (!allowed)
        {
            _store.RotatePairingToken();
            _store.AddEvent("Событие", $"Сопряжение отклонено: {deviceName}");
            return Task.FromResult(new HttpResponse(403, new { ok = false, error = "Pairing rejected" }));
        }

        var device = _store.AddDevice(deviceName);
        _store.AddEvent("Событие", $"Подключено устройство: {device.Name}");
        return Task.FromResult(new HttpResponse(200, new
        {
            ok = true,
            device = new
            {
                id = device.Id,
                name = device.Name,
                token = device.Token
            }
        }));
    }

    private TrustedDevice? GetAuthorizedDevice(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Nexus-Device-Token", out var header))
        {
            return _store.ValidateDeviceToken(header);
        }
        if (request.Headers.TryGetValue("X-Nexus-Key", out var legacyHeader))
        {
            return _store.ValidateDeviceToken(legacyHeader);
        }
        if (request.Query.TryGetValue("deviceToken", out var query))
        {
            return _store.ValidateDeviceToken(query);
        }
        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            try
            {
                using var doc = JsonDocument.Parse(request.Body);
                if (doc.RootElement.TryGetProperty("deviceToken", out var deviceToken))
                {
                    return _store.ValidateDeviceToken(deviceToken.GetString() ?? "");
                }
                if (doc.RootElement.TryGetProperty("token", out var token))
                {
                    return _store.ValidateDeviceToken(token.GetString() ?? "");
                }
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private Task<HttpResponse> HandleCommandAsync(HttpRequest request, TrustedDevice device)
    {
        if (request.Method != "POST")
        {
            return Task.FromResult(new HttpResponse(405, new { ok = false, error = "POST required" }));
        }

        if (!AllowCommand(device.Id))
        {
            _store.AddEvent("Ошибка", $"Rate limit для устройства: {device.Name}");
            return Task.FromResult(new HttpResponse(429, new { ok = false, error = "Too many commands" }));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "";
        var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

        if (type == "programs_gui_open")
        {
            App.Current.Dispatcher.BeginInvoke(_openProgramManager);
            return Task.FromResult(new HttpResponse(200, new { ok = true }));
        }

        if (type == "programs_refresh")
        {
            return Task.FromResult(new HttpResponse(200, new { ok = true, programs = _store.LoadPrograms().Select(ProgramRuntime.ToStatus) }));
        }

        if (CommandExecutor.IsDangerous(type))
        {
            var allowed = App.Current.Dispatcher.Invoke(() =>
                _confirmDangerousCommand(device.Name, CommandExecutor.GetDangerousPrompt(type)));
            if (!allowed)
            {
                _store.AddEvent("Событие", $"Команда отклонена: {type} от {device.Name}");
                return Task.FromResult(new HttpResponse(403, new { ok = false, error = "Command rejected" }));
            }
        }

        var result = CommandExecutor.Execute(type, payload);
        if (!result.Ok)
        {
            _store.AddEvent("Ошибка", $"{type}: {result.Error}");
        }
        return Task.FromResult(new HttpResponse(result.Ok ? 200 : 400, result));
    }

    private bool AllowCommand(string deviceId)
    {
        lock (_commandRate)
        {
            var now = DateTime.UtcNow;
            if (!_commandRate.TryGetValue(deviceId, out var queue))
            {
                queue = new Queue<DateTime>();
                _commandRate[deviceId] = queue;
            }
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromSeconds(10))
            {
                queue.Dequeue();
            }
            if (queue.Count >= 30) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    private static async Task WriteJsonAsync(TcpClient client, int status, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {Reason(status)}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Headers: Content-Type, X-Nexus-Key, X-Nexus-Device-Token\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await client.GetStream().WriteAsync(header);
        await client.GetStream().WriteAsync(bytes);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        _ => "Error"
    };

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        AppLogger.Info("HTTP сервер остановлен.");
        _cts.Dispose();
    }
}

public sealed record HttpResponse(int Status, object Body);

public sealed class HttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required Dictionary<string, string> Query { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required string Body { get; init; }

    public static async Task<HttpRequest?> ReadAsync(NetworkStream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine)) return null;
        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            headers[line[..separator]] = line[(separator + 1)..].Trim();
        }

        var length = headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : 0;
        var body = "";
        if (length > 0)
        {
            var buffer = new char[length];
            var read = 0;
            while (read < length)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(read, length - read));
                if (count <= 0) break;
                read += count;
            }
            body = new string(buffer, 0, read);
        }

        var uri = new Uri("http://nexus.local" + parts[1]);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]),
                pair => pair.Length > 1 ? WebUtility.UrlDecode(pair[1]) : "",
                StringComparer.OrdinalIgnoreCase);

        return new HttpRequest
        {
            Method = parts[0].ToUpperInvariant(),
            Path = uri.AbsolutePath,
            Query = query,
            Headers = headers,
            Body = body
        };
    }
}
