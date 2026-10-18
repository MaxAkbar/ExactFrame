using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;
using ExactFrame.Native;
using Microsoft.UI.Dispatching;

namespace ExactFrame.Services;

/// <summary>
/// Temporary input surfaces let the user draw without capturing the desktop or moving another app.
/// Every native window is created, drawn and destroyed on the UI thread.
/// </summary>
internal sealed class FrameRegionPicker : IFrameRegionPicker
{
    private const int VkEscape = 0x1B;
    private const int IdcCross = 32515;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly NativeMethods.HookProc _keyboardProc;
    private TaskCompletionSource<Rectangle?>? _pending;
    private PickerWindow? _dragWindow;
    private nint _keyboardHook;

    public FrameRegionPicker() => _keyboardProc = KeyboardProc;

    public async Task<Rectangle?> PickAsync(IReadOnlyList<DisplayInfo> displays, bool keepClearOfTaskbar,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Draw on screen must start on the app's UI thread.");
        if (_pending is not null)
            throw new InvalidOperationException("A frame selection is already open.");
        if (cancellation.IsCancellationRequested) return null;

        var targets = displays.Select(display => (Display: display,
                Area: Rectangle.Intersect(display.Bounds, display.UsableArea(keepClearOfTaskbar))))
            .Where(target => target.Area.Width >= 100 && target.Area.Height >= 100).ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("No connected display has room for a frame of at least 100 × 100 pixels.");

        var pending = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        var windows = new List<PickerWindow>();
        try
        {
            // Prepare every surface before showing any. Failed exclusion never leaves a selector visible.
            foreach (var target in targets)
            {
                var window = new PickerWindow(this, target.Display, target.Area, dispatcher);
                windows.Add(window);
                if (!window.SetCaptureExclusion(true))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Windows could not hide the selection surface from capture. Draw on screen was canceled before showing it.");
                window.Paint();
            }

            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardProc,
                NativeMethods.GetModuleHandle(null), 0);
            if (_keyboardHook == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enable Escape to cancel frame selection.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            // Cancellation may run on a worker thread: it only completes the task, never touches an HWND.
            using var registration = timeout.Token.Register(() => pending.TrySetResult(null));
            if (!pending.Task.IsCompleted)
                foreach (var window in windows) window.Show();

            return await pending.Task;
        }
        finally
        {
            await OnUiThreadAsync(dispatcher, () =>
            {
                _dragWindow = null;
                if (_keyboardHook != 0)
                {
                    NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                    _keyboardHook = 0;
                }
                foreach (var window in windows) window.Dispose();
                _pending = null;
            });
        }
    }

    private static Task OnUiThreadAsync(DispatcherQueue dispatcher, Action action)
    {
        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try { action(); pending.TrySetResult(); }
            catch (Exception ex) { pending.TrySetException(ex); }
        })) pending.TrySetException(new InvalidOperationException("The app closed before frame selection could be cleaned up."));
        return pending.Task;
    }

    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _pending is { Task.IsCompleted: false } &&
            (uint)wParam is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardLowLevel>(lParam);
            if (data.VirtualKey == VkEscape)
            {
                Complete(null);
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void Complete(Rectangle? bounds) => _pending?.TrySetResult(bounds);

    private void Fail(Exception exception) => _pending?.TrySetException(exception);

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    private sealed class PickerWindow : LayeredWindow
    {
        private readonly FrameRegionPicker _owner;
        private readonly DisplayInfo _display;
        private readonly Rectangle _area;
        private readonly DispatcherQueue _dispatcher;
        private Point _start;
        private Rectangle? _selection;
        private bool _dragging;
        private bool _paintQueued;

        public PickerWindow(FrameRegionPicker owner, DisplayInfo display, Rectangle area,
            DispatcherQueue dispatcher) : base(clickThrough: false)
        {
            _owner = owner;
            _display = display;
            _area = area;
            _dispatcher = dispatcher;
        }

        public void Paint() => Render(_display.Bounds, Draw);

        private void Draw(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var dim = new SolidBrush(Color.FromArgb(112, 0, 0, 0));
            graphics.FillRectangle(dim, 0, 0, _display.Bounds.Width, _display.Bounds.Height);

            float scale = (float)_display.Scale;
            if (_selection is { Width: > 0, Height: > 0 } selection)
            {
                var local = new Rectangle(selection.X - _display.Bounds.X,
                    selection.Y - _display.Bounds.Y, selection.Width, selection.Height);
                // Alpha 1 keeps the selected interior an input surface; no desktop pixels are copied.
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    graphics.FillRectangle(clear, local);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using var border = new Pen(Color.FromArgb(255, 52, 213, 181), Math.Max(2, 2 * scale));
                border.Alignment = PenAlignment.Inset;
                graphics.DrawRectangle(border, local.X, local.Y, Math.Max(0, local.Width - 1), Math.Max(0, local.Height - 1));
            }

            float padding = 16 * scale;
            float panelWidth = Math.Min(580 * scale, Math.Max(1, _area.Width - padding * 2));
            float left = _area.Left - _display.Bounds.Left + Math.Min(padding, _area.Width / 4f);
            float top = _area.Top - _display.Bounds.Top + Math.Min(padding, _area.Height / 4f);
            using var background = new SolidBrush(Color.FromArgb(245, 15, 31, 36));
            using var white = new SolidBrush(Color.White);
            using var soft = new SolidBrush(Color.FromArgb(255, 190, 211, 211));
            using var title = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.FillRectangle(background, left, top, panelWidth, 98 * scale);
            graphics.DrawString("Draw a frame", title, white, left + padding, top + 10 * scale);
            graphics.DrawString("Drag on one display. Esc or right-click cancels.", small, soft,
                new RectangleF(left + padding, top + 35 * scale, Math.Max(1, panelWidth - padding * 2), 32 * scale));
            string detail = _selection is { } selected
                ? $"{selected.Width} × {selected.Height} px  ·  X {selected.X}  Y {selected.Y}" +
                  (selected.Width < 100 || selected.Height < 100 ? "  ·  Minimum 100 × 100" : "")
                : $"Display {_display.Number}  ·  Physical pixels  ·  Minimum 100 × 100";
            graphics.DrawString(detail, small, white,
                new RectangleF(left + padding, top + 69 * scale, Math.Max(1, panelWidth - padding * 2), 26 * scale));
        }

        protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            try
            {
                switch (message)
                {
                    case NativeMethods.WmSetCursor:
                        NativeMethods.SetCursor(NativeMethods.LoadCursor(0, IdcCross));
                        return 1;
                    case NativeMethods.WmLButtonDown when _owner._pending is { Task.IsCompleted: false }:
                        if (_owner._dragWindow is not null) return 0;
                        if (!NativeMethods.TryGetMouseMessageScreenPoint(hwnd, lParam, out var start))
                            throw new InvalidOperationException("Windows could not locate the drag's start. Frame selection was canceled.");
                        _start = new Point(start.X, start.Y);
                        if (!_area.Contains(_start)) return 0;
                        _dragging = true;
                        _owner._dragWindow = this;
                        NativeMethods.SetCapture(hwnd);
                        if (GetCapture() != hwnd)
                            throw new InvalidOperationException("Windows could not capture the drag. Frame selection was canceled.");
                        UpdateSelection();
                        return 0;
                    case NativeMethods.WmMouseMove when _owner._dragWindow is { } moving:
                        moving.UpdateSelection();
                        return 0;
                    case NativeMethods.WmLButtonUp when _owner._dragWindow is { } finishing:
                        finishing.UpdateSelection();
                        var selection = finishing._selection;
                        finishing._dragging = false;
                        _owner._dragWindow = null;
                        NativeMethods.ReleaseCapture();
                        _owner.Complete(selection is { Width: >= 100, Height: >= 100 } ? selection : null);
                        return 0;
                    case NativeMethods.WmRButtonDown:
                        return 0;
                    case NativeMethods.WmRButtonUp:
                        _owner.Complete(null);
                        return 0;
                    case NativeMethods.WmCaptureChanged when _dragging:
                        _dragging = false;
                        _owner._dragWindow = null;
                        _owner.Complete(null);
                        return 0;
                    case NativeMethods.WmDisplayChange:
                    case NativeMethods.WmSettingChange when (int)wParam == NativeMethods.SpiSetWorkArea:
                        _owner.Complete(null);
                        return 0;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
            {
                _owner.Fail(ex);
                return 0;
            }
            return base.WndProc(hwnd, message, wParam, lParam);
        }

        private void UpdateSelection()
        {
            if (!_dragging || _owner._pending is not { Task.IsCompleted: false }) return;
            _selection = FrameManipulation.Draw(_start, CursorPosition(), _area);
            QueuePaint();
        }

        private void QueuePaint()
        {
            if (_paintQueued) return;
            _paintQueued = true;
            if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _paintQueued = false;
                if (IsDisposed || _owner._pending is not { Task.IsCompleted: false }) return;
                try { Paint(); }
                catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
                {
                    _owner.Fail(ex);
                }
            })) _paintQueued = false;
        }

        private static Point CursorPosition()
        {
            if (!NativeMethods.GetCursorPos(out var point))
                throw new InvalidOperationException("Windows could not read the pointer location. Frame selection was canceled.");
            return new Point(point.X, point.Y);
        }

        protected override void OnDisposing()
        {
            _dragging = false;
            if (GetCapture() == Handle) NativeMethods.ReleaseCapture();
            base.OnDisposing();
        }
    }
}
