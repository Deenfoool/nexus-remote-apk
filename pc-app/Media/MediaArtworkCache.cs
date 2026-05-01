using System.Collections.Concurrent;
using System.Net.Http;

namespace NexusRemotePC.Media;

public sealed class MediaArtworkCache
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> GetBase64Async(string? artworkUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
        {
            return null;
        }

        if (artworkUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var comma = artworkUrl.IndexOf(',');
            return comma >= 0 ? artworkUrl[(comma + 1)..] : null;
        }

        if (_cache.TryGetValue(artworkUrl, out var cached))
        {
            return cached;
        }

        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        try
        {
            using var response = await HttpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > 700 * 1024)
            {
                return null;
            }

            var base64 = Convert.ToBase64String(bytes);
            _cache[artworkUrl] = base64;
            return base64;
        }
        catch
        {
            _cache[artworkUrl] = null;
            return null;
        }
    }
}
