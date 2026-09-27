using System.Drawing;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Tests;

internal sealed class FakeDisplays : IDisplayService
{
    public static readonly DisplayInfo Primary = new(@"\\.\DISPLAY1", 1,
        new Rectangle(0, 0, 3840, 2160), new Rectangle(0, 0, 3840, 2088), 144, true);

    public static readonly DisplayInfo Secondary = new(@"\\.\DISPLAY2", 2,
        new Rectangle(3840, 0, 2560, 1440), new Rectangle(3840, 0, 2560, 1392), 96, false);

    public List<DisplayInfo> Displays { get; } = [Primary, Secondary];

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayInfo> GetDisplays() => Displays;

    public void RaiseChanged() => DisplaysChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeWindows : IWindowService
{
    public static readonly WindowInfo Studio = new(0x1001, 10, 11, "ExactFrame — Microsoft Visual Studio", "devenv",
        new Rectangle(240, 180, 2880, 1720));

    public static readonly WindowInfo Terminal = new(0x2002, 20, 21, "PowerShell", "WindowsTerminal",
        new Rectangle(1200, 640, 1600, 900));

    public static readonly WindowInfo Notes = new(0x3003, 30, 31, "Notes — Notepad", "notepad",
        new Rectangle(300, 200, 1200, 800), IsMinimized: true);

    private readonly HashSet<nint> _saved = [];

    public List<WindowInfo> Windows { get; } = [Studio, Terminal, Notes];

    public Rectangle? AcceptedSize { get; set; }

    public List<(WindowInfo Window, Rectangle Desired, WindowArea Area)> Resizes { get; } = [];

    public IReadOnlyList<WindowInfo> GetWindows() => Windows;

    public Rectangle Measure(WindowInfo window, WindowArea area)
    {
        if (Current(window).IsMinimized) throw new InvalidOperationException("That window is minimized.");
        return area == WindowArea.Client
            ? new Rectangle(window.Bounds.X, window.Bounds.Y + 48, window.Bounds.Width, window.Bounds.Height - 48)
            : window.Bounds;
    }

    private WindowInfo Current(WindowInfo window) => Windows.FirstOrDefault(w => w.IsSameWindow(window)) ?? window;

    public bool CanRestore(WindowInfo? window) => window is not null && _saved.Contains(window.Handle);

    public Task<ResizeResult> ResizeAsync(WindowInfo window, Rectangle desired, WindowArea area, CancellationToken cancellation)
    {
        Resizes.Add((window, desired, area));
        _saved.Add(window.Handle);
        var actual = AcceptedSize is { } accepted ? new Rectangle(desired.Location, accepted.Size) : desired;

        // Like the real service: a minimized window is restored first and ends up on screen at its new size.
        bool restored = Current(window).IsMinimized;
        int index = Windows.FindIndex(w => w.IsSameWindow(window));
        if (index >= 0) Windows[index] = Windows[index] with { IsMinimized = false, Bounds = actual };
        return Task.FromResult(new ResizeResult(actual, actual == desired, restored));
    }

    public Task RestoreAsync(WindowInfo window, CancellationToken cancellation)
    {
        _saved.Remove(window.Handle);
        return Task.CompletedTask;
    }
}

internal sealed class FakePicker : IWindowPicker
{
    public WindowInfo? Result { get; set; }

    public Task<WindowInfo?> PickAsync(CancellationToken cancellation) => Task.FromResult(Result);
}

internal sealed class FakeOverlay : IOutlineOverlay
{
    public event EventHandler? FrameMoved;

    public event EventHandler? VisibilityChanged;

    public bool IsVisible { get; private set; }

    public Rectangle Frame { get; private set; }

    public DisplayInfo? Display { get; private set; }

    public bool CaptureExclusionApplied { get; set; } = true;

    public OutlineStyle? Style { get; private set; }

    public bool Locked { get; private set; }

    public bool Excluded { get; private set; }

    public int ShowCount { get; private set; }

    public IReadOnlyList<NestedFrameBounds> NestedFrames { get; private set; } = [];

    public void Show(Rectangle frame, DisplayInfo display, bool keepClearOfTaskbar, IReadOnlyList<NestedFrameBounds> nestedFrames)
    {
        Frame = frame;
        Display = display;
        NestedFrames = nestedFrames;
        ShowCount++;
        if (IsVisible) return;
        IsVisible = true;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Drag(Point location)
    {
        Frame = new Rectangle(location, Frame.Size);
        FrameMoved?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyStyle(OutlineStyle style) => Style = style;

    public void SetLocked(bool locked) => Locked = locked;

    public void SetCaptureExclusion(bool exclude) => Excluded = exclude;

    public void Dispose()
    {
    }
}

internal sealed class FakeHotkeys : IHotkeyService
{
    public Dictionary<int, HotkeyGesture> Registered { get; } = [];

    public HashSet<HotkeyGesture> Taken { get; } = [];

    public event EventHandler<int>? Pressed;

    public bool Register(int id, HotkeyGesture gesture)
    {
        if (Taken.Contains(gesture) || Registered.ContainsValue(gesture)) return false;
        Registered[id] = gesture;
        return true;
    }

    public void Unregister(int id) => Registered.Remove(id);

    public void Press(int id) => Pressed?.Invoke(this, id);

    public void Dispose()
    {
    }
}

internal sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public void SetText(string text) => Text = text;
}

internal sealed class FakeDialogs : IDialogService
{
    public string? ProfileName { get; set; }

    public HotkeyGesture? RecordedHotkey { get; set; }

    public bool Confirm { get; set; } = true;

    public Task<string?> PromptProfileNameAsync(string suggestion) => Task.FromResult(ProfileName);

    public Task<HotkeyGesture?> RecordHotkeyAsync(string actionName, HotkeyGesture current) => Task.FromResult(RecordedHotkey);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(Confirm);
}

internal sealed class MemoryStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public AppSettings Load() => Settings;

    public void Save(AppSettings settings)
    {
        Settings = settings;
        SaveCount++;
    }
}
