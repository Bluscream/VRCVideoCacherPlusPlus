using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string Version { get; }
    public string PlusAuthor { get; } = "VRCVideoCacherPlus by codeyumx";
    public string CreatedBy { get; }
    public StatsViewModel Stats { get; } = new();

    /// <summary>
    /// Message of the day from the VRCVideoCacher API. Rendered through
    /// <see cref="Controls.MarkdownText"/> so announcements can carry links.
    /// </summary>
    [ObservableProperty]
    private string _motd = string.Empty;

    public bool HasMotd => !string.IsNullOrWhiteSpace(Motd);

    partial void OnMotdChanged(string value) => OnPropertyChanged(nameof(HasMotd));

    public AboutViewModel()
    {
        Version = VRCVideoCacher.Program.Version;
        CreatedBy = Localizer.Get("CreatedBy") + $" {VRCVideoCacher.Program.Creator_Elly}, {VRCVideoCacher.Program.Creator_Natsumi}, {VRCVideoCacher.Program.Creator_Haxy}, {VRCVideoCacher.Program.Creator_Hauskaz}, {VRCVideoCacher.Program.Creator_DubyaDude}";

        Motd = VvcConfigService.CurrentConfig.Motd;

        // The config fetch runs during backend startup, which normally finishes after this
        // view model has been constructed, so take the value again when it arrives.
        VvcConfigService.OnApiConfigChanged += OnApiConfigChanged;
    }

    private void OnApiConfigChanged() =>
        Dispatcher.UIThread.Post(() => Motd = VvcConfigService.CurrentConfig.Motd);
}
