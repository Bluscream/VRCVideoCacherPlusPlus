using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Database;

namespace VRCVideoCacher.ViewModels;

public class TopVideoViewModel : ViewModelBase
{
    public required string VideoId { get; init; }
    public required int WatchCount { get; init; }
    public required DateTime LastWatchedAt { get; init; }
    public string? Title { get; init; }

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;
    public bool IsStillCached { get; init; }
}

public partial class StatsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _hitRateText = "-";

    [ObservableProperty]
    private string _bytesSavedText = "-";

    [ObservableProperty]
    private string _cacheHitsText = "-";

    [ObservableProperty]
    private string _totalPlaysText = "-";

    [ObservableProperty]
    private string _bytesSavedCaveat = string.Empty;

    public ObservableCollection<TopVideoViewModel> TopVideos { get; } = [];

    public StatsViewModel()
    {
        DatabaseManager.OnPlayHistoryAdded += () => Dispatcher.UIThread.Post(Refresh);
        CacheManager.OnCacheChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.InvokeAsync(Refresh);

        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var stats = DatabaseManager.GetAllVideoWatchStats();
        var totalPlays = DatabaseManager.GetPlayHistoryCount();
        var cacheHits = stats.Values.Sum(s => s.WatchCount);

        // Size is only known for videos still on disk. A hit on a since-evicted video
        // really did save bandwidth, but we have no size to attribute to it — so the
        // total is a floor, not an exact figure.
        var sizeByVideoId = CacheManager.GetCachedAssets()
            .ToDictionary(
                kvp => Path.GetFileNameWithoutExtension(kvp.Key),
                kvp => kvp.Value.Size);

        long bytesSaved = 0;
        var uncountedHits = 0;
        foreach (var (videoId, stat) in stats)
        {
            if (sizeByVideoId.TryGetValue(videoId, out var size))
                bytesSaved += size * stat.WatchCount;
            else
                uncountedHits += stat.WatchCount;
        }

        TotalPlaysText = totalPlays.ToString("N0");
        CacheHitsText = cacheHits.ToString("N0");
        HitRateText = totalPlays > 0 ? $"{(double)cacheHits / totalPlays:P1}" : "-";
        BytesSavedText = FormatSize(bytesSaved);
        BytesSavedCaveat = uncountedHits > 0
            ? string.Format(Localizer.Get("BytesSavedCaveatFormat"), uncountedHits)
            : string.Empty;

        TopVideos.Clear();
        foreach (var (videoId, stat) in stats.OrderByDescending(s => s.Value.WatchCount).Take(10))
        {
            TopVideos.Add(new TopVideoViewModel
            {
                VideoId = videoId,
                WatchCount = stat.WatchCount,
                LastWatchedAt = stat.LastWatchedAt.ToLocalTime(),
                Title = DatabaseManager.GetVideoInfoCache(videoId)?.Title,
                IsStillCached = sizeByVideoId.ContainsKey(videoId)
            });
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0) return "0 B";
        var mag = Math.Min((int)Math.Log(bytes, 1024), suffixes.Length - 1);
        return $"{bytes / Math.Pow(1024, mag):N2} {suffixes[mag]}";
    }
}
