using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class RecordingCorrectnessTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly MainViewModel _main;

    public RecordingCorrectnessTests()
    {
        _main = new(_displays, new FakeWindows(), new FakePicker(), _overlay, new FakeHotkeys(),
            _clipboard, new FakeDialogs(), new MemoryStore { Settings = new AppSettings { HideFromRecorders = true } }, new FakeStartup());
        _main.Initialize();
    }
    public void Dispose() => _main.Dispose();
    private RecordingSetupViewModel Setup() => new(_main, new FakeAppInfo(), _displays, _clipboard);
    private static void ReportPass(RecordingSetupViewModel setup)
    {
        setup.Recorder = "OBS 32.2.2"; setup.Browser = "Test fixture / N/A";
        setup.CaptureMethod = setup.CaptureMethods[1]; setup.Clip = "clip.mkv";
        setup.DimensionsMatch = setup.EdgesVisible = setup.OverlaysAbsent = true;
    }

    [Fact]
    public void Failed_native_exclusion_is_retried_and_never_cached_as_applied()
    {
        var request = new CaptureExclusionRequest();
        int calls = 0;
        CaptureExclusionResult Apply(bool exclude) => ++calls == 1 ? new(false, 87) : new(true);
        Assert.False(request.Apply(true, Apply).Succeeded);
        Assert.Null(request.AppliedValue);
        Assert.Equal(87, request.LastResult.ErrorCode);
        Assert.True(request.Apply(true, Apply).Succeeded);
        request.Apply(true, Apply);
        Assert.Equal(2, calls);
        Assert.True(request.AppliedValue);
        request.Apply(false, Apply);
        Assert.Equal(3, calls);
        Assert.False(request.AppliedValue);
    }

    [Fact]
    public void Failed_change_does_not_claim_the_previous_value_is_current()
    {
        var request = new CaptureExclusionRequest();
        request.Apply(true, _ => new(true));
        request.Apply(false, _ => new(false, 5));
        Assert.Null(request.AppliedValue);
        Assert.False(request.LastResult.Succeeded);
    }

    [Fact]
    public void Control_bar_failure_is_visible_even_when_other_overlays_succeeded()
    {
        _main.PrimaryCommand.Execute(null);
        _main.ReportHudCaptureExclusion(false, 87);
        Assert.True(_main.HudCaptureExclusionFailed);
        Assert.Contains("error 87", _main.CaptureExclusionStatusText);
        Assert.Contains("control bar", _main.StatusDetail);
        _main.CenterCommand.Execute(null);
        Assert.Equal(StatusKind.Warning, _main.StatusKind);
        _main.ReportHudCaptureExclusion(true);
        Assert.False(_main.HudCaptureExclusionFailed);
        Assert.Contains("still unverified", _main.CaptureExclusionStatusText);
        _main.HideOutlineCommand.Execute(null);
        Assert.Contains("Not checked", _main.CaptureExclusionStatusText);
    }

    [Fact]
    public void Native_success_does_not_mark_a_recording_as_verified()
    {
        _main.PrimaryCommand.Execute(null); _main.ReportHudCaptureExclusion(true);
        using var setup = Setup();
        Assert.False(setup.CheckComplete);
        Assert.Contains("unverified", setup.Result);
        Assert.Contains("Windows accepted", setup.Context);
        Assert.Contains("unverified", setup.Report());
    }

    [Fact]
    public void User_report_requires_clip_metadata_and_all_observations()
    {
        using var setup = Setup();
        setup.DimensionsMatch = setup.EdgesVisible = setup.OverlaysAbsent = true;
        Assert.False(setup.CheckComplete);
        ReportPass(setup);
        Assert.True(setup.CheckComplete);
        Assert.Contains("User-reported pass", setup.Result);
        Assert.Contains("has not inspected", setup.Result);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("display")]
    [InlineData("recorder")]
    [InlineData("method")]
    [InlineData("clip")]
    [InlineData("browser")]
    [InlineData("exclusion")]
    [InlineData("guides")]
    [InlineData("nested")]
    public void Changing_the_setup_invalidates_old_clip_observations(string change)
    {
        using var setup = Setup(); ReportPass(setup);
        switch (change)
        {
            case "frame": _main.WidthValue = 1280; break;
            case "display": _displays.RaiseChanged(); break;
            case "recorder": setup.Recorder = "Other 1.0"; break;
            case "method": setup.CaptureMethod = setup.CaptureMethods[2]; break;
            case "clip": setup.Clip = "new.mkv"; break;
            case "browser": setup.Browser = "Changed version"; break;
            case "exclusion": _main.HideFromRecorders = false; break;
            case "guides": _main.ShowThirds = true; break;
            case "nested": _main.AddSquareFrameCommand.Execute(null); break;
        }
        Assert.False(setup.CheckComplete);
        Assert.False(setup.DimensionsMatch);
        Assert.False(setup.EdgesVisible);
        Assert.False(setup.OverlaysAbsent);
    }

    [Fact]
    public void Copy_report_preserves_unverified_status_and_excludes_the_database_path()
    {
        using var setup = Setup();
        setup.CopyReportCommand.Execute(null);
        Assert.Contains("unverified", _clipboard.Text);
        Assert.Contains("150%", _clipboard.Text);
        Assert.DoesNotContain("exactframe.db", _clipboard.Text);
    }

    [Fact]
    public void Unknown_capture_method_cannot_complete_a_report()
    {
        using var setup = Setup(); ReportPass(setup);
        setup.CaptureMethod = "Unrecognized method";
        setup.DimensionsMatch = setup.EdgesVisible = setup.OverlaysAbsent = true;
        Assert.False(setup.CheckComplete);
        Assert.Contains("unverified", setup.Result);
    }
}
