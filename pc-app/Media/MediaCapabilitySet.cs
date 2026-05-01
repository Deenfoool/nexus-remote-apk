namespace NexusRemotePC.Media;

public sealed record MediaCapabilitySet(
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek
);
