using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexusRemotePC;

public sealed class CompanionStore
{
    private readonly object _sync = new();
    private readonly string _root;
    private readonly string _tokenPath;
    private readonly string _programsPath;
    private readonly string _devicesPath;
    private readonly string _eventsPath;

    public CompanionStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nexus Remote PC");
        Directory.CreateDirectory(_root);
        _tokenPath = Path.Combine(_root, "token.txt");
        _programsPath = Path.Combine(_root, "programs.json");
        _devicesPath = Path.Combine(_root, "devices.json");
        _eventsPath = Path.Combine(_root, "events.json");
        Token = LoadOrCreateToken();
        RotatePairingToken();
    }

    public string RootPath => _root;
    public string Token { get; private set; }
    public string PairingToken { get; private set; } = "";
    public DateTime PairingTokenCreatedAt { get; private set; }

    public string RotatePairingToken()
    {
        lock (_sync)
        {
            PairingToken = CreateToken();
            PairingTokenCreatedAt = DateTime.UtcNow;
            return PairingToken;
        }
    }

    public bool IsPairingTokenValid(string token)
    {
        lock (_sync)
        {
            return PairingToken.Length > 0
                   && SlowEquals(PairingToken, token)
                   && DateTime.UtcNow - PairingTokenCreatedAt < TimeSpan.FromMinutes(5);
        }
    }

    public IReadOnlyList<ProgramEntry> LoadPrograms()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_programsPath)) return Array.Empty<ProgramEntry>();
                return JsonSerializer.Deserialize<List<ProgramEntry>>(
                    File.ReadAllText(_programsPath),
                    JsonDefaults.Options) ?? new List<ProgramEntry>();
            }
            catch
            {
                return Array.Empty<ProgramEntry>();
            }
        }
    }

    public void SavePrograms(IEnumerable<ProgramEntry> programs)
    {
        lock (_sync)
        {
            File.WriteAllText(
                _programsPath,
                JsonSerializer.Serialize(programs, JsonDefaults.Options));
        }
    }

    public IReadOnlyList<TrustedDevice> LoadDevices()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_devicesPath)) return Array.Empty<TrustedDevice>();
                return JsonSerializer.Deserialize<List<TrustedDevice>>(
                    File.ReadAllText(_devicesPath),
                    JsonDefaults.Options) ?? new List<TrustedDevice>();
            }
            catch
            {
                return Array.Empty<TrustedDevice>();
            }
        }
    }

    public TrustedDevice AddDevice(string name)
    {
        lock (_sync)
        {
            var devices = LoadDevices().ToList();
            var device = new TrustedDevice(
                Id: Guid.NewGuid().ToString("N"),
                Name: string.IsNullOrWhiteSpace(name) ? "Android" : name.Trim(),
                Token: CreateToken(),
                CreatedAt: DateTime.UtcNow,
                LastSeenAt: DateTime.UtcNow);
            devices.Add(device);
            SaveDevicesUnsafe(devices);
            RotatePairingToken();
            return device;
        }
    }

    public TrustedDevice? ValidateDeviceToken(string token)
    {
        lock (_sync)
        {
            var devices = LoadDevices().ToList();
            var index = devices.FindIndex(device => SlowEquals(device.Token, token));
            if (index < 0) return null;
            var current = devices[index] with { LastSeenAt = DateTime.UtcNow };
            devices[index] = current;
            SaveDevicesUnsafe(devices);
            return current;
        }
    }

    public void RemoveDevice(string id)
    {
        lock (_sync)
        {
            SaveDevicesUnsafe(LoadDevices().Where(device => device.Id != id));
        }
    }

    public void ClearDevices()
    {
        lock (_sync)
        {
            SaveDevicesUnsafe(Array.Empty<TrustedDevice>());
            RotatePairingToken();
        }
    }

    public IReadOnlyList<AppEvent> LoadEvents()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_eventsPath)) return Array.Empty<AppEvent>();
                return JsonSerializer.Deserialize<List<AppEvent>>(
                    File.ReadAllText(_eventsPath),
                    JsonDefaults.Options) ?? new List<AppEvent>();
            }
            catch
            {
                return Array.Empty<AppEvent>();
            }
        }
    }

    public void AddEvent(string level, string message)
    {
        lock (_sync)
        {
            var events = LoadEvents().ToList();
            events.Insert(0, new AppEvent(DateTime.UtcNow, level, message));
            SaveEventsUnsafe(events.Take(30));
            if (string.Equals(level, "Ошибка", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Error(message);
            }
            else if (string.Equals(level, "Предупреждение", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn(message);
            }
            else
            {
                AppLogger.Info(message);
            }
        }
    }

    public string ResetToken()
    {
        lock (_sync)
        {
            Token = CreateToken();
            File.WriteAllText(_tokenPath, Token);
            return Token;
        }
    }

    private string LoadOrCreateToken()
    {
        try
        {
            if (File.Exists(_tokenPath))
            {
                var token = File.ReadAllText(_tokenPath).Trim();
                if (token.Length >= 24) return token;
            }
        }
        catch
        {
            // Fall through and create a fresh token.
        }

        return ResetToken();
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private void SaveDevicesUnsafe(IEnumerable<TrustedDevice> devices)
    {
        File.WriteAllText(_devicesPath, JsonSerializer.Serialize(devices, JsonDefaults.Options));
    }

    private void SaveEventsUnsafe(IEnumerable<AppEvent> events)
    {
        File.WriteAllText(_eventsPath, JsonSerializer.Serialize(events, JsonDefaults.Options));
    }

    private static bool SlowEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left ?? "");
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right ?? "");
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record ProgramEntry(string Name, string Path);

public sealed record TrustedDevice(
    string Id,
    string Name,
    string Token,
    DateTime CreatedAt,
    DateTime LastSeenAt);

public sealed record AppEvent(DateTime CreatedAt, string Level, string Message);

public static class ProgramRuntime
{
    public static object ToStatus(ProgramEntry entry)
    {
        var resolvedPath = ShortcutUtil.ResolveLaunchPath(entry.Path);
        var processName = System.IO.Path.GetFileNameWithoutExtension(resolvedPath);
        var running = false;
        try
        {
            running = Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            running = false;
        }

        return new
        {
            name = string.IsNullOrWhiteSpace(entry.Name) ? processName : entry.Name,
            command = entry.Path,
            running,
            icon = IconExtractor.TryExtractBase64Png(resolvedPath)
        };
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
