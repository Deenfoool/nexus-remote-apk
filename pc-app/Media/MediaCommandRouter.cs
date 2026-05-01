using System.Text.Json;
using Windows.Media.Control;

namespace NexusRemotePC.Media;

public sealed class MediaCommandRouter
{
    public static MediaCommandRouter Shared { get; } = new();

    private MediaCommandRouter()
    {
    }

    public bool TryHandle(string type)
    {
        return type is "media_play_pause"
            or "media_prev"
            or "media_next"
            or "media_seek_relative"
            or "media_seek_to"
            or "media_fullscreen"
            or "media_subtitles"
            or "media_stop";
    }

    public async Task<bool> ExecuteAsync(string type, JsonElement payload, CancellationToken cancellationToken)
    {
        var sourceId = GetString(payload, "sourceId");

        if (IsBrowserSource(sourceId) && BrowserBridgeServer.Shared.HasConnectedClients)
        {
            var sent = await BrowserBridgeServer.Shared.SendCommandAsync(type, sourceId, CreateBrowserPayload(payload), cancellationToken);
            if (sent)
            {
                return true;
            }
        }

        return await TryExecuteGsmtcAsync(type, sourceId, payload);
    }

    private static bool IsBrowserSource(string? sourceId)
    {
        return !string.IsNullOrWhiteSpace(sourceId)
               && sourceId.StartsWith("browser:", StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateBrowserPayload(JsonElement payload)
    {
        return new
        {
            seconds = GetInt(payload, "seconds", 0),
            positionMs = GetLong(payload, "positionMs"),
            enabled = GetBool(payload, "enabled")
        };
    }

    private static async Task<bool> TryExecuteGsmtcAsync(string type, string? sourceId, JsonElement payload)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = await ResolveSessionAsync(manager, sourceId);
            if (session == null)
            {
                return false;
            }

            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var controls = playback?.Controls;

            switch (type)
            {
                case "media_play_pause":
                    if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        return await session.TryPauseAsync();
                    }

                    if (controls?.IsPlayEnabled == true)
                    {
                        return await session.TryPlayAsync();
                    }

                    return await session.TryTogglePlayPauseAsync();

                case "media_prev":
                    return controls?.IsPreviousEnabled == true && await session.TrySkipPreviousAsync();

                case "media_next":
                    return controls?.IsNextEnabled == true && await session.TrySkipNextAsync();

                case "media_seek_relative":
                    if (controls?.IsPlaybackPositionEnabled != true)
                    {
                        return false;
                    }

                    var deltaSeconds = GetInt(payload, "seconds", 0);
                    var target = timeline.Position + TimeSpan.FromSeconds(deltaSeconds);
                    if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                    if (timeline.EndTime > TimeSpan.Zero && target > timeline.EndTime) target = timeline.EndTime;
                    return await session.TryChangePlaybackPositionAsync((long)target.TotalMilliseconds);

                case "media_seek_to":
                    if (controls?.IsPlaybackPositionEnabled != true)
                    {
                        return false;
                    }

                    var positionMs = GetLong(payload, "positionMs");
                    if (positionMs == null)
                    {
                        return false;
                    }

                    return await session.TryChangePlaybackPositionAsync(positionMs.Value);

                case "media_stop":
                    return controls?.IsStopEnabled == true && await session.TryStopAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GSMTC control failed: {ex.Message}");
        }

        return false;
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> ResolveSessionAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return manager.GetCurrentSession();
        }

        foreach (var session in manager.GetSessions())
        {
            try
            {
                var media = await session.TryGetMediaPropertiesAsync();
                var appId = GsmtcProvider.NormalizeAppId(session.SourceAppUserModelId);
                var candidateId = GsmtcProvider.BuildSourceId(appId, Safe(media?.Title), Safe(media?.Artist));
                if (candidateId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }
            catch
            {
                // Keep looking.
            }
        }

        return manager.GetCurrentSession();
    }

    private static string? Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
    }

    private static int GetInt(JsonElement payload, string name, int fallback)
    {
        return payload.ValueKind == JsonValueKind.Object
               && payload.TryGetProperty(name, out var value)
               && value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static long? GetLong(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static bool GetBool(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
               && payload.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.True;
    }
}
