using Avalonia.Controls;
using Avalonia.Interactivity;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class StatsView : UserControl
{
    public StatsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as StatsViewModel)?.RefreshCommand.Execute(null);
    }
}
