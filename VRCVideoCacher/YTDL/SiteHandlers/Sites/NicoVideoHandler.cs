using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class NicoVideoHandler : ISiteHandler
{

    private static readonly ILogger Log = Program.Logger.ForContext<NicoVideoHandler>();

    // Matches full nicovideo/niconico URLs
    private static readonly Regex NicoID1 = new(@"^(https?)://(live|www)\.nicovideo\.jp/watch/(.+)$", RegexOptions.Compiled);
    private static readonly Regex NicoID2 = new(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled);

    public bool CanHandle(Uri uri) => false; // rewrite only, GenericHandler picks up after

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!uri.Host.EndsWith("nicovideo.jp") && !uri.Host.EndsWith("nico.ms"))
            return Task.FromResult(url);

        var (m, group) = new[]
        {
            (NicoID1.Match(url), 3),
            (NicoID2.Match(url), 2),
        }.FirstOrDefault(x => x.Item1.Success);

        if (m?.Success != true)
            return Task.FromResult(url);

        // nicovideo.life is an unofficial third-party mirror, unaffiliated with this project
        // and with niconico. Rewriting here means playing a niconico link tells that mirror
        // what is being watched — documented under "What does it connect to?" in the README.
        var newUrl = $"https://www.nicovideo.life/watch/{m.Groups[group].Value}";
        Log.Information("Incompatible URL, passing to third-party resolver {Host}: {URL}", "nicovideo.life", newUrl);
        return Task.FromResult(newUrl);
    }

}