using System.Globalization;
using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// These numbers are the tool's own justification — "you saved this much bandwidth". If the
// accounting silently inflates, the Stats view is worse than not existing. The rules being
// pinned here: watch counts are cache hits only, evicted videos contribute hits but no
// bytes, and the shortfall is reported rather than hidden.
public class CacheStatsTests
{
    [Fact]
    public void HitRateIsHitsOverTotalPlays()
    {
        var result = CacheStats.Compute(
            watchCounts: new Dictionary<string, int> { ["a"] = 3 },
            sizesByVideoId: new Dictionary<string, long> { ["a"] = 100 },
            totalPlays: 4);

        Assert.Equal(3, result.CacheHits);
        Assert.Equal(4, result.TotalPlays);
        Assert.Equal(0.75, result.HitRate);
    }

    [Fact]
    public void HitRateIsNullWithNoPlays()
    {
        // A fresh install must show "-", not a divide-by-zero or a fake 0%/100%.
        var result = CacheStats.Compute(
            new Dictionary<string, int>(),
            new Dictionary<string, long>(),
            totalPlays: 0);

        Assert.Null(result.HitRate);
        Assert.Equal(0, result.CacheHits);
    }

    [Fact]
    public void BytesSavedCountsEveryHitNotJustTheFirst()
    {
        // Each cache hit avoided one download of the whole file, so a video watched 3 times
        // saved 3x its size — not 2x (watches minus the original download), because the
        // watch count only ever increments on a hit.
        var result = CacheStats.Compute(
            new Dictionary<string, int> { ["a"] = 3 },
            new Dictionary<string, long> { ["a"] = 100 },
            totalPlays: 3);

        Assert.Equal(300, result.BytesSaved);
        Assert.Equal(0, result.UncountedHits);
    }

    [Fact]
    public void EvictedVideosContributeHitsButNoBytes()
    {
        // "b" was watched from cache twice, then evicted. Those hits were real savings but
        // we no longer know the size — they must surface as UncountedHits so the UI can say
        // the figure is a floor, instead of quietly under-reporting.
        var result = CacheStats.Compute(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
            new Dictionary<string, long> { ["a"] = 500 },
            totalPlays: 3);

        Assert.Equal(3, result.CacheHits);
        Assert.Equal(500, result.BytesSaved);
        Assert.Equal(2, result.UncountedHits);
    }

    [Fact]
    public void NoCaveatWhenEverythingIsStillCached()
    {
        var result = CacheStats.Compute(
            new Dictionary<string, int> { ["a"] = 2 },
            new Dictionary<string, long> { ["a"] = 10 },
            totalPlays: 2);

        Assert.Equal(0, result.UncountedHits);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1073741824, "1.00 GB")]
    public void FormatSizeScalesToUnit(long bytes, string expected)
    {
        // Pinned to invariant culture: the expected strings use a dot decimal separator,
        // which would otherwise fail on a machine set to a comma-decimal locale.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, CacheStats.FormatSize(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
