namespace NexusRemotePC.Media;

public interface IMediaProvider
{
    string Name { get; }
    Task<IReadOnlyList<MediaSourceSnapshot>> GetSourcesAsync(CancellationToken cancellationToken);
}
