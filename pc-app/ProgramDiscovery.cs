using System.IO;

namespace NexusRemotePC;

public static class ProgramDiscovery
{
    private static readonly string[] SearchRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
    ];

    public static IReadOnlyList<ProgramEntry> DiscoverSuggestedPrograms(IEnumerable<ProgramEntry> existing)
    {
        var existingPaths = new HashSet<string>(
            existing.Select(entry => entry.Path),
            StringComparer.OrdinalIgnoreCase);

        var suggestions = new List<ProgramEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in SearchRoots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                                        || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                if (existingPaths.Contains(file) || !seen.Add(file))
                {
                    continue;
                }

                var displayName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(displayName) || IsNoise(displayName))
                {
                    continue;
                }

                suggestions.Add(new ProgramEntry(displayName.Trim(), file));
            }
        }

        return suggestions
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(250)
            .ToArray();
    }

    private static bool IsNoise(string name)
    {
        return name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || name.Contains("удал", StringComparison.OrdinalIgnoreCase)
               || name.Contains("readme", StringComparison.OrdinalIgnoreCase)
               || name.Contains("help", StringComparison.OrdinalIgnoreCase)
               || name.Contains("license", StringComparison.OrdinalIgnoreCase);
    }
}
