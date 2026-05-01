using System.Text.Json;

namespace NexusRemotePC.Media;

public sealed record BrowserSessionUpdate(
    string SourceId,
    string BrowserId,
    string BrowserName,
    string? Site,
    string? PageUrl,
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
    bool IsActiveTab,
    bool CanTogglePlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek,
    DateTime LastUpdatedUtc)
{
    public static BrowserSessionUpdate? FromJson(JsonElement node)
    {
        var sourceId = node.TryGetProperty("sourceId", out var sourceIdNode) ? sourceIdNode.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        var browserId = node.TryGetProperty("browserId", out var browserIdNode) ? browserIdNode.GetString() ?? "browser" : "browser";
        var browserName = node.TryGetProperty("browserName", out var browserNameNode) ? browserNameNode.GetString() ?? "Browser" : "Browser";
        var site = GetOptionalString(node, "site");
        var pageUrl = GetOptionalString(node, "pageUrl");
        var tabTitle = GetOptionalString(node, "tabTitle");
        var mediaKind = GetOptionalString(node, "mediaKind") ?? "unknown";
        var title = GetOptionalString(node, "title");
        var artist = GetOptionalString(node, "artist");
        var album = GetOptionalString(node, "album");
        var artworkUrl = GetOptionalString(node, "artworkUrl");
        var artworkBase64 = GetOptionalString(node, "artworkBase64");
        var positionMs = GetOptionalLong(node, "positionMs");
        var durationMs = GetOptionalLong(node, "durationMs");
        var isPlaying = GetOptionalBool(node, "isPlaying");
        var isMuted = GetOptionalBool(node, "isMuted");
        var isActiveTab = GetOptionalBool(node, "isActiveTab");
        var canTogglePlayPause = GetOptionalBool(node, "canTogglePlayPause", true);
        var canNext = GetOptionalBool(node, "canNext");
        var canPrevious = GetOptionalBool(node, "canPrevious");
        var canSeek = GetOptionalBool(node, "canSeek");
        var lastUpdatedUtc = node.TryGetProperty("lastUpdatedUtc", out var updatedNode)
                             && updatedNode.ValueKind == JsonValueKind.String
                             && DateTime.TryParse(updatedNode.GetString(), out var parsedUpdated)
            ? parsedUpdated.ToUniversalTime()
            : DateTime.UtcNow;

        return new BrowserSessionUpdate(
            SourceId: sourceId,
            BrowserId: browserId,
            BrowserName: browserName,
            Site: site,
            PageUrl: pageUrl,
            TabTitle: tabTitle,
            MediaKind: mediaKind,
            Title: title,
            Artist: artist,
            Album: album,
            ArtworkUrl: artworkUrl,
            ArtworkBase64: artworkBase64,
            PositionMs: positionMs,
            DurationMs: durationMs,
            IsPlaying: isPlaying,
            IsMuted: isMuted,
            IsActiveTab: isActiveTab,
            CanTogglePlayPause: canTogglePlayPause,
            CanNext: canNext,
            CanPrevious: canPrevious,
            CanSeek: canSeek,
            LastUpdatedUtc: lastUpdatedUtc);
    }

    private static string? GetOptionalString(JsonElement node, string propertyName)
    {
        return node.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? GetOptionalLong(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out var fromString))
        {
            return fromString;
        }

        return null;
    }

    private static bool GetOptionalBool(JsonElement node, string propertyName, bool fallback = false)
    {
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind == JsonValueKind.True
            || (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed) && parsed);
    }
}
