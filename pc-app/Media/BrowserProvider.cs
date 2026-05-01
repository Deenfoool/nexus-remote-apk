namespace NexusRemotePC.Media;

public sealed class BrowserProvider : IMediaProvider
{
    private readonly BrowserSessionRegistry _registry;

    public BrowserProvider()
        : this(BrowserSessionRegistry.Shared)
    {
    }

    public BrowserProvider(BrowserSessionRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "BrowserProvider";

    public Task<IReadOnlyList<MediaSourceSnapshot>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MediaSourceSnapshot>>(_registry.GetSnapshots());
    }
}
