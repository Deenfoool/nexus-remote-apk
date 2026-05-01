using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NexusRemotePC;

public static class CommandExecutor
{
    public static string LastAction { get; private set; } = "";
    private static readonly HashSet<string> DangerousCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "power_shutdown",
        "power_shutdown_timer",
        "power_shutdown_cancel",
        "power_restart",
        "power_sleep",
        "power_lock",
        "monitor_off"
    };

    public static bool IsDangerous(string type) => DangerousCommands.Contains(type);

    public static string GetDangerousPrompt(string type)
    {
        return type switch
        {
            "power_shutdown" => "Выключить ПК сейчас?",
            "power_shutdown_timer" => "Запланировать выключение ПК по таймеру?",
            "power_shutdown_cancel" => "Отменить запланированное выключение ПК?",
            "power_restart" => "Перезагрузить ПК сейчас?",
            "power_sleep" => "Перевести ПК в сон?",
            "power_lock" => "Заблокировать ПК?",
            "monitor_off" => "Выключить экран?",
            _ => "Выполнить опасную команду?"
        };
    }

    public static CommandResult Execute(string type, JsonElement payload)
    {
        try
        {
            switch (type)
            {
                case "launcher_run":
                    Start(GetString(payload, "path"));
                    break;
                case "media_play_pause":
                    PressKey(0xB3);
                    break;
                case "media_prev":
                    PressKey(0xB1);
                    break;
                case "media_next":
                    PressKey(0xB0);
                    break;
                case "volume_mute":
                    VolumeController.ToggleMute();
                    break;
                case "volume_down":
                    VolumeController.ChangeVolume(-Math.Max(2, GetInt(payload, "steps", 1) * 2));
                    break;
                case "volume_up":
                    VolumeController.ChangeVolume(Math.Max(2, GetInt(payload, "steps", 1) * 2));
                    break;
                case "volume_set":
                    VolumeController.SetVolume(GetInt(payload, "value", 50));
                    break;
                case "brightness_set":
                    Brightness.SetBrightness(GetInt(payload, "value", 50));
                    break;
                case "keyboard_shortcut":
                    PressShortcut(GetKeys(payload));
                    break;
                case "power_shutdown":
                    Start("shutdown", "/s /t 0");
                    break;
                case "power_shutdown_timer":
                    Start("shutdown", $"/s /t {Math.Clamp(GetInt(payload, "seconds", 1800), 60, 86400)}");
                    break;
                case "power_shutdown_cancel":
                    Start("shutdown", "/a");
                    break;
                case "power_restart":
                    Start("shutdown", "/r /t 0");
                    break;
                case "power_sleep":
                    SetSuspendState(false, true, false);
                    break;
                case "power_lock":
                    LockWorkStation();
                    break;
                case "monitor_off":
                    SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
                    break;
                default:
                    return new CommandResult(false, $"Unknown command: {type}");
            }

            LastAction = DateTime.Now.ToString("HH:mm:ss");
            return new CommandResult(true, "");
        }
        catch (Exception ex)
        {
            return new CommandResult(false, ex.Message);
        }
    }

    private static void Start(string path, string arguments = "")
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Path is empty");
        Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true });
    }

    private static void RepeatKey(byte key, int steps)
    {
        for (var i = 0; i < Math.Clamp(steps, 1, 100); i++)
        {
            PressKey(key);
            Thread.Sleep(12);
        }
    }

    private static void PressShortcut(IEnumerable<string> keys)
    {
        var codes = keys.Select(ToVirtualKey).ToArray();
        foreach (var key in codes)
        {
            keybd_event((byte)key, 0, 0, UIntPtr.Zero);
            Thread.Sleep(10);
        }

        foreach (var key in codes.Reverse())
        {
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(10);
        }
    }

    private static int ToVirtualKey(string key)
    {
        return key.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "WIN" or "CMD" => 0x5B,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            var one when one.Length == 1 => one[0],
            _ => throw new InvalidOperationException($"Unsupported key: {key}")
        };
    }

    private static void PressKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static int GetInt(JsonElement payload, string name, int fallback)
    {
        return payload.ValueKind == JsonValueKind.Object
               && payload.TryGetProperty(name, out var value)
               && value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static string GetString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value)
            ? value.GetString() ?? ""
            : "";
    }

    private static IEnumerable<string> GetKeys(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("keys", out var keys))
        {
            return Array.Empty<string>();
        }

        return keys.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0);
    }

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const int HWND_BROADCAST = 0xffff;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(int hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}

public sealed record CommandResult(bool Ok, string Error);
