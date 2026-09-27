using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Views;

/// <summary>Style tab: outline color, weight and line, dimming, guides and the on-screen controls.</summary>
public sealed partial class StylePanel : UserControl
{
    public StylePanel(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
