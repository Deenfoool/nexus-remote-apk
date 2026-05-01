using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NexusRemotePC.Media;

namespace NexusRemotePC;

public static class MetricsCollector
{
    private static readonly object Sync = new();
    private static PerformanceCounter? _cpuCounter;
    private static PerformanceCounter? _diskActivityCounter;
    private static DateTime _lastNetworkAt = DateTime.UtcNow;
    private static long _lastReceived;
    private static long _lastSent;
    private static int _lastCpu;
    private static int _lastDown;
    private static int _lastUp;
    private static int? _lastDiskActivity;
    private static readonly MediaBroker MediaBroker = MediaBroker.CreateDefault();

    public static object CreateSnapshot(CompanionStore store)
    {
        var memory = GetMemory();
        var disk = GetDisk();
        var diskActivity = GetDiskActivity();
        var network = GetNetwork();
        var hardware = HardwareSensors.Read();
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var media = GetMediaSnapshot();

        return new
        {
            cpu = new { load = GetCpuLoad() },
            gpu = new { load = hardware.GpuLoad ?? GetGpuLoad() },
            ram = new
            {
                used_gb = memory.UsedGb,
                total_gb = memory.TotalGb,
                percent = memory.Percent
            },
            disk = new
            {
                percent = disk.Percent,
                used_gb = disk.UsedGb,
                total_gb = disk.TotalGb
            },
            storage = new { activity = diskActivity, temperature = hardware.StorageTemperature },
            fans = new { rpm = hardware.FanRpm },
            network = new
            {
                down_mbps = network.DownMbps,
                up_mbps = network.UpMbps,
                total_mbps = network.DownMbps + network.UpMbps
            },
            system = new
            {
                hostname = Environment.MachineName,
                os = GetOsName(),
                uptime = FormatUptime(uptime),
                volume = VolumeController.TryGetVolume(),
                brightness = Brightness.TryGetBrightness(),
                processes = Process.GetProcesses().Length,
                power = "Нормально",
                last_action = CommandExecutor.LastAction
            },
            media,
            programs = store.LoadPrograms().Select(ProgramRuntime.ToStatus).ToArray()
        };
    }

    private static object GetMediaSnapshot()
    {
        try
        {
            var snapshot = MediaBroker.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
            var active = snapshot.Sources.FirstOrDefault(source => source.SourceId == snapshot.ActiveSourceId);
            return new
            {
                active_source_id = snapshot.ActiveSourceId,
                updated_at_utc = snapshot.UpdatedAtUtc,
                title = active?.Title ?? "",
                process = active?.AppName ?? "",
                position = active?.PositionMs ?? 0,
                duration = active?.DurationMs ?? 0,
                sources = snapshot.Sources.Select(source => new
                {
                    source_id = source.SourceId,
                    source_type = source.SourceType,
                    app_id = source.AppId,
                    app_name = source.AppName,
                    site = source.Site,
                    tab_title = source.TabTitle,
                    media_kind = source.MediaKind,
                    title = source.Title,
                    artist = source.Artist,
                    album = source.Album,
                    artwork_url = source.ArtworkUrl,
                    artwork_base64 = source.ArtworkBase64,
                    position_ms = source.PositionMs,
                    duration_ms = source.DurationMs,
                    is_playing = source.IsPlaying,
                    is_muted = source.IsMuted,
                    priority = source.Priority,
                    last_updated_utc = source.LastUpdatedUtc,
                    capabilities = new
                    {
                        can_play = source.Capabilities.CanPlay,
                        can_pause = source.Capabilities.CanPause,
                        can_toggle_play_pause = source.Capabilities.CanTogglePlayPause,
                        can_next = source.Capabilities.CanNext,
                        can_previous = source.Capabilities.CanPrevious,
                        can_seek = source.Capabilities.CanSeek
                    }
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Media snapshot fallback: {ex.Message}");
            return new
            {
                active_source_id = (string?)null,
                updated_at_utc = DateTime.UtcNow,
                title = "",
                process = "",
                position = 0,
                duration = 0,
                sources = Array.Empty<object>()
            };
        }
    }

    private static int GetCpuLoad()
    {
        lock (Sync)
        {
            try
            {
                _cpuCounter ??= new PerformanceCounter("Processor", "% Processor Time", "_Total");
                var value = (int)Math.Clamp(Math.Round(_cpuCounter.NextValue()), 0, 100);
                if (value == 0 && _lastCpu == 0)
                {
                    Thread.Sleep(150);
                    value = (int)Math.Clamp(Math.Round(_cpuCounter.NextValue()), 0, 100);
                }
                if (value > 0) _lastCpu = value;
                return value == 0 ? _lastCpu : value;
            }
            catch
            {
                return _lastCpu;
            }
        }
    }

    private static int? GetGpuLoad()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return null;
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit(800);
            return int.TryParse(output, out var value) ? Math.Clamp(value, 0, 100) : null;
        }
        catch
        {
            return null;
        }
    }

    private static (double UsedGb, double TotalGb, int Percent) GetMemory()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);

        var total = status.ullTotalPhys / 1024d / 1024d / 1024d;
        var free = status.ullAvailPhys / 1024d / 1024d / 1024d;
        var used = Math.Max(0, total - free);
        return (Math.Round(used, 1), Math.Round(total, 1), (int)Math.Clamp(status.dwMemoryLoad, 0, 100));
    }

    private static (int Percent, long UsedGb, long TotalGb) GetDisk()
    {
        try
        {
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(item => item.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", StringComparison.OrdinalIgnoreCase)
                                        && item.IsReady)
                        ?? DriveInfo.GetDrives().First(item => item.IsReady);
            var total = drive.TotalSize;
            var used = total - drive.AvailableFreeSpace;
            var percent = total > 0 ? (int)Math.Clamp(used * 100d / total, 0, 100) : 0;
            return (percent, (long)Math.Round(used / 1024d / 1024d / 1024d), (long)Math.Round(total / 1024d / 1024d / 1024d));
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static int? GetDiskActivity()
    {
        lock (Sync)
        {
            try
            {
                _diskActivityCounter ??= new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                var value = (int)Math.Clamp(Math.Round(_diskActivityCounter.NextValue()), 0, 100);
                _lastDiskActivity = value;
                return value;
            }
            catch
            {
                return _lastDiskActivity;
            }
        }
    }

    private static (int DownMbps, int UpMbps) GetNetwork()
    {
        lock (Sync)
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToArray();
                var received = interfaces.Sum(nic => nic.GetIPv4Statistics().BytesReceived);
                var sent = interfaces.Sum(nic => nic.GetIPv4Statistics().BytesSent);
                var now = DateTime.UtcNow;
                if (_lastReceived == 0 && _lastSent == 0)
                {
                    _lastNetworkAt = now;
                    _lastReceived = received;
                    _lastSent = sent;
                    return (_lastDown, _lastUp);
                }
                var seconds = Math.Max(0.5, (now - _lastNetworkAt).TotalSeconds);
                var down = (int)Math.Max(0, Math.Round((received - _lastReceived) * 8d / seconds / 1_000_000d));
                var up = (int)Math.Max(0, Math.Round((sent - _lastSent) * 8d / seconds / 1_000_000d));
                _lastNetworkAt = now;
                _lastReceived = received;
                _lastSent = sent;
                if (down < 10_000) _lastDown = down;
                if (up < 10_000) _lastUp = up;
                return (_lastDown, _lastUp);
            }
            catch
            {
                return (_lastDown, _lastUp);
            }
        }
    }

    private static string GetOsName()
    {
        var caption = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName",
            null) as string;
        return string.IsNullOrWhiteSpace(caption) ? Environment.OSVersion.VersionString : caption;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays}д {uptime.Hours}ч";
        return $"{uptime.Hours}ч {uptime.Minutes}м";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
