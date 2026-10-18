using ExactFrame.Core.Obs;
using ExactFrame.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExactFrame.Views;

public sealed partial class ObsDialog : ContentDialog
{
    public ObsDialog(ObsViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.SyncRegions();
        InitializeComponent();
    }
    public ObsViewModel ViewModel { get; }
    private async void OnSceneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ObsScene scene } && scene != ViewModel.SelectedScene && !ViewModel.IsBusy)
            await ViewModel.SelectSceneAsync(scene);
    }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = ViewModel.IsBusy;
        if (!args.Cancel) ViewModel.Password = string.Empty;
    }
}
