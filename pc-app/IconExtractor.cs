using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace NexusRemotePC;

public static class IconExtractor
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string TryExtractBase64Png(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (Cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            if (!File.Exists(path)) return "";
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return "";
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            cached = Convert.ToBase64String(stream.ToArray());
            Cache[path] = cached;
            return cached;
        }
        catch
        {
            return "";
        }
    }
}
