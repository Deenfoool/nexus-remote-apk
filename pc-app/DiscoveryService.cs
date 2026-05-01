using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NexusRemotePC;

public sealed class DiscoveryService : IDisposable
{
    private readonly int _serverPort;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;

    public DiscoveryService(int serverPort)
    {
        _serverPort = serverPort;
    }

    public void Start()
    {
        _udp = new UdpClient(8766) { EnableBroadcast = true };
        _ = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        if (_udp == null) return;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(_cts.Token);
                var message = Encoding.UTF8.GetString(result.Buffer);
                if (!message.Contains("NEXUS_REMOTE_DISCOVER", StringComparison.OrdinalIgnoreCase)) continue;

                var payload = JsonSerializer.Serialize(new
                {
                    ok = true,
                    name = "Nexus Remote PC",
                    host = NetworkUtil.GetPrimaryIPv4Address(),
                    port = _serverPort
                });
                var bytes = Encoding.UTF8.GetBytes(payload);
                await _udp.SendAsync(bytes, result.RemoteEndPoint);
            }
            catch when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(250);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
        _cts.Dispose();
    }
}
