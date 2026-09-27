using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Views;

/// <summary>Resize window mode: choose a window, the exact size, what the size measures and where it goes.</summary>
public sealed partial class ResizePanel : UserControl
{
    public ResizePanel(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
