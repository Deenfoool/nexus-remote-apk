using System.IO;

namespace NexusRemotePC;

public static class ShortcutUtil
{
    public static string ResolveLaunchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return path;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            var target = (shortcut.TargetPath as string) ?? "";
            return File.Exists(target) ? target : path;
        }
        catch
        {
            return path;
        }
    }
}
