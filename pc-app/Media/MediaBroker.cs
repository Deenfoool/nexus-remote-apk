namespace NexusRemotePC.Media;

public sealed class MediaBroker
{
    private readonly IReadOnlyList<IMediaProvider> _providers;
    private readonly MediaSourceResolver _resolver;

    public MediaBroker(IEnumerable<IMediaProvider> providers, MediaSourceResolver? resolver = null)
    {
        _providers = providers.ToArray();
        _resolver = resolver ?? new MediaSourceResolver();
    }

    public static MediaBroker CreateDefault()
    {
        return new MediaBroker(new IMediaProvider[]
        {
            new BrowserProvider(),
            new GsmtcProvider()
        });
    }

    public async Task<MediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var tasks = _providers.Select(provider => GetSafeProviderSourcesAsync(provider, cancellationToken)).ToArray();
        var sourceGroups = await Task.WhenAll(tasks);
        var resolved = _resolver.Resolve(sourceGroups.SelectMany(group => group));
        return new MediaSnapshot(resolved.ActiveSourceId, resolved.Sources, DateTime.UtcNow);
    }

    private static async Task<IReadOnlyList<MediaSourceSnapshot>> GetSafeProviderSourcesAsync(
        IMediaProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetSourcesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"{provider.Name}: {ex.Message}");
            return Array.Empty<MediaSourceSnapshot>();
        }
    }
}
