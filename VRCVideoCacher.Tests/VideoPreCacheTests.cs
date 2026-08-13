using VRCVideoCacher.Utils;
using Xunit;

namespace VRCVideoCacher.Tests;

// A pre-cache row is a paste target: users drop in a whole list at once. Splitting it
// is what turns that paste into individually resolvable URLs — if it stops splitting,
// the entire pasted blob is handed to yt-dlp as one bogus URL and nothing pre-caches.
public class VideoPreCacheSplitTests
{
    [Theory]
    [InlineData("a,b")]
    [InlineData("a b")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a;b")]
    [InlineData("a , b")]
    [InlineData("  a  ,,  b  ")]
    public void SplitsOnEverySupportedDelimiter(string entry)
    {
        Assert.Equal(["a", "b"], VideoPreCache.SplitUrls([entry]));
    }

    [Fact]
    public void LeavesASingleUrlAlone()
    {
        Assert.Equal(
            ["https://youtu.be/dQw4w9WgXcQ"],
            VideoPreCache.SplitUrls(["  https://youtu.be/dQw4w9WgXcQ  "]));
    }

    [Fact]
    public void FlattensAcrossEntriesAndDropsBlanks()
    {
        Assert.Equal(
            ["one", "two", "three"],
            VideoPreCache.SplitUrls(["one, two", "", "   ", "three"]));
    }
}
