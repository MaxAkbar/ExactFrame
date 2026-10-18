using ExactFrame.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExactFrame.Views;

public sealed partial class RecordingSetupDialog : ContentDialog
{
    public RecordingSetupDialog(RecordingSetupViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
    public RecordingSetupViewModel ViewModel { get; }
}
