namespace VRCVideoCacher.Utils;

public readonly record struct CacheStatsResult(
    int TotalPlays,
    int CacheHits,
    double? HitRate,
    long BytesSaved,
    int UncountedHits);

// The arithmetic behind the Stats view, kept pure so the accounting rules can be tested
// without a database or a cache directory.
public static class CacheStats
{
    // watchCounts: videoId -> number of times it was served FROM cache. VideoWatchStats is
    // only incremented on a cache hit, while every resolved request lands in PlayHistory —
    // so totalPlays is the denominator and the sum of watchCounts is the numerator.
    //
    // sizesByVideoId: size on disk for videos still cached. A hit on a since-evicted video
    // really did save bandwidth but has no size to attribute, so those hits are reported
    // separately as UncountedHits and BytesSaved is a floor, not an exact total.
    public static CacheStatsResult Compute(
        IReadOnlyDictionary<string, int> watchCounts,
        IReadOnlyDictionary<string, long> sizesByVideoId,
        int totalPlays)
    {
        var cacheHits = 0;
        long bytesSaved = 0;
        var uncountedHits = 0;

        foreach (var (videoId, count) in watchCounts)
        {
            cacheHits += count;
            if (sizesByVideoId.TryGetValue(videoId, out var size))
                bytesSaved += size * count;
            else
                uncountedHits += count;
        }

        return new CacheStatsResult(
            totalPlays,
            cacheHits,
            totalPlays > 0 ? (double)cacheHits / totalPlays : null,
            bytesSaved,
            uncountedHits);
    }

    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0) return "0 B";
        var mag = Math.Min((int)Math.Log(bytes, 1024), suffixes.Length - 1);
        return $"{bytes / Math.Pow(1024, mag):N2} {suffixes[mag]}";
    }
}
