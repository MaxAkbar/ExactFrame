using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;
using ExactFrame.Helpers;

namespace ExactFrame.Views;

/// <summary>Help and About: a searchable topic list beside the selected topic.</summary>
public sealed partial class HelpView : UserControl
{
    public HelpView(HelpViewModel help, MainViewModel main)
    {
        Help = help;
        Main = main;
        InitializeComponent();
        RenderTopic();

        // Both live as long as the main window, so there is nothing to unsubscribe.
        Help.PropertyChanged += OnHelpPropertyChanged;
    }

    public HelpViewModel Help { get; }

    /// <summary>For the live list of global shortcuts.</summary>
    public MainViewModel Main { get; }

    /// <summary>Puts the cursor in the search box, so typing searches right away.</summary>
    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private void OnHelpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HelpViewModel.SelectedTopic)) return;
        RenderTopic();
        ContentScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void RenderTopic() => HelpRenderer.Render(TopicBody, Help.SelectedTopic);
}
