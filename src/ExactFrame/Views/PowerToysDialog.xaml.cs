using ExactFrame.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExactFrame.Views;

public sealed partial class PowerToysDialog : ContentDialog
{
    public PowerToysDialog(PowerToysViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    public PowerToysViewModel ViewModel { get; }
}
