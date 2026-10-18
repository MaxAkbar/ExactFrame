using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ExactFrame.Core.ViewModels;
using ExactFrame.Helpers;

namespace ExactFrame.Views;

/// <summary>Help and About: a searchable topic list beside the selected topic.</summary>
public sealed partial class HelpView : UserControl
{
    private bool _topicSyncQueued;
    public HelpView(HelpViewModel help, MainViewModel main)
    {
        Help = help;
        Main = main;
        InitializeComponent();
        RenderTopic();

        // Both live as long as the main window, so there is nothing to unsubscribe.
        Help.PropertyChanged += OnHelpPropertyChanged;
        Help.VisibleTopics.CollectionChanged += (_, _) =>
        {
            // Filtering clears and repopulates the list. Restore the active topic once that batch
            // finishes, including when it still matches and SelectedTopic did not change.
            if (_topicSyncQueued) return;
            _topicSyncQueued = true;
            DispatcherQueue.TryEnqueue(() => { _topicSyncQueued = false; SyncCompactTopic(); });
        };
        SizeChanged += (_, _) => UpdateLayoutForTextScale(new Windows.UI.ViewManagement.UISettings().TextScaleFactor);
    }

    public HelpViewModel Help { get; }

    /// <summary>For the live list of global shortcuts.</summary>
    public MainViewModel Main { get; }

    public void UpdateLayoutForTextScale(double textScale)
    {
        bool compact = ActualWidth < 900 * Math.Max(1, textScale);
        TopicsColumn.Width = new GridLength(compact ? 0 : 264 * textScale);
        TopicsPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactTopicsBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SyncCompactTopic();
    }

    /// <summary>Puts the cursor in the search box, so typing searches right away.</summary>
    public void FocusSearch() => (CompactTopicsBar.Visibility == Visibility.Visible ? CompactSearch : SearchBox).Focus(FocusState.Programmatic);

    private void SyncCompactTopic() => CompactTopics.SelectedItem = Help.VisibleTopics.FirstOrDefault(t => t.Topic == Help.SelectedTopic);

    private void OnCompactTopicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompactTopics.SelectedItem is HelpTopicItem item) item.SelectCommand.Execute(null);
    }

    private void OnHelpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HelpViewModel.SelectedTopic)) return;
        RenderTopic();
        SyncCompactTopic();
        ContentScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void RenderTopic() => HelpRenderer.Render(TopicBody, Help.SelectedTopic);
}
