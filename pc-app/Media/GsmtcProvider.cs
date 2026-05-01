using System.Security.Cryptography;
using System.Text;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NexusRemotePC.Media;

public sealed class GsmtcProvider : IMediaProvider
{
    public string Name => "GsmtcProvider";

    public async Task<IReadOnlyList<MediaSourceSnapshot>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var current = manager.GetCurrentSession();
        var sessions = manager.GetSessions();
        var snapshots = new List<MediaSourceSnapshot>(sessions.Count);

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await CreateSnapshotAsync(session, current);
            if (snapshot != null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private static async Task<MediaSourceSnapshot?> CreateSnapshotAsync(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSession? current)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            var controls = playback?.Controls;
            var timeline = session.GetTimelineProperties();
            var media = await session.TryGetMediaPropertiesAsync();
            var appId = NormalizeAppId(session.SourceAppUserModelId);
            var appName = PrettyAppName(appId);
            var title = Safe(media?.Title);
            var artist = Safe(media?.Artist);
            var album = Safe(media?.AlbumTitle);
            var artworkBase64 = await TryReadArtworkBase64Async(media?.Thumbnail);
            var positionMs = timeline.Position > TimeSpan.Zero ? (long?)timeline.Position.TotalMilliseconds : null;
            var durationMs = timeline.EndTime > timeline.StartTime
                ? (long?)(timeline.EndTime - timeline.StartTime).TotalMilliseconds
                : null;
            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var sourceId = BuildSourceId(appId, title, artist);

            return new MediaSourceSnapshot(
                SourceId: sourceId,
                SourceType: "gsmtc",
                AppId: appId,
                AppName: appName,
                Site: null,
                TabTitle: title,
                MediaKind: "unknown",
                Title: title,
                Artist: artist,
                Album: album,
                ArtworkUrl: null,
                ArtworkBase64: artworkBase64,
                PositionMs: positionMs,
                DurationMs: durationMs,
                IsPlaying: isPlaying,
                IsMuted: false,
                Capabilities: new MediaCapabilitySet(
                    CanPlay: controls?.IsPlayEnabled ?? false,
                    CanPause: controls?.IsPauseEnabled ?? false,
                    CanTogglePlayPause: (controls?.IsPlayEnabled ?? false) || (controls?.IsPauseEnabled ?? false),
                    CanNext: controls?.IsNextEnabled ?? false,
                    CanPrevious: controls?.IsPreviousEnabled ?? false,
                    CanSeek: controls?.IsPlaybackPositionEnabled ?? false
                ),
                Priority: ReferenceEquals(session, current) ? 200 : 100,
                LastUpdatedUtc: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GsmtcProvider session skipped: {ex.Message}");
            return null;
        }
    }

    internal static string BuildSourceId(string appId, string? title, string? artist)
    {
        var payload = $"{appId}|{title}|{artist}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"gsmtc:{appId}:{hash[..12]}";
    }

    internal static string NormalizeAppId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var cleaned = raw.Trim().ToLowerInvariant();
        if (cleaned.Contains("yandex")) return "yandex";
        if (cleaned.Contains("chrome")) return "chrome";
        if (cleaned.Contains("msedge")) return "edge";
        if (cleaned.Contains("spotify")) return "spotify";
        if (cleaned.Contains("vlc")) return "vlc";
        if (cleaned.Contains("firefox")) return "firefox";
        return cleaned.Replace("!", ".").Split('.').LastOrDefault() ?? cleaned;
    }

    private static string PrettyAppName(string appId)
    {
        return appId switch
        {
            "yandex" => "Yandex Browser",
            "chrome" => "Google Chrome",
            "edge" => "Microsoft Edge",
            "spotify" => "Spotify",
            "vlc" => "VLC",
            "firefox" => "Firefox",
            "unknown" => "Unknown source",
            _ => char.ToUpperInvariant(appId[0]) + appId[1..]
        };
    }

    private static string? Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<string?> TryReadArtworkBase64Async(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null)
        {
            return null;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 512 * 1024)
            {
                return null;
            }

            var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
            await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            var bytes = new byte[buffer.Length];
            DataReader.FromBuffer(buffer).ReadBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }
}
