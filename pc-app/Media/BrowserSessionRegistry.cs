using System.Collections.Concurrent;

namespace NexusRemotePC.Media;

public sealed class BrowserSessionRegistry
{
    public static BrowserSessionRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<string, BrowserSessionState> _sessions = new(StringComparer.Ordinal);
    private readonly MediaArtworkCache _artworkCache = new();

    public async Task UpsertAsync(BrowserSessionUpdate update, CancellationToken cancellationToken)
    {
        var artworkBase64 = update.ArtworkBase64;
        if (string.IsNullOrWhiteSpace(artworkBase64) && !string.IsNullOrWhiteSpace(update.ArtworkUrl))
        {
            artworkBase64 = await _artworkCache.GetBase64Async(update.ArtworkUrl, cancellationToken);
        }

        var snapshot = new MediaSourceSnapshot(
            SourceId: update.SourceId,
            SourceType: "browser",
            AppId: update.BrowserId,
            AppName: update.BrowserName,
            Site: update.Site,
            TabTitle: update.TabTitle,
            MediaKind: update.MediaKind,
            Title: update.Title,
            Artist: update.Artist,
            Album: update.Album,
            ArtworkUrl: update.ArtworkUrl,
            ArtworkBase64: artworkBase64,
            PositionMs: update.PositionMs,
            DurationMs: update.DurationMs,
            IsPlaying: update.IsPlaying,
            IsMuted: update.IsMuted,
            Capabilities: new MediaCapabilitySet(
                CanPlay: !update.IsPlaying,
                CanPause: update.IsPlaying,
                CanTogglePlayPause: update.CanTogglePlayPause,
                CanNext: update.CanNext,
                CanPrevious: update.CanPrevious,
                CanSeek: update.CanSeek),
            Priority: update.IsActiveTab ? 320 : 300,
            LastUpdatedUtc: update.LastUpdatedUtc);

        _sessions[update.SourceId] = new BrowserSessionState(snapshot, update.PageUrl);
    }

    public void Remove(string sourceId)
    {
        _sessions.TryRemove(sourceId, out _);
    }

    public IReadOnlyList<MediaSourceSnapshot> GetSnapshots()
    {
        var staleBefore = DateTime.UtcNow.AddMinutes(-2);
        foreach (var entry in _sessions)
        {
            if (entry.Value.Snapshot.LastUpdatedUtc < staleBefore)
            {
                _sessions.TryRemove(entry.Key, out _);
            }
        }

        return _sessions.Values
            .Select(state => state.Snapshot)
            .OrderByDescending(snapshot => snapshot.Priority)
            .ThenByDescending(snapshot => snapshot.LastUpdatedUtc)
            .ToArray();
    }
}

public sealed record BrowserSessionState(MediaSourceSnapshot Snapshot, string? PageUrl);
