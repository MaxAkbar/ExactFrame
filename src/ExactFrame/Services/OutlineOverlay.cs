using System.Drawing;
using Microsoft.UI.Dispatching;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Interop;
using ExactFrame.Native;

namespace ExactFrame.Services;

/// <summary>
/// Composes the on-screen outline from three kinds of Win32 windows, bottom to top:
/// four shade panels (dimming), guide layers, movable subframe borders and the main draggable border.
/// Windows are created lazily on first use, on the UI thread. While visible, rapid size changes (a slider
/// drag) are coalesced so the outline redraws once per dispatcher pass instead of once per value.
/// </summary>
internal sealed class OutlineOverlay : IOutlineOverlay
{
    private ShadeWindow[]? _shades;
    private GuideWindow? _guides;
    private NestedFramesWindow? _nested;
    private FrameBorderWindow? _border;
    private readonly Dictionary<string, FrameBorderWindow> _nestedMovers = [];
    private EdgeMagnifierWindow? _magnifier;
    private IReadOnlyList<NestedFrameBounds> _nestedFrames = [];
    private OutlineStyle _style = new();
    private DisplayInfo? _display;
    private bool _keepClear;
    private bool _locked;
    private bool _excludeFromCapture = true;
    private bool _disposed;
    private bool _layoutQueued;
    private FrameEditingOptions _editing = new();
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer? _magnifierTimer;

    public OutlineOverlay()
    {
        if (_dispatcher is null) return;
        _magnifierTimer = _dispatcher.CreateTimer();
        _magnifierTimer.Interval = TimeSpan.FromMilliseconds(100);
        _magnifierTimer.IsRepeating = true;
        _magnifierTimer.Tick += OnMagnifierTick;
    }

    public event EventHandler? FrameMoved;

    public event EventHandler? VisibilityChanged;
    public event EventHandler? FrameInteractionStarted;
    public event EventHandler<bool>? FrameInteractionFinished;
    public event EventHandler<NestedFrameInteraction>? NestedFrameInteraction;

    public bool IsVisible { get; private set; }

    public Rectangle Frame { get; private set; }

    public DisplayInfo? Display => IsVisible ? _display : null;

    public bool CaptureExclusionApplied { get; private set; } = true;

    public OutlineStyle Style => _style;

    public void Show(Rectangle frame, DisplayInfo display, bool keepClearOfTaskbar, IReadOnlyList<NestedFrameBounds> nestedFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        _display = display;
        _keepClear = keepClearOfTaskbar;
        Frame = frame;
        bool nestedVisibilityChanged = (_nestedFrames.Count > 0) != (nestedFrames.Count > 0);
        _nestedFrames = nestedFrames;

        if (IsVisible)
        {
            if (nestedVisibilityChanged) ShowLayers();
            else QueueLayout();
            return;
        }

        Layout();
        IsVisible = true;
        ShowLayers();
        ApplyCaptureExclusion();
        UpdateMagnifierTimer();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        if (!IsVisible) return;
        _border?.CancelInteraction();
        foreach (var mover in _nestedMovers.Values.ToArray()) mover.CancelInteraction();
        IsVisible = false;
        foreach (var shade in _shades ?? []) shade.Hide();
        _guides?.Hide();
        _nested?.Hide();
        _border?.Hide();
        foreach (var mover in _nestedMovers.Values) mover.Hide();
        UpdateMagnifierTimer();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyStyle(OutlineStyle style)
    {
        _style = style;
        if (!IsVisible) return;
        Layout();
        ShowLayers();
        UpdateMagnifierTimer();
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (IsVisible) Layout();
    }

    public void SetCaptureExclusion(bool exclude)
    {
        _excludeFromCapture = exclude;
        if (_border is not null) ApplyCaptureExclusion();
    }

    public void SetEditingOptions(FrameEditingOptions options)
    {
        _editing = options;
        if (_display is not null)
            _border?.Configure(_display.UsableArea(_keepClear), _editing, Math.Clamp((int)Math.Round(8 * _display.Scale), 8, FrameBorderWindow.GrabMargin));
    }

    public void CancelFrameInteraction()
    {
        _border?.CancelInteraction();
        foreach (var mover in _nestedMovers.Values.ToArray()) mover.CancelInteraction();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _disposed = true;
        if (_magnifierTimer is not null)
        {
            _magnifierTimer.Stop();
            _magnifierTimer.Tick -= OnMagnifierTick;
        }
        if (_border is not null) _border.Dragged -= OnBorderDragged;
        _border?.Dispose();
        _guides?.Dispose();
        _nested?.Dispose();
        foreach (var mover in _nestedMovers.Values) mover.Dispose();
        _nestedMovers.Clear();
        _magnifier?.Dispose();
        foreach (var shade in _shades ?? []) shade.Dispose();
    }

    private void QueueLayout()
    {
        if (_dispatcher is null)
        {
            Layout();
            return;
        }
        if (_layoutQueued) return;
        _layoutQueued = true;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _layoutQueued = false;
            if (IsVisible && !_disposed) Layout();
        });
    }

    private void EnsureWindows()
    {
        if (_border is not null) return;
        _shades = [new ShadeWindow(), new ShadeWindow(), new ShadeWindow(), new ShadeWindow()];
        _guides = new GuideWindow();
        _nested = new NestedFramesWindow();
        _border = new FrameBorderWindow();
        _magnifier = new EdgeMagnifierWindow();
        _border.Dragged += OnBorderDragged;
        _border.InteractionStarted += (_, _) => FrameInteractionStarted?.Invoke(this, EventArgs.Empty);
        _border.InteractionFinished += (_, canceled) => FrameInteractionFinished?.Invoke(this, canceled);
    }

    private void Layout()
    {
        if (_display is null || _border is null || _guides is null || _nested is null || _shades is null) return;

        _border.Configure(_display.UsableArea(_keepClear), _editing, Math.Clamp((int)Math.Round(8 * _display.Scale), 8, FrameBorderWindow.GrabMargin));
        _border.Update(Frame, _style, _locked);
        if (GuideWindow.IsNeeded(_style)) _guides.Update(Frame, _display, _style);
        if (_nestedFrames.Count > 0) _nested.Update(Frame, _nestedFrames, _style, _display.Scale);
        LayoutNestedMovers();

        var d = _display.Bounds;
        var f = Frame;
        bool dim = _style.DimOutside;
        byte alpha = (byte)Math.Round(Math.Clamp(_style.DimOpacityPercent, 0, 100) * 2.55);
        foreach (var shade in _shades) shade.SetOpacity(alpha);

        bool visible = IsVisible && dim;
        _shades[0].Place(Rectangle.FromLTRB(d.Left, d.Top, d.Right, Math.Min(f.Top, d.Bottom)), visible);
        _shades[1].Place(Rectangle.FromLTRB(d.Left, Math.Max(f.Bottom, d.Top), d.Right, d.Bottom), visible);
        _shades[2].Place(Rectangle.FromLTRB(d.Left, Math.Max(f.Top, d.Top), Math.Min(f.Left, d.Right), Math.Min(f.Bottom, d.Bottom)), visible);
        _shades[3].Place(Rectangle.FromLTRB(Math.Max(f.Right, d.Left), Math.Max(f.Top, d.Top), d.Right, Math.Min(f.Bottom, d.Bottom)), visible);
    }

    private void LayoutNestedMovers()
    {
        bool changed = false;
        var frames = _nestedFrames.Where(frame => frame.Id.Length > 0 && !frame.Bounds.IsEmpty &&
            frame.MovementArea.Contains(frame.Bounds)).ToList();
        foreach (string id in _nestedMovers.Keys.Where(id => !frames.Any(frame => frame.Id == id)).ToArray())
        {
            var removed = _nestedMovers[id]; _nestedMovers.Remove(id); removed.Dispose(); changed = true;
        }
        foreach (var frame in frames)
        {
            if (!_nestedMovers.TryGetValue(frame.Id, out var mover))
            {
                string id = frame.Id;
                mover = new FrameBorderWindow(moveOnly: true, name: $"ExactFrame subframe: {frame.Name}");
                _nestedMovers.Add(id, mover); changed = true;
                mover.InteractionStarted += (_, _) => NestedFrameInteraction?.Invoke(this, new(id, Rectangle.Empty, NestedFrameInteractionKind.Started));
                mover.Dragged += (_, bounds) => NestedFrameInteraction?.Invoke(this, new(id, bounds, NestedFrameInteractionKind.Moved));
                mover.InteractionFinished += (_, canceled) => NestedFrameInteraction?.Invoke(this,
                    new(id, Rectangle.Empty, canceled ? NestedFrameInteractionKind.Canceled : NestedFrameInteractionKind.Completed));
            }
            mover.Configure(frame.MovementArea, new FrameEditingOptions(), 8);
            mover.Update(frame.Bounds, _style with { Color = frame.Color }, _locked);
        }
        if (changed) ApplyCaptureExclusion();
        if (IsVisible)
        {
            foreach (var mover in _nestedMovers.Values) mover.Show();
            _border?.Show();
        }
    }

    /// <summary>Shows the layers in z-order so the border ends up on top.</summary>
    private void ShowLayers()
    {
        if (_border is null || _guides is null || _nested is null) return;
        Layout();
        if (GuideWindow.IsNeeded(_style)) _guides.Show();
        else _guides.Hide();
        if (_nestedFrames.Count > 0) _nested.Show();
        else _nested.Hide();
        foreach (var mover in _nestedMovers.Values) mover.Show();
        _border.Show();
    }

    private void ApplyCaptureExclusion()
    {
        if (_border is null || _guides is null || _nested is null || _shades is null) return;
        bool ok = _border.SetCaptureExclusion(_excludeFromCapture);
        ok &= _guides.SetCaptureExclusion(_excludeFromCapture);
        ok &= _nested.SetCaptureExclusion(_excludeFromCapture);
        foreach (var mover in _nestedMovers.Values) ok &= mover.SetCaptureExclusion(_excludeFromCapture);
        if (_magnifier is not null) ok &= _magnifier.SetCaptureExclusion(_excludeFromCapture);
        foreach (var shade in _shades) ok &= shade.SetCaptureExclusion(_excludeFromCapture);
        CaptureExclusionApplied = !_excludeFromCapture || ok;
    }

    private void UpdateMagnifierTimer()
    {
        if (_magnifierTimer is null) return;
        _magnifierTimer.Stop();
        if (!IsVisible || !_style.ShowEdgeMagnifier)
        {
            _magnifier?.Hide();
            return;
        }

        _magnifierTimer.Start();
        UpdateMagnifier();
    }

    private void OnMagnifierTick(DispatcherQueueTimer sender, object args) => UpdateMagnifier();

    private void UpdateMagnifier()
    {
        if (!IsVisible || !_style.ShowEdgeMagnifier || _magnifier is null || _display is null ||
            !NativeMethods.GetCursorPos(out var cursor))
        {
            _magnifier?.Hide();
            return;
        }

        var pointer = new Point(cursor.X, cursor.Y);
        if (!EdgeMagnifierGeometry.TryNearestEdge(Frame, pointer, 14, out var edge))
        {
            _magnifier.Hide();
            return;
        }

        try
        {
            _magnifier.Update(Frame, pointer, edge, _display.Bounds);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                   System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            // Screen pixels may be temporarily unavailable (for example on a secure desktop).
            _magnifier.Hide();
        }
    }

    private void OnBorderDragged(object? sender, Rectangle proposed)
    {
        if (_display is null) return;
        var area = _display.UsableArea(_keepClear);
        if (proposed.Width > area.Width || proposed.Height > area.Height) return;

        var frame = FrameGeometry.ClampPosition(proposed, area);
        if (frame == Frame) return;
        int dx = frame.X - Frame.X;
        int dy = frame.Y - Frame.Y;
        _nestedFrames = [.. _nestedFrames.Select(item => item with
        {
            Bounds = new Rectangle(item.Bounds.X + dx, item.Bounds.Y + dy, item.Bounds.Width, item.Bounds.Height),
            MovementArea = new Rectangle(item.MovementArea.X + dx, item.MovementArea.Y + dy, item.MovementArea.Width, item.MovementArea.Height),
            SafeArea = item.SafeArea.IsEmpty ? Rectangle.Empty : new Rectangle(item.SafeArea.X + dx, item.SafeArea.Y + dy, item.SafeArea.Width, item.SafeArea.Height)
        })];
        Frame = frame;
        QueueLayout();
        FrameMoved?.Invoke(this, EventArgs.Empty);
    }
}
