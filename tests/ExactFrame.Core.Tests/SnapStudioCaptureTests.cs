using System.Drawing;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Core.Tests;

public sealed class SnapStudioCaptureTests : IDisposable
{
    private readonly FakeDisplays _displays = new();
    private readonly MemoryStore _store = new();
    private readonly CaptureService _capture = new();
    private MainViewModel? _vm;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ExactFrame-SnapStudio-" + Guid.NewGuid().ToString("N"));

    private MainViewModel Create(string? application = "C:\\SnapStudio\\SnapStudio.App.exe")
    {
        _store.Settings.SnapStudioExecutablePath = application;
        _vm = new MainViewModel(_displays, new FakeWindows(), new FakePicker(), new FakeOverlay(), new FakeHotkeys(),
            new FakeClipboard(), new FakeDialogs(), _store, new FakeStartup(), snapStudioCapture: _capture);
        _vm.Initialize();
        return _vm;
    }

    public void Dispose()
    {
        _vm?.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public async Task Main_frame_uses_physical_pixels_at_150_percent_display_scaling()
    {
        var vm = Create();
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(new Rectangle(960, 540, 1920, 1080), _capture.Region);
        Assert.Equal("Main frame", _capture.Name);
        Assert.Contains("Opened the frame", vm.StatusDetail);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Nested_guide_uses_its_screen_crop_without_upscaling_to_output_size()
    {
        var vm = Create();
        vm.AddShortsFrameCommand.Execute(null);
        var guide = vm.NestedFrames.Single();
        vm.SnapStudioFrameId = guide.Frame.Id;
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(guide.Bounds, _capture.Region);
        Assert.NotEqual(guide.Frame.OutputHeight, _capture.Region.Height);
        Assert.Equal(guide.Name, _capture.Name);
        Assert.Equal(2, vm.SnapStudioFrames.Count);
    }

    [Fact]
    public void Removing_the_selected_guide_returns_capture_to_the_main_frame()
    {
        var vm = Create();
        vm.AddSquareFrameCommand.Execute(null);
        var guide = vm.NestedFrames.Single();
        vm.SnapStudioFrameId = guide.Frame.Id;
        guide.RemoveCommand.Execute(null);
        Assert.Equal("main", vm.SnapStudioFrameId);
        Assert.Single(vm.SnapStudioFrames);
    }

    [Fact]
    public async Task Cancelled_first_use_picker_does_not_capture_or_save_a_path()
    {
        var vm = Create(null);
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(1, _capture.ChooseCount);
        Assert.Equal(0, _capture.CaptureCount);
        Assert.Null(_store.Settings.SnapStudioExecutablePath);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task First_use_remembers_the_application_and_reuses_it()
    {
        _capture.Chosen = "C:\\Portable\\SnapStudio.App.exe";
        var vm = Create(null);
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(1, _capture.ChooseCount);
        Assert.Equal(2, _capture.CaptureCount);
        Assert.Equal(_capture.Chosen, _store.Settings.SnapStudioExecutablePath);
        Assert.True(_store.SaveCount > 0);
    }

    [Fact]
    public async Task Capture_failure_is_reported_and_allows_retry()
    {
        _capture.Failure = new IOException("SnapStudio closed the capture connection.");
        var vm = Create();
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Contains("closed the capture connection", vm.StatusDetail);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CaptureInSnapStudioCommand.CanExecute(null));
        _capture.Failure = null;
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(2, _capture.CaptureCount);
        Assert.Contains("Opened the frame", vm.StatusDetail);
    }

    [Fact]
    public async Task A_removed_display_is_rejected_before_any_capture()
    {
        var vm = Create();
        _displays.Displays.Clear();
        await vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.Equal(0, _capture.CaptureCount);
        Assert.Contains("one connected display", vm.StatusDetail);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Busy_capture_disables_both_commands_until_completion()
    {
        _capture.Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Create();
        Task pending = vm.CaptureInSnapStudioCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CaptureInSnapStudioCommand.CanExecute(null));
        Assert.False(vm.ChooseSnapStudioCommand.CanExecute(null));
        _capture.Pending.SetResult();
        await pending;
        Assert.True(vm.CaptureInSnapStudioCommand.CanExecute(null));
    }

    [Fact]
    public void Geometry_allows_negative_monitor_origins_and_rejects_overflow_or_oversized_images()
    {
        SnapStudioCaptureConnection.ValidateRegion(new Rectangle(-1920, -1080, 1920, 1080));
        Assert.Throws<InvalidOperationException>(() => SnapStudioCaptureConnection.ValidateRegion(new Rectangle(0, 0, 3, 720)));
        Assert.Throws<InvalidOperationException>(() => SnapStudioCaptureConnection.ValidateRegion(new Rectangle(0, 0, 10000, 10000)));
        Assert.Throws<InvalidOperationException>(() => SnapStudioCaptureConnection.ValidateRegion(new Rectangle(int.MaxValue, 0, 100, 100)));
    }

    [Fact]
    public void Older_portable_builds_are_rejected_and_new_capability_marker_is_required()
    {
        Directory.CreateDirectory(_folder);
        string executable = Path.Combine(_folder, "SnapStudio.App.exe");
        File.WriteAllText(executable, "test fixture - never executed");
        Assert.Throws<InvalidOperationException>(() => SnapStudioCaptureConnection.ValidateApplication(executable));
        string marker = Path.Combine(_folder, "SnapStudio.integration.json");
        File.WriteAllText(marker, "{\"externalCaptureProtocol\":2}");
        Assert.Throws<InvalidOperationException>(() => SnapStudioCaptureConnection.ValidateApplication(executable));
        File.WriteAllText(marker, "{\"externalCaptureProtocol\":1}");
        SnapStudioCaptureConnection.ValidateApplication(executable);
        string settings = Path.Combine(_folder, "settings.json");
        var store = new JsonSettingsStore(settings);
        store.Save(new AppSettings { SnapStudioExecutablePath = executable });
        Assert.Equal(executable, store.Load().SnapStudioExecutablePath);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("bad-json")]
    [InlineData("wrong-id")]
    [InlineData("oversized")]
    [InlineData("disconnected")]
    public async Task Client_checks_response_correlation_errors_and_transport_limits(string mode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string name = "ExactFrame.test." + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(deadline.Token);
            using var reader = new StreamReader(server, leaveOpen: true);
            using var request = JsonDocument.Parse((await reader.ReadLineAsync(deadline.Token))!);
            Assert.Equal(-1920, request.RootElement.GetProperty("x").GetInt32());
            Guid id = request.RootElement.GetProperty("requestId").GetGuid();
            string reply = mode switch
            {
                "bad-json" => "{bad",
                "wrong-id" => JsonSerializer.Serialize(new { protocolVersion = 1, requestId = Guid.NewGuid(), succeeded = true }),
                "oversized" => new string('x', 8192),
                _ => JsonSerializer.Serialize(new { protocolVersion = 1, requestId = id, succeeded = mode != "failure", error = "Finish the current dialog." })
            };
            if (mode == "disconnected") { server.Disconnect(); return; }
            await server.WriteAsync(Encoding.UTF8.GetBytes(reply + (mode == "oversized" ? "" : "\n")), deadline.Token);
        }, deadline.Token);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(deadline.Token);
        Task capture = SnapStudioCaptureConnection.RequestAsync(client, new Rectangle(-1920, 0, 1280, 720), "Main frame", deadline.Token);
        if (mode == "success") await capture;
        else if (mode == "failure") Assert.Contains("Finish the current dialog", (await Assert.ThrowsAsync<InvalidOperationException>(() => capture)).Message);
        else if (mode == "disconnected") await Assert.ThrowsAsync<IOException>(() => capture);
        else await Assert.ThrowsAsync<InvalidDataException>(() => capture);
        await serve;
    }

    private sealed class CaptureService : ISnapStudioCaptureService
    {
        public string? Chosen { get; set; }
        public int ChooseCount { get; private set; }
        public int CaptureCount { get; private set; }
        public Rectangle Region { get; private set; }
        public string? Name { get; private set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource? Pending { get; set; }
        public Task<string?> ChooseApplicationAsync(CancellationToken cancellation) { ChooseCount++; return Task.FromResult(Chosen); }
        public Task CaptureAsync(string executable, Rectangle region, string name, CancellationToken cancellation)
        {
            CaptureCount++; Region = region; Name = name;
            return Failure is not null ? Task.FromException(Failure) : Pending?.Task ?? Task.CompletedTask;
        }
    }
}
