using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NexusRemotePC;

public static class ContextCollector
{
    public static object CreateContext()
    {
        var title = "";
        var processName = "";

        try
        {
            var hwnd = GetForegroundWindow();
            var length = GetWindowTextLength(hwnd);
            var builder = new StringBuilder(length + 1);
            GetWindowText(hwnd, builder, builder.Capacity);
            title = builder.ToString();

            GetWindowThreadProcessId(hwnd, out var pid);
            processName = Process.GetProcessById((int)pid).ProcessName + ".exe";
        }
        catch
        {
            // Keep empty context.
        }

        var section = SuggestSection(processName);
        return new
        {
            active_title = title,
            active_process = processName,
            suggested_section = section,
            suggested_label = string.IsNullOrWhiteSpace(section) ? "" : section
        };
    }

    private static string SuggestSection(string process)
    {
        var lower = process.ToLowerInvariant();
        if (lower.Contains("chrome") || lower.Contains("browser") || lower.Contains("firefox") || lower.Contains("msedge") || lower.Contains("yandex"))
        {
            return "Browser";
        }
        if (lower.Contains("vlc") || lower.Contains("mpc") || lower.Contains("player") || lower.Contains("spotify"))
        {
            return "Player";
        }
        if (!string.IsNullOrWhiteSpace(lower))
        {
            return "Apps";
        }
        return "";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
