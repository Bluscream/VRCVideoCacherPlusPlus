using Serilog;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Utils;

// Queues the user's PreCacheVideos list through the normal resolve-and-download path,
// so they are already on disk the first time a world plays them.
//
// Distinct from BulkPreCache, which mirrors JSON manifests of direct file URLs.
public static class VideoPreCache
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(VideoPreCache));

    // A single entry may hold several URLs pasted together — comma, whitespace or
    // newline separated — so every stored entry is expanded before use. Applied both
    // here and when Settings saves, so a hand-edited config behaves like the UI.
    private static readonly char[] UrlSeparators = [',', ';', ' ', '\t', '\n', '\r'];

    public static string[] SplitUrls(IEnumerable<string> entries) =>
        entries
            .SelectMany(entry => entry.Split(
                UrlSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

    public static async Task QueueConfiguredVideos()
    {
        var urls = SplitUrls(ConfigManager.Config.PreCacheVideos);
        if (urls.Length == 0)
            return;

        var cachedIds = CacheManager.GetCachedAssets().Keys
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var queued = 0;
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            try
            {
                // avPro: true matches what the "save to cache" action in the UI requests.
                var videoInfo = await VideoId.GetVideoId(url.Trim(), true);
                if (videoInfo == null || string.IsNullOrEmpty(videoInfo.VideoId))
                {
                    Log.Warning("Pre-cache: couldn't resolve {Url}", url);
                    continue;
                }

                if (cachedIds.Contains(videoInfo.VideoId))
                    continue;

                // QueueDownload de-duplicates against anything already pending.
                VideoDownloader.QueueDownload(videoInfo);
                queued++;
            }
            catch (Exception ex)
            {
                // One bad URL must not stop the rest of the list.
                Log.Warning("Pre-cache: failed to queue {Url}: {Error}", url, ex.Message);
            }
        }

        if (queued > 0)
            Log.Information("Pre-cache: queued {Count} video(s) for download.", queued);
    }
}
