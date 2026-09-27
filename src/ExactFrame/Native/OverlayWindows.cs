using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ExactFrame.Core.Models;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// The draggable border. Its window extends <see cref="GrabMargin"/> pixels past the frame so the edge is easy
/// to grab; that margin is drawn at alpha 1 (invisible) and lies outside the recorded rectangle. The colored
/// border itself is drawn inside the frame, so the frame's outer edges are the capture rectangle.
/// </summary>
internal sealed class FrameBorderWindow : LayeredWindow
{
    public const int GrabMargin = 6;

    private Rectangle _frame;
    private OutlineStyle _style = new();
    private bool _locked;
    private bool _dragging;
    private Point _dragOffset;

    public FrameBorderWindow() : base(clickThrough: false)
    {
    }

    /// <summary>Raised continuously while the user drags, with the proposed (unclamped) frame.</summary>
    public event EventHandler<Rectangle>? Dragged;

    public void Update(Rectangle frame, OutlineStyle style, bool locked)
    {
        bool redraw = frame.Size != _frame.Size || style != _style || locked != _locked || Bounds.IsEmpty;
        _frame = frame;
        _style = style;
        if (locked != _locked)
        {
            _locked = locked;
            _dragging = false;
            NativeMethods.ReleaseCapture();
            SetClickThrough(locked);
        }

        var bounds = Rectangle.Inflate(frame, GrabMargin, GrabMargin);
        if (redraw) Render(bounds, Draw);
        else MoveTo(bounds.Location);
    }

    private void Draw(Graphics graphics)
    {
        var frame = new Rectangle(GrabMargin, GrabMargin, _frame.Width, _frame.Height);
        var outer = Rectangle.Inflate(frame, GrabMargin, GrabMargin);

        if (!_locked)
        {
            using var grab = new SolidBrush(Color.FromArgb(1, 0, 0, 0));
            FillRing(graphics, grab, outer, frame);
        }

        // A faint dark hairline just outside the frame keeps a light outline visible on white backgrounds.
        using (var halo = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            FillRing(graphics, halo, Rectangle.Inflate(frame, 1, 1), frame);

        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)OutlinePalette.Argb(_style.Color))));
        int t = Math.Clamp(_style.Thickness, 1, Math.Max(1, Math.Min(frame.Width, frame.Height) / 4));
        switch (_style.Line)
        {
            case OutlineLine.Dashed:
                DrawDashed(graphics, brush, frame, t);
                break;
            case OutlineLine.Corners:
                DrawCorners(graphics, brush, frame, t);
                break;
            default:
                FillRing(graphics, brush, frame, Rectangle.Inflate(frame, -t, -t));
                break;
        }
    }

    internal static void FillRing(Graphics graphics, Brush brush, Rectangle outer, Rectangle inner)
    {
        graphics.FillRectangle(brush, outer.Left, outer.Top, outer.Width, inner.Top - outer.Top);
        graphics.FillRectangle(brush, outer.Left, inner.Bottom, outer.Width, outer.Bottom - inner.Bottom);
        graphics.FillRectangle(brush, outer.Left, inner.Top, inner.Left - outer.Left, inner.Height);
        graphics.FillRectangle(brush, inner.Right, inner.Top, outer.Right - inner.Right, inner.Height);
    }

    internal static void DrawDashed(Graphics graphics, Brush brush, Rectangle r, int t)
    {
        int dash = Math.Max(10, t * 4);
        int gap = Math.Max(6, t * 3);
        for (int x = r.Left; x < r.Right; x += dash + gap)
        {
            int w = Math.Min(dash, r.Right - x);
            graphics.FillRectangle(brush, x, r.Top, w, t);
            graphics.FillRectangle(brush, x, r.Bottom - t, w, t);
        }
        for (int y = r.Top; y < r.Bottom; y += dash + gap)
        {
            int h = Math.Min(dash, r.Bottom - y);
            graphics.FillRectangle(brush, r.Left, y, t, h);
            graphics.FillRectangle(brush, r.Right - t, y, t, h);
        }
    }

    internal static void DrawCorners(Graphics graphics, Brush brush, Rectangle r, int t)
    {
        int length = Math.Min(Math.Max(28, t * 10), Math.Min(r.Width, r.Height) / 3);
        graphics.FillRectangle(brush, r.Left, r.Top, length, t);
        graphics.FillRectangle(brush, r.Left, r.Top, t, length);
        graphics.FillRectangle(brush, r.Right - length, r.Top, length, t);
        graphics.FillRectangle(brush, r.Right - t, r.Top, t, length);
        graphics.FillRectangle(brush, r.Left, r.Bottom - t, length, t);
        graphics.FillRectangle(brush, r.Left, r.Bottom - length, t, length);
        graphics.FillRectangle(brush, r.Right - length, r.Bottom - t, length, t);
        graphics.FillRectangle(brush, r.Right - t, r.Bottom - length, t, length);
    }

    protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmSetCursor when !_locked:
                NativeMethods.SetCursor(NativeMethods.LoadCursor(0, NativeMethods.IdcSizeAll));
                return 1;

            case NativeMethods.WmLButtonDown when !_locked:
                NativeMethods.GetCursorPos(out var start);
                _dragOffset = new Point(start.X - _frame.X, start.Y - _frame.Y);
                _dragging = true;
                NativeMethods.SetCapture(hwnd);
                return 0;

            case NativeMethods.WmMouseMove when _dragging:
                NativeMethods.GetCursorPos(out var cursor);
                Dragged?.Invoke(this, new Rectangle(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y, _frame.Width, _frame.Height));
                return 0;

            case NativeMethods.WmLButtonUp:
                _dragging = false;
                NativeMethods.ReleaseCapture();
                return 0;

            case NativeMethods.WmCaptureChanged:
                _dragging = false;
                break;
        }
        return base.WndProc(hwnd, message, wParam, lParam);
    }
}

/// <summary>Click-through outlines for every frame nested inside the draggable main frame.</summary>
internal sealed class NestedFramesWindow : LayeredWindow
{
    private IReadOnlyList<NestedFrameBounds> _frames = [];
    private OutlineStyle _style = new();
    private double _scale = 1;

    public NestedFramesWindow() : base(clickThrough: true)
    {
    }

    public void Update(Rectangle parent, IReadOnlyList<NestedFrameBounds> frames, OutlineStyle style, double scale)
    {
        var relative = frames.Select(frame => frame with
        {
            Bounds = new Rectangle(frame.Bounds.X - parent.X, frame.Bounds.Y - parent.Y,
                frame.Bounds.Width, frame.Bounds.Height)
        }).ToArray();
        bool redraw = Bounds.Size != parent.Size || style != _style || Math.Abs(scale - _scale) > 0.001 ||
            !_frames.SequenceEqual(relative);
        _frames = relative;
        _style = style;
        _scale = scale;
        if (redraw) Render(parent, Draw);
        else MoveTo(parent.Location);
    }

    private void Draw(Graphics graphics)
    {
        foreach (var frame in _frames)
        {
            var bounds = frame.Bounds;
            if (bounds.Width < 2 || bounds.Height < 2) continue;
            var color = Color.FromArgb(unchecked((int)OutlinePalette.Argb(frame.Color)));
            using var brush = new SolidBrush(color);
            int thickness = Math.Clamp(_style.Thickness, 1, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 4));
            switch (_style.Line)
            {
                case OutlineLine.Dashed:
                    FrameBorderWindow.DrawDashed(graphics, brush, bounds, thickness);
                    break;
                case OutlineLine.Corners:
                    FrameBorderWindow.DrawCorners(graphics, brush, bounds, thickness);
                    break;
                default:
                    FrameBorderWindow.FillRing(graphics, brush, bounds, Rectangle.Inflate(bounds, -thickness, -thickness));
                    break;
            }

            if (_style.ShowSizeLabel) DrawLabel(graphics, frame, color, thickness);
        }
    }

    private void DrawLabel(Graphics graphics, NestedFrameBounds frame, Color color, int thickness)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        string text = $"{frame.Name}  {frame.Bounds.Width} × {frame.Bounds.Height}";
        using var font = new Font("Segoe UI Semibold", (float)(12 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        int height = (int)Math.Round(22 * _scale);
        int padding = (int)Math.Round(6 * _scale);
        int width = Math.Min(frame.Bounds.Width - thickness * 2,
            (int)Math.Ceiling(graphics.MeasureString(text, font).Width) + padding * 2);
        if (width <= 0 || frame.Bounds.Height < height + thickness) return;
        var box = new Rectangle(frame.Bounds.Left + thickness, frame.Bounds.Top + thickness, width, height);
        using var fill = new SolidBrush(color);
        using var ink = new SolidBrush(Color.FromArgb(255, 6, 32, 28));
        graphics.FillRectangle(fill, box);
        var state = graphics.Save();
        graphics.SetClip(box);
        graphics.DrawString(text, font, ink, box.Left + padding, box.Top + (height - font.Height) / 2f);
        graphics.Restore(state);
    }
}

/// <summary>Click-through layer with the guides inside the frame and the size label.</summary>
internal sealed class GuideWindow : LayeredWindow
{
    private Rectangle _frame;
    private OutlineStyle _style = new();
    private bool _labelAbove;
    private double _scale = 1;

    public GuideWindow() : base(clickThrough: true)
    {
    }

    public static bool IsNeeded(OutlineStyle style) => style.HasGuides || style.ShowSizeLabel;

    public void Update(Rectangle frame, DisplayInfo display, OutlineStyle style)
    {
        int labelHeight = LabelHeight(display.Scale);
        bool labelAbove = frame.Top - display.Bounds.Top >= labelHeight + 2;
        var bounds = labelAbove && style.ShowSizeLabel
            ? Rectangle.FromLTRB(frame.Left, frame.Top - labelHeight, frame.Right, frame.Bottom)
            : frame;

        bool redraw = frame.Size != _frame.Size || style != _style || labelAbove != _labelAbove ||
                      Math.Abs(display.Scale - _scale) > 0.001 || Bounds.Size != bounds.Size;
        _frame = frame;
        _style = style;
        _labelAbove = labelAbove;
        _scale = display.Scale;

        if (redraw) Render(bounds, g => Draw(g, new Point(frame.X - bounds.X, frame.Y - bounds.Y)));
        else MoveTo(bounds.Location);
    }

    private static int LabelHeight(double scale) => (int)Math.Round(22 * scale);

    private void Draw(Graphics graphics, Point offset)
    {
        graphics.SmoothingMode = SmoothingMode.None;
        var frame = new Rectangle(offset, _frame.Size);
        uint argb = OutlinePalette.Argb(_style.Color);
        var solid = Color.FromArgb(unchecked((int)argb));
        var soft = Color.FromArgb(120, solid);
        float line = (float)Math.Max(1, Math.Round(_scale));

        if (_style.ShowThirds)
        {
            using var pen = new Pen(soft, line);
            for (int i = 1; i <= 2; i++)
            {
                float x = frame.Left + frame.Width * i / 3f;
                float y = frame.Top + frame.Height * i / 3f;
                graphics.DrawLine(pen, x, frame.Top, x, frame.Bottom);
                graphics.DrawLine(pen, frame.Left, y, frame.Right, y);
            }
        }

        if (_style.ShowCenterMark)
        {
            using var pen = new Pen(Color.FromArgb(230, solid), line);
            float arm = (float)(12 * _scale);
            float cx = frame.Left + frame.Width / 2f, cy = frame.Top + frame.Height / 2f;
            graphics.DrawLine(pen, cx - arm, cy, cx + arm, cy);
            graphics.DrawLine(pen, cx, cy - arm, cx, cy + arm);
        }

        if (_style.ShowSafeArea)
        {
            using var pen = new Pen(soft, line) { DashStyle = DashStyle.Dash };
            var safe = Rectangle.Inflate(frame, -(int)Math.Round(frame.Width * 0.05), -(int)Math.Round(frame.Height * 0.05));
            graphics.DrawRectangle(pen, safe);
        }

        if (_style.ShowSizeLabel) DrawLabel(graphics, frame, solid);
    }

    private void DrawLabel(Graphics graphics, Rectangle frame, Color fill)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        string text = $"{_frame.Width} × {_frame.Height}";
        using var font = new Font("Segoe UI Semibold", (float)(12 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        var textSize = graphics.MeasureString(text, font);
        int height = LabelHeight(_scale);
        int padding = (int)Math.Round(8 * _scale);
        int width = (int)Math.Ceiling(textSize.Width) + padding * 2;
        int inset = _labelAbove ? 0 : _style.Thickness + (int)Math.Round(4 * _scale);
        var box = new Rectangle(frame.Left + inset, _labelAbove ? frame.Top - height : frame.Top + inset,
            Math.Min(width, frame.Width - inset), height);

        using var path = RoundedTop(box, (int)Math.Round(4 * _scale), roundBottom: !_labelAbove);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 6, 32, 28));
        graphics.DrawString(text, font, textBrush, box.Left + padding, box.Top + (height - textSize.Height) / 2);
    }

    private static GraphicsPath RoundedTop(Rectangle r, int radius, bool roundBottom)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        if (roundBottom)
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        else
        {
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        }
        path.CloseFigure();
        return path;
    }
}

/// <summary>A click-through, semi-transparent black rectangle. Four of them dim everything outside the frame.</summary>
internal sealed class ShadeWindow : NativeWindow
{
    private byte _alpha;

    public ShadeWindow()
    {
        CreateHandle(NativeMethods.WsExLayered | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow |
                     NativeMethods.WsExNoActivate | NativeMethods.WsExTopmost,
            NativeMethods.WsPopup, blackBackground: true);
        SetOpacity(115);
    }

    public void SetOpacity(byte alpha)
    {
        if (alpha == _alpha) return;
        _alpha = alpha;
        NativeMethods.SetLayeredWindowAttributes(Handle, 0, alpha, NativeMethods.LwaAlpha);
    }

    public void Place(Rectangle bounds, bool visible)
    {
        if (!visible || bounds.Width <= 0 || bounds.Height <= 0)
        {
            NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
            return;
        }
        // Keep the z-order set when the overlay was shown: shades stay below the guides and the border.
        NativeMethods.SetWindowPos(Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void Hide() => NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);

    public bool SetCaptureExclusion(bool exclude) =>
        NativeMethods.SetWindowDisplayAffinity(Handle, exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);

    protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam) => message switch
    {
        NativeMethods.WmMouseActivate => NativeMethods.MaNoActivate,
        NativeMethods.WmDpiChanged => 0,
        _ => base.WndProc(hwnd, message, wParam, lParam)
    };
}
