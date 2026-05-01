using System.Text;
using System.IO;

namespace NexusRemotePC;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nexus Remote PC",
        "logs");
    private static readonly string LogPath = Path.Combine(Root, "nexus-remote.log");
    private static readonly string ArchivePath = Path.Combine(Root, "nexus-remote.previous.log");

    public static string DirectoryPath => Root;
    public static string CurrentLogPath => LogPath;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var suffix = exception == null ? "" : $"{Environment.NewLine}{exception}";
        Write("ERROR", $"{message}{suffix}");
    }

    public static string ReadTail(int maxLines = 60)
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(LogPath)) return "Лог-файл пока пуст.";
                var lines = File.ReadAllLines(LogPath, Encoding.UTF8);
                return string.Join(Environment.NewLine, lines.TakeLast(Math.Max(1, maxLines)));
            }
            catch (Exception ex)
            {
                return $"Не удалось прочитать лог: {ex.Message}";
            }
        }
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Root);
                RotateIfNeeded();
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging should never crash the app.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;

        var info = new FileInfo(LogPath);
        if (info.Length < 1024 * 1024) return;

        if (File.Exists(ArchivePath))
        {
            File.Delete(ArchivePath);
        }

        File.Move(LogPath, ArchivePath);
    }
}
