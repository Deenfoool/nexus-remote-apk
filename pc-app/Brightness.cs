using System.Diagnostics;

namespace NexusRemotePC;

public static class Brightness
{
    public static int? TryGetBrightness()
    {
        try
        {
            var output = RunPowerShell(
                "(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty CurrentBrightness)");
            return int.TryParse(output.Trim(), out var value) ? Math.Clamp(value, 0, 100) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SetBrightness(int value)
    {
        value = Math.Clamp(value, 0, 100);
        RunPowerShell(
            "$b=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction SilentlyContinue; " +
            "if ($null -eq $b) { throw 'Brightness API is not available' }; " +
            $"$b.WmiSetBrightness(1,{value}) | Out-Null");
    }

    private static string RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(command),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell is not available");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(3000);
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
