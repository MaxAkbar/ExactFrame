using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Views;

/// <summary>Frame tab: size presets, width and height, anchor and position, click-through and capture exclusion.</summary>
public sealed partial class FramePanel : UserControl
{
    public FramePanel(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
