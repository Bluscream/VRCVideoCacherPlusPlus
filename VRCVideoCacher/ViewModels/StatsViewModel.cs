using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Database;
using VRCVideoCacher.Utils;

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
        var sizeByVideoId = CacheManager.GetCachedAssets()
            .ToDictionary(
                kvp => Path.GetFileNameWithoutExtension(kvp.Key),
                kvp => kvp.Value.Size);

        var result = CacheStats.Compute(
            stats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.WatchCount),
            sizeByVideoId,
            DatabaseManager.GetPlayHistoryCount());

        TotalPlaysText = result.TotalPlays.ToString("N0");
        CacheHitsText = result.CacheHits.ToString("N0");
        HitRateText = result.HitRate is { } rate ? $"{rate:P1}" : "-";
        BytesSavedText = CacheStats.FormatSize(result.BytesSaved);
        BytesSavedCaveat = result.UncountedHits > 0
            ? string.Format(Localizer.Get("BytesSavedCaveatFormat"), result.UncountedHits)
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
}
