using ExactFrame.Core.Help;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

internal sealed class FakeAppInfo : IAppInfo
{
    public string Version => "2.0.0";

    public string RuntimeVersion => "10.0.4";

    public string WindowsAppSdkVersion => "2.5.1";

    public string OperatingSystem => "Windows 10.0.26100 (x64)";

        public string DatabasePath => @"C:\Users\me\AppData\Local\ExactFrame\exactframe.db";
}

internal sealed class FakeShell : IShellService
{
    public string? Revealed { get; private set; }

    public void RevealInExplorer(string path) => Revealed = path;
}

public sealed class HelpTests
{
    private readonly FakeShell _shell = new();
    private readonly FakeClipboard _clipboard = new();

    private HelpViewModel Create() => new(new FakeAppInfo(), _shell, _clipboard, new FakeDisplays());

    [Fact]
    public void Every_topic_has_content_and_a_unique_id()
    {
        Assert.Equal(HelpContent.Topics.Count, HelpContent.Topics.Select(t => t.Id).Distinct().Count());
        Assert.All(HelpContent.Topics, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.False(string.IsNullOrWhiteSpace(t.Summary));
            Assert.NotEmpty(t.Sections);
            Assert.All(t.Sections, s => Assert.NotEmpty(s.Blocks));
        });
    }

    [Fact]
    public void Bold_markers_are_balanced()
    {
        foreach (var topic in HelpContent.Topics)
            foreach (var block in topic.Sections.SelectMany(s => s.Blocks))
                foreach (string text in block.AllText())
                    Assert.True(CountMarkers(text) % 2 == 0, $"Unbalanced ** in {topic.Id}: {text}");

        static int CountMarkers(string text) => (text.Length - text.Replace("**", string.Empty).Length) / 2;
    }

    [Theory]
    [InlineData(FrameMode.Outline, SettingsTab.Frame, HelpContent.OutlineArea)]
    [InlineData(FrameMode.Outline, SettingsTab.Style, HelpContent.StyleAndGuides)]
    [InlineData(FrameMode.Outline, SettingsTab.Profiles, HelpContent.Profiles)]
    [InlineData(FrameMode.Resize, SettingsTab.Style, HelpContent.ResizeWindow)]
    public void Opens_at_the_topic_for_what_the_user_is_doing(FrameMode mode, SettingsTab tab, string expected)
    {
        var help = Create();

        help.Open(HelpViewModel.TopicFor(mode, tab));

        Assert.True(help.IsOpen);
        Assert.Equal(expected, help.SelectedTopic.Id);
        Assert.True(help.VisibleTopics.Single(t => t.Topic.Id == expected).IsSelected);
    }

    [Fact]
    public void Search_filters_topics_and_keeps_a_visible_selection()
    {
        var help = Create();
        help.Open(HelpContent.About);

        help.SearchText = "minimized";

        Assert.Equal([HelpContent.ResizeWindow], help.VisibleTopics.Select(t => t.Topic.Id));
        Assert.Equal(HelpContent.ResizeWindow, help.SelectedTopic.Id);
        Assert.False(help.HasNoResults);
    }

    [Fact]
    public void Search_ignores_bold_markers_and_needs_every_word()
    {
        var help = Create();

        help.SearchText = "copy bounds recorder";
        Assert.Contains(help.VisibleTopics, t => t.Topic.Id == HelpContent.GettingStarted);

        help.SearchText = "zebra";
        Assert.True(help.HasNoResults);
    }

    [Fact]
    public void Opening_clears_the_search()
    {
        var help = Create();
        help.SearchText = "zebra";

        help.Open(HelpContent.Shortcuts);

        Assert.Equal(string.Empty, help.SearchText);
        Assert.Equal(HelpContent.Topics.Count, help.VisibleTopics.Count);
        Assert.True(help.ShowShortcuts);
        Assert.False(help.ShowAbout);
    }

    [Fact]
    public void Close_hides_help()
    {
        var help = Create();
        help.Open();

        help.CloseCommand.Execute(null);

        Assert.False(help.IsOpen);
    }

    [Fact]
    public void About_shows_versions_and_reveals_the_settings_file()
    {
        var help = Create();
        help.Open(HelpContent.About);

        Assert.True(help.ShowAbout);
        Assert.Equal("ExactFrame 2.0.0", help.AppTitle);
        Assert.Contains(new AboutRow("Windows App SDK", "2.5.1"), help.AboutRows);

        help.RevealSettingsCommand.Execute(null);
        Assert.Equal(new FakeAppInfo().DatabasePath, _shell.Revealed);
    }

    [Fact]
    public async Task Copy_system_info_includes_versions_and_displays()
    {
        var help = Create();

        await help.CopySystemInfoCommand.ExecuteAsync(null);

        Assert.StartsWith("ExactFrame 2.0.0", _clipboard.Text);
        Assert.Contains("Display 1: 3840 × 2160 at (0, 0), 150%, work area 3840 × 2088 at (0, 0), primary", _clipboard.Text);
        Assert.Contains("Display 2: 2560 × 1440 at (3840, 0), 100%", _clipboard.Text);
        Assert.False(help.IsInfoCopied);
    }
}
