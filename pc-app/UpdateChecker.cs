using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NexusRemotePC;

public static class UpdateChecker
{
    private static readonly HttpClient Client = CreateClient();
    private const string LatestReleaseUrl = "https://api.github.com/repos/Deenfoool/nexus-remote-pc/releases/latest";

    public static async Task<UpdateCheckResult> CheckPcAppAsync(Version currentVersion)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                false,
                "",
                "",
                "",
                "",
                "Публичный релиз ещё не опубликован. Создайте GitHub Release для включения update-check.");
        }
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
        var publishedAt = root.TryGetProperty("published_at", out var publishedElement) ? publishedElement.GetString() ?? "" : "";
        var title = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";

        var remoteVersion = ParseVersion(tag);
        var hasUpdate = remoteVersion != null && remoteVersion > currentVersion;
        var description = hasUpdate
            ? $"Доступна версия {remoteVersion}."
            : remoteVersion != null
                ? $"Установлена актуальная версия {currentVersion}."
                : $"Не удалось сравнить версию. Последний релиз: {tag}.";

        return new UpdateCheckResult(
            hasUpdate,
            tag,
            title,
            publishedAt,
            htmlUrl,
            description);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NexusRemotePC", "1.0"));
        return client;
    }

    private static Version? ParseVersion(string raw)
    {
        var cleaned = raw.Trim().TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var version) ? version : null;
    }
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string Tag,
    string Title,
    string PublishedAt,
    string Url,
    string Summary);
