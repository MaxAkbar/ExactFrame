using System.Drawing;
using Microsoft.UI.Dispatching;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Native;

namespace ExactFrame.Services;

/// <summary>
/// Composes the on-screen outline from three kinds of Win32 windows, bottom to top:
/// four shade panels (dimming), the guide layer (guides and size label) and the draggable border.
/// Windows are created lazily on first use, on the UI thread. While visible, rapid size changes (a slider
/// drag) are coalesced so the outline redraws once per dispatcher pass instead of once per value.
/// </summary>
internal sealed class OutlineOverlay : IOutlineOverlay
{
    private ShadeWindow[]? _shades;
    private GuideWindow? _guides;
    private FrameBorderWindow? _border;
    private OutlineStyle _style = new();
    private DisplayInfo? _display;
    private bool _keepClear;
    private bool _locked;
    private bool _excludeFromCapture = true;
    private bool _disposed;
    private bool _layoutQueued;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    public event EventHandler? FrameMoved;

    public event EventHandler? VisibilityChanged;

    public bool IsVisible { get; private set; }

    public Rectangle Frame { get; private set; }

    public DisplayInfo? Display => IsVisible ? _display : null;

    public bool CaptureExclusionApplied { get; private set; } = true;

    public OutlineStyle Style => _style;

    public void Show(Rectangle frame, DisplayInfo display, bool keepClearOfTaskbar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        _display = display;
        _keepClear = keepClearOfTaskbar;
        Frame = frame;

        if (IsVisible)
        {
            QueueLayout();
            return;
        }

        Layout();
        IsVisible = true;
        ShowLayers();
        ApplyCaptureExclusion();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        foreach (var shade in _shades ?? []) shade.Hide();
        _guides?.Hide();
        _border?.Hide();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyStyle(OutlineStyle style)
    {
        _style = style;
        if (!IsVisible) return;
        Layout();
        ShowLayers();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_border is not null) _border.Dragged -= OnBorderDragged;
        _border?.Dispose();
        _guides?.Dispose();
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
        _border = new FrameBorderWindow();
        _border.Dragged += OnBorderDragged;
    }

    private void Layout()
    {
        if (_display is null || _border is null || _guides is null || _shades is null) return;

        _border.Update(Frame, _style, _locked);
        if (GuideWindow.IsNeeded(_style)) _guides.Update(Frame, _display, _style);

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

    /// <summary>Shows the layers in z-order so the border ends up on top.</summary>
    private void ShowLayers()
    {
        if (_border is null || _guides is null) return;
        Layout();
        if (GuideWindow.IsNeeded(_style)) _guides.Show();
        else _guides.Hide();
        _border.Show();
    }

    private void ApplyCaptureExclusion()
    {
        if (_border is null || _guides is null || _shades is null) return;
        bool ok = _border.SetCaptureExclusion(_excludeFromCapture);
        ok &= _guides.SetCaptureExclusion(_excludeFromCapture);
        foreach (var shade in _shades) ok &= shade.SetCaptureExclusion(_excludeFromCapture);
        CaptureExclusionApplied = !_excludeFromCapture || ok;
    }

    private void OnBorderDragged(object? sender, Rectangle proposed)
    {
        if (_display is null) return;
        var area = _display.UsableArea(_keepClear);
        if (proposed.Width > area.Width || proposed.Height > area.Height) return;

        var frame = FrameGeometry.ClampPosition(proposed, area);
        if (frame == Frame) return;
        Frame = frame;
        Layout();
        FrameMoved?.Invoke(this, EventArgs.Empty);
    }
}
