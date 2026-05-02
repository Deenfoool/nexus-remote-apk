using System.Text.RegularExpressions;

namespace NexusRemotePC.Media;

public sealed class MediaSourceResolver
{
    private static readonly string[] BrowserAppIds = ["yandex", "chrome", "edge", "chromium", "firefox"];

    public ResolvedMediaState Resolve(IEnumerable<MediaSourceSnapshot> rawSources)
    {
        var sources = rawSources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceId))
            .ToArray();

        var browserSources = sources.Where(source => source.SourceType == "browser").ToList();
        var gsmtcSources = sources.Where(source => source.SourceType == "gsmtc").ToList();
        var usedGsmtc = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<MediaSourceSnapshot>(sources.Length);

        foreach (var browser in browserSources)
        {
            var match = gsmtcSources
                .Where(candidate => !usedGsmtc.Contains(candidate.SourceId))
                .Where(candidate => IsBrowserApp(candidate.AppId))
                .Select(candidate => new { Candidate = candidate, Score = ScoreMatch(browser, candidate) })
                .Where(item => item.Score >= 70)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Candidate.IsPlaying)
                .ThenByDescending(item => item.Candidate.LastUpdatedUtc)
                .FirstOrDefault();

            if (match != null)
            {
                usedGsmtc.Add(match.Candidate.SourceId);
                resolved.Add(Merge(browser, match.Candidate));
            }
            else
            {
                resolved.Add(BoostBrowser(browser));
            }
        }

        foreach (var gsmtc in gsmtcSources.Where(source => !usedGsmtc.Contains(source.SourceId)))
        {
            resolved.Add(gsmtc);
        }

        foreach (var source in sources.Where(source => source.SourceType is not ("browser" or "gsmtc")))
        {
            resolved.Add(source);
        }

        var ordered = resolved
            .OrderByDescending(source => source.IsPlaying)
            .ThenByDescending(source => source.Priority)
            .ThenByDescending(source => source.LastUpdatedUtc)
            .ThenBy(source => source.SourceId, StringComparer.Ordinal)
            .ToArray();

        var active = ordered.FirstOrDefault(source => source.IsPlaying)
                     ?? ordered.FirstOrDefault();

        return new ResolvedMediaState(active?.SourceId, ordered);
    }

    private static MediaSourceSnapshot BoostBrowser(MediaSourceSnapshot browser)
    {
        return browser with
        {
            Priority = browser.Priority + (browser.IsPlaying ? 20 : 10)
        };
    }

    private static MediaSourceSnapshot Merge(MediaSourceSnapshot browser, MediaSourceSnapshot gsmtc)
    {
        var isPlaying = browser.IsPlaying || gsmtc.IsPlaying;
        var browserTitle = Pick(browser.Title, gsmtc.Title);
        var browserArtist = Pick(browser.Artist, gsmtc.Artist);
        var browserAlbum = Pick(browser.Album, gsmtc.Album);
        var browserArtworkUrl = Pick(browser.ArtworkUrl, gsmtc.ArtworkUrl);
        var browserArtworkBase64 = Pick(browser.ArtworkBase64, gsmtc.ArtworkBase64);
        var mediaKind = browser.MediaKind != "unknown" ? browser.MediaKind : gsmtc.MediaKind;

        return new MediaSourceSnapshot(
            SourceId: browser.SourceId,
            SourceType: "browser",
            AppId: browser.AppId,
            AppName: Pick(browser.AppName, gsmtc.AppName) ?? browser.AppName,
            Site: Pick(browser.Site, gsmtc.Site),
            TabTitle: Pick(browser.TabTitle, gsmtc.TabTitle),
            MediaKind: mediaKind,
            Title: browserTitle,
            Artist: browserArtist,
            Album: browserAlbum,
            ArtworkUrl: browserArtworkUrl,
            ArtworkBase64: browserArtworkBase64,
            PositionMs: Pick(browser.PositionMs, gsmtc.PositionMs),
            DurationMs: Pick(browser.DurationMs, gsmtc.DurationMs),
            IsPlaying: isPlaying,
            IsMuted: browser.IsMuted,
            Capabilities: new MediaCapabilitySet(
                CanPlay: browser.Capabilities.CanPlay || gsmtc.Capabilities.CanPlay,
                CanPause: browser.Capabilities.CanPause || gsmtc.Capabilities.CanPause,
                CanTogglePlayPause: browser.Capabilities.CanTogglePlayPause || gsmtc.Capabilities.CanTogglePlayPause,
                CanNext: browser.Capabilities.CanNext || gsmtc.Capabilities.CanNext,
                CanPrevious: browser.Capabilities.CanPrevious || gsmtc.Capabilities.CanPrevious,
                CanSeek: browser.Capabilities.CanSeek || gsmtc.Capabilities.CanSeek),
            Priority: Math.Max(browser.Priority, gsmtc.Priority) + 40,
            LastUpdatedUtc: browser.LastUpdatedUtc >= gsmtc.LastUpdatedUtc ? browser.LastUpdatedUtc : gsmtc.LastUpdatedUtc);
    }

    private static int ScoreMatch(MediaSourceSnapshot browser, MediaSourceSnapshot gsmtc)
    {
        var score = 0;

        if (browser.AppId.Equals(gsmtc.AppId, StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        var browserTitle = Normalize(browser.Title ?? browser.TabTitle);
        var gsmtcTitle = Normalize(gsmtc.Title ?? gsmtc.TabTitle);
        if (!string.IsNullOrWhiteSpace(browserTitle) && !string.IsNullOrWhiteSpace(gsmtcTitle))
        {
            if (browserTitle == gsmtcTitle)
            {
                score += 45;
            }
            else if (browserTitle.Contains(gsmtcTitle, StringComparison.OrdinalIgnoreCase)
                     || gsmtcTitle.Contains(browserTitle, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }
        }

        var browserArtist = Normalize(browser.Artist);
        var gsmtcArtist = Normalize(gsmtc.Artist);
        if (!string.IsNullOrWhiteSpace(browserArtist) && !string.IsNullOrWhiteSpace(gsmtcArtist))
        {
            if (browserArtist == gsmtcArtist)
            {
                score += 25;
            }
            else if (browserArtist.Contains(gsmtcArtist, StringComparison.OrdinalIgnoreCase)
                     || gsmtcArtist.Contains(browserArtist, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }
        }

        if (browser.IsPlaying == gsmtc.IsPlaying)
        {
            score += 10;
        }

        var ageDelta = Math.Abs((browser.LastUpdatedUtc - gsmtc.LastUpdatedUtc).TotalSeconds);
        if (ageDelta <= 5)
        {
            score += 10;
        }

        return score;
    }

    private static bool IsBrowserApp(string appId)
    {
        return BrowserAppIds.Contains(appId, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value
            .Replace("youtube", "", StringComparison.OrdinalIgnoreCase)
            .Replace("яндекс браузер", "", StringComparison.OrdinalIgnoreCase)
            .Replace("yandex browser", "", StringComparison.OrdinalIgnoreCase)
            .Replace("vk видео", "", StringComparison.OrdinalIgnoreCase)
            .Replace("vk music", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        normalized = Regex.Replace(normalized, @"[\s\-–—|:]+", " ");
        return normalized.Trim().ToLowerInvariant();
    }

    private static string? Pick(string? primary, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(primary) ? primary : fallback;
    }

    private static long? Pick(long? primary, long? fallback)
    {
        return primary is > 0 ? primary : fallback;
    }
}

public sealed record ResolvedMediaState(
    string? ActiveSourceId,
    IReadOnlyList<MediaSourceSnapshot> Sources
);
