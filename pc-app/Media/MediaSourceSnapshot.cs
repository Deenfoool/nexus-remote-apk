namespace NexusRemotePC.Media;

public sealed record MediaSourceSnapshot(
    string SourceId,
    string SourceType,
    string AppId,
    string AppName,
    string? Site,
    string? TabTitle,
    string MediaKind,
    string? Title,
    string? Artist,
    string? Album,
    string? ArtworkUrl,
    string? ArtworkBase64,
    long? PositionMs,
    long? DurationMs,
    bool IsPlaying,
    bool IsMuted,
    MediaCapabilitySet Capabilities,
    int Priority,
    DateTime LastUpdatedUtc
);
