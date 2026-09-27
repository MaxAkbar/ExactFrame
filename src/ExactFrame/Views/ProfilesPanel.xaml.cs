using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Views;

/// <summary>Profiles tab: saved setups with their shortcuts, and the global hotkeys.</summary>
public sealed partial class ProfilesPanel : UserControl
{
    public ProfilesPanel(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
