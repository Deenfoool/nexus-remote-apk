namespace NexusRemotePC.Media;

public sealed record MediaSnapshot(
    string? ActiveSourceId,
    IReadOnlyList<MediaSourceSnapshot> Sources,
    DateTime UpdatedAtUtc
);
