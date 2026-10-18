using ExactFrame.Core.Obs;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class ObsViewModelTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly FakeObsClient _client = new();
    private readonly MainViewModel _frame;
    private readonly ObsService _service;
    private readonly ObsViewModel _viewModel;
    public ObsViewModelTests()
    {
        _frame = new MainViewModel(_displays, new FakeWindows(), new FakePicker(), new FakeOverlay(), new FakeHotkeys(),
            new FakeClipboard(), new FakeDialogs(), new MemoryStore(), new FakeStartup());
        _frame.Initialize();
        _service = new ObsService(_client, _displays);
        _client.Disconnect();
        _viewModel = new ObsViewModel(_service, _frame, _displays);
    }
    private async Task Connect()
    {
        _viewModel.Password = "session-password";
        await _viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.True(_viewModel.IsConnected);
    }

    [Fact]
    public async Task Connect_clears_password_and_selects_a_source_without_changing_OBS()
    {
        await Connect();
        Assert.Equal("", _viewModel.Password);
        Assert.Equal("Tutorial", _viewModel.SelectedScene?.Name);
        Assert.Equal("Desktop", _viewModel.SelectedSource?.Name);
        Assert.False(_viewModel.ApplyCommand.CanExecute(null));
        Assert.DoesNotContain(_client.Calls, c => c.Type.StartsWith("Set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Applying_requires_a_preview_and_consumes_it()
    {
        await Connect();
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.True(_viewModel.ApplyCommand.CanExecute(null));
        await _viewModel.ApplyCommand.ExecuteAsync(null);
        Assert.False(_viewModel.HasPreview);
        Assert.False(_viewModel.ApplyCommand.CanExecute(null));
        Assert.True(_viewModel.UndoCommand.CanExecute(null));
        Assert.Contains("verified", _viewModel.Status);
    }

    [Fact]
    public async Task Editing_the_ExactFrame_region_invalidates_the_OBS_preview()
    {
        await Connect();
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.True(_viewModel.HasPreview);
        _frame.WidthValue = 1280;
        Assert.False(_viewModel.HasPreview);
        Assert.False(_viewModel.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_nested_frame_can_be_previewed_and_changes_invalidate_it()
    {
        _frame.AddShortsFrameCommand.Execute(null);
        await Connect();
        _viewModel.SelectedRegion = _viewModel.Regions[1];
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.True(_viewModel.HasPreview);
        Assert.Equal("Shorts 9:16", _viewModel.Plan?.Region.Name);
        _frame.NestedFrames[0].ScalePercent = 50;
        Assert.False(_viewModel.HasPreview);
    }

    [Fact]
    public async Task Display_changes_invalidate_a_prepared_transform()
    {
        await Connect();
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        _displays.RaiseChanged();
        Assert.False(_viewModel.HasPreview);
    }

    [Fact]
    public async Task Errors_are_presented_without_enabling_apply()
    {
        await Connect();
        _client.Filtered = true;
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("filters", _viewModel.Status);
        Assert.False(_viewModel.ApplyCommand.CanExecute(null));
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task Disconnect_clears_selection_and_undo_history()
    {
        await Connect();
        await _viewModel.PreviewCommand.ExecuteAsync(null);
        await _viewModel.ApplyCommand.ExecuteAsync(null);
        _viewModel.DisconnectCommand.Execute(null);
        Assert.False(_viewModel.IsConnected);
        Assert.Empty(_viewModel.Sources);
        Assert.False(_viewModel.UndoCommand.CanExecute(null));
        Assert.Null(_viewModel.Plan);
    }
    public void Dispose() { _viewModel.Dispose(); _service.Dispose(); _frame.Dispose(); _client.Dispose(); }
}
