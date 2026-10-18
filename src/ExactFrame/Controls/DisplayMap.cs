using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.ViewModels;
using ExactFrame.Helpers;
using DRect = System.Drawing.Rectangle;
using DPoint = System.Drawing.Point;
using Shapes = Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI.Core;

namespace ExactFrame.Controls;

/// <summary>
/// The to-scale preview of the selected display: wallpaper, taskbar, the frame with its style (dimming,
/// guides, label, handles) or, in Resize window mode, the window's current and target rectangles.
/// Redrawn from a <see cref="PreviewState"/> whenever it or the control's size changes.
/// </summary>
public sealed class DisplayMap : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(object), typeof(DisplayMap), new PropertyMetadata(null, (d, _) => ((DisplayMap)d).OnStateChanged()));

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(object), typeof(DisplayMap), new PropertyMetadata(null,
            (d, e) => ((DisplayMap)d).OnEditorChanged(e.OldValue as MainViewModel)));

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Border _focusBorder;
    private double _scale;
    private double _originX;
    private double _originY;
    private DRect _display;
    private MainViewModel? _gestureEditor;
    private DRect _original;
    private DRect _expected;
    private DRect _gestureArea;
    private DRect _gestureDisplay;
    private DPoint _start;
    private FrameHandle _handle;
    private uint _pointerId;
    private int _editRevision;
    private bool _drawing;
    private bool _ending;
    private bool _subscribed;
    private bool _drawIsValid;
    private DRect? _drawPreview;
    private InputSystemCursorShape _cursorShape = InputSystemCursorShape.Arrow;

    public DisplayMap()
    {
        _focusBorder = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(5),
            Child = _canvas
        };
        Content = _focusBorder;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        AutomationProperties.SetName(this, "Recording frame preview");
        AutomationProperties.SetHelpText(this,
            "Drag inside the frame to move it, drag an edge or corner to resize, or drag outside it to draw a new frame. " +
            "Arrow keys move one physical pixel; Shift moves ten. Control and arrow keys resize. Escape cancels a drag.");
        SizeChanged += (_, _) => OnLayoutChanged();
        _canvas.SizeChanged += (_, _) => OnLayoutChanged();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += (_, _) => CancelInteraction();
        PointerCaptureLost += (_, _) => CancelInteraction();
        KeyDown += OnKeyDown;
        GotFocus += (_, _) => _focusBorder.BorderBrush = Format.BrushFromHex("#34D5B5");
        LostFocus += (_, _) =>
        {
            CancelInteraction();
            _focusBorder.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        };
        Loaded += (_, _) => SubscribeEditor();
        Unloaded += (_, _) =>
        {
            CancelInteraction();
            UnsubscribeEditor(Editor as MainViewModel);
        };
    }

    /// <summary>A <see cref="PreviewState"/>.</summary>
    public object? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The view model that accepts physical-pixel edits, or null for a read-only map.</summary>
    public object? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public bool IsInteracting => _gestureEditor is not null;

    /// <summary>Restores the frame that was present before the current pointer gesture.</summary>
    public void CancelInteraction() => FinishInteraction(cancel: true);

    private void OnEditorChanged(MainViewModel? oldEditor)
    {
        CancelInteraction();
        UnsubscribeEditor(oldEditor);
        if (IsLoaded) SubscribeEditor();
        UpdateEditability();
    }

    private void OnLayoutChanged()
    {
        CancelInteraction();
        Redraw();
    }

    private void SubscribeEditor()
    {
        if (_subscribed || Editor is not MainViewModel editor) return;
        editor.PropertyChanged += OnEditorPropertyChanged;
        _subscribed = true;
        UpdateEditability();
    }

    private void UnsubscribeEditor(MainViewModel? editor)
    {
        if (!_subscribed || editor is null) return;
        editor.PropertyChanged -= OnEditorPropertyChanged;
        _subscribed = false;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Editor is not MainViewModel editor) return;
        if (IsInteracting && (!editor.CanEditFrame || editor.SelectedFrameArea != _gestureArea ||
            editor.FrameEditRevision != _editRevision))
            CancelInteraction();
        if (e.PropertyName is nameof(MainViewModel.CanEditFrame) or "" or null)
            UpdateEditability();
    }

    private void UpdateEditability()
    {
        bool editable = Editor is MainViewModel { CanEditFrame: true } && State is PreviewState { IsResizeMode: false };
        IsTabStop = editable;
        AutomationProperties.SetName(this, editable ? "Recording frame preview editor" : "Recording frame preview");
    }

    private void OnStateChanged()
    {
        if (IsInteracting && (State is not PreviewState state ||
            state.IsResizeMode || state.DisplayBounds != _gestureDisplay || state.Frame != _expected))
            CancelInteraction();
        UpdateEditability();
        Redraw();
    }

    private bool CanInteract(out MainViewModel editor)
    {
        editor = Editor as MainViewModel ?? null!;
        return editor is { CanEditFrame: true } && State is PreviewState { IsResizeMode: false } && _scale > 0 &&
            editor.SelectedFrameArea.Width >= MainViewModel.MinSize && editor.SelectedFrameArea.Height >= MainViewModel.MinSize;
    }

    private bool IsOnDisplay(Windows.Foundation.Point point) =>
        point.X >= _originX && point.Y >= _originY &&
        point.X <= _originX + _display.Width * _scale && point.Y <= _originY + _display.Height * _scale;

    private DPoint PhysicalPoint(Windows.Foundation.Point point) => new(
        _display.Left + (int)Math.Round((point.X - _originX) / _scale),
        _display.Top + (int)Math.Round((point.Y - _originY) / _scale));

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (point.Properties.IsRightButtonPressed && IsInteracting)
        {
            CancelInteraction();
            e.Handled = true;
            return;
        }
        if (IsInteracting || !point.Properties.IsLeftButtonPressed || !CanInteract(out var editor) ||
            !IsOnDisplay(point.Position)) return;

        Focus(FocusState.Pointer);
        _original = ((PreviewState)State!).Frame;
        _expected = _original;
        _start = PhysicalPoint(point.Position);
        _handle = FrameManipulation.HitTest(_original, _start, Math.Max(1, (int)Math.Ceiling(8 / _scale)), includeInterior: true);
        _drawing = _handle == FrameHandle.None;
        _drawPreview = null;
        _drawIsValid = !_drawing;
        _gestureArea = editor.SelectedFrameArea;
        _gestureDisplay = _display;
        _pointerId = e.Pointer.PointerId;
        editor.BeginFrameInteraction();
        _editRevision = editor.FrameEditRevision;
        _gestureEditor = editor;
        // Capture the stable control, never one of the shapes replaced by Redraw.
        if (!CapturePointer(e.Pointer))
        {
            CancelInteraction();
            return;
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (!IsInteracting || e.Pointer.PointerId != _pointerId)
        {
            UpdatePointerCursor(point.Position);
            return;
        }
        UpdateInteraction(PhysicalPoint(point.Position));
        e.Handled = true;
    }

    private void UpdateInteraction(DPoint current)
    {
        if (_gestureEditor is not { } editor || !editor.CanEditFrame) return;
        DRect frame;
        if (_drawing)
        {
            frame = FrameManipulation.Draw(_start, current, _gestureArea);
            _drawPreview = frame;
            _drawIsValid = frame.Width >= MainViewModel.MinSize && frame.Height >= MainViewModel.MinSize;
            if (!_drawIsValid)
            {
                Redraw();
                return;
            }
        }
        else
        {
            frame = FrameManipulation.Drag(_original, _start, current, _handle, _gestureArea,
                aspectRatio: editor.FrameEditAspectRatio,
                snapTargets: editor.SnapToEdges ? editor.FrameSnapTargets : null,
                snapDistance: editor.SnapToEdges ? Math.Max(1, (int)Math.Ceiling(6 / _scale)) : 0);
        }
        _expected = frame;
        editor.SetFrameFromInteraction(frame, freeform: _drawing);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsInteracting || e.Pointer.PointerId != _pointerId) return;
        UpdateInteraction(PhysicalPoint(e.GetCurrentPoint(_canvas).Position));
        FinishInteraction(cancel: _drawing && !_drawIsValid);
        e.Handled = true;
    }

    private void FinishInteraction(bool cancel)
    {
        if (_ending || _gestureEditor is not { } editor) return;
        _ending = true;
        _gestureEditor = null;
        try
        {
            ReleasePointerCaptures();
            editor.FinishFrameInteraction(cancel);
        }
        finally
        {
            _ending = false;
            _drawing = false;
            _drawPreview = null;
            SetPointerCursor(InputSystemCursorShape.Arrow);
            Redraw();
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && IsInteracting)
        {
            CancelInteraction();
            e.Handled = true;
            return;
        }
        if (IsInteracting || FocusState == FocusState.Unfocused || !CanInteract(out var editor)) return;
        int step = IsKeyDown(VirtualKey.Shift) ? 10 : 1;
        int dx = e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0;
        int dy = e.Key == VirtualKey.Up ? -step : e.Key == VirtualKey.Down ? step : 0;
        if (dx == 0 && dy == 0 || IsKeyDown(VirtualKey.Menu)) return;
        editor.NudgeFrame(dx, dy, resize: IsKeyDown(VirtualKey.Control));
        e.Handled = true;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void UpdatePointerCursor(Windows.Foundation.Point position)
    {
        var shape = InputSystemCursorShape.Arrow;
        if (CanInteract(out _) && IsOnDisplay(position) && State is PreviewState state)
        {
            var handle = FrameManipulation.HitTest(state.Frame, PhysicalPoint(position),
                Math.Max(1, (int)Math.Ceiling(8 / _scale)), includeInterior: true);
            shape = handle switch
            {
                FrameHandle.Move => InputSystemCursorShape.SizeAll,
                FrameHandle.Left or FrameHandle.Right => InputSystemCursorShape.SizeWestEast,
                FrameHandle.Top or FrameHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
                FrameHandle.TopLeft or FrameHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
                FrameHandle.TopRight or FrameHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
                _ => InputSystemCursorShape.Cross
            };
        }
        SetPointerCursor(shape);
    }

    private void SetPointerCursor(InputSystemCursorShape shape)
    {
        if (_cursorShape == shape) return;
        _cursorShape = shape;
        ProtectedCursor = InputSystemCursor.Create(shape);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        if (State is not PreviewState state || _canvas.ActualWidth <= 1 || _canvas.ActualHeight <= 1) return;

        _display = state.DisplayBounds;
        _scale = Math.Min(_canvas.ActualWidth / _display.Width, _canvas.ActualHeight / _display.Height);
        double width = _display.Width * _scale, height = _display.Height * _scale;
        _originX = Math.Round((_canvas.ActualWidth - width) / 2);
        _originY = 0;
        _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(_originX, _originY, width, height) };

        AddBox(_originX, _originY, width, height, "#14272C", "#2A4147", 1, 8);
        if (!state.IsResizeMode) DrawDesktop();
        DrawTaskbar(state);

        if (state.IsResizeMode) DrawResize(state);
        else DrawOutline(state);

        if (_drawing && _drawPreview is { Width: > 0, Height: > 0 } draft)
        {
            var (x, y, w, h) = Map(draft);
            AddOutline(x, y, w, h, Format.BrushFromHex(_drawIsValid ? "#34D5B5" : "#F5B84A"), 2, dashed: true);
            AddText($"{draft.Width} × {draft.Height}" + (_drawIsValid ? "" : " · minimum 100 × 100"),
                x + 5, y + 5, _drawIsValid ? "#34D5B5" : "#F5B84A", 11);
        }
    }

    // ---- Scene --------------------------------------------------------------------------------

    private void DrawDesktop()
    {
        double w = _display.Width * _scale, h = _display.Height * _scale;
        var editor = AddBox(_originX + w * 0.05, _originY + h * 0.08, w * 0.56, h * 0.62, "#1A3137", "#24414A", 1, 5);
        AddBox(_originX + w * 0.05, _originY + h * 0.08, w * 0.56, 12, "#203C45", null, 0, 5);
        double[] lines = [0.38, 0.62, 0.54, 0.7, 0.3, 0.58, 0.44];
        for (int i = 0; i < lines.Length; i++)
        {
            AddBox(_originX + w * 0.05 + 18, _originY + h * 0.08 + 28 + i * 13, w * 0.56 * lines[i] * 0.85, 5,
                i % 4 == 0 ? "#2F5963" : "#29494F", null, 0, 3);
        }
        _ = editor;
        AddBox(_originX + w * 0.64, _originY + h * 0.2, w * 0.31, h * 0.48, "#182D33", "#223D45", 1, 5);
        AddBox(_originX + w * 0.64, _originY + h * 0.2, w * 0.31, 12, "#1E3840", null, 0, 5);
    }

    private void DrawTaskbar(PreviewState state)
    {
        var bounds = state.DisplayBounds;
        var work = state.WorkArea;
        DRect bar;
        if (work.Bottom < bounds.Bottom) bar = DRect.FromLTRB(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom);
        else if (work.Top > bounds.Top) bar = DRect.FromLTRB(bounds.Left, bounds.Top, bounds.Right, work.Top);
        else if (work.Left > bounds.Left) bar = DRect.FromLTRB(bounds.Left, bounds.Top, work.Left, bounds.Bottom);
        else if (work.Right < bounds.Right) bar = DRect.FromLTRB(work.Right, bounds.Top, bounds.Right, bounds.Bottom);
        else return; // auto-hidden taskbar

        var (x, y, w, h) = Map(bar);
        AddBox(x, y, Math.Max(w, 2), Math.Max(h, 2), "#0A1619", null, 0, 0);

        bool horizontal = bar.Width >= bar.Height;
        double icon = Math.Clamp((horizontal ? h : w) * 0.5, 3, 8);
        for (int i = -2; i <= 2; i++)
        {
            double cx = horizontal ? x + w / 2 + i * (icon + 4) : x + w / 2;
            double cy = horizontal ? y + h / 2 : y + h / 2 + i * (icon + 4);
            AddBox(cx - icon / 2, cy - icon / 2, icon, icon, i == 0 ? "#1E7A6B" : "#2C4A51", null, 0, 2);
        }
    }

    private void DrawOutline(PreviewState state)
    {
        var style = state.Style;
        uint argb = state.IsFit ? OutlinePalette.Argb(style.Color) : 0xFFF5B84A;
        var color = Format.Opaque(argb);
        var (fx, fy, fw, fh) = Map(state.Frame);
        double mapW = _display.Width * _scale, mapH = _display.Height * _scale;

        if (style.DimOutside)
        {
            var dim = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Round(style.DimOpacityPercent * 2.55), 4, 10, 12));
            AddFill(_originX, _originY, mapW, Math.Max(0, fy - _originY), dim);
            AddFill(_originX, fy + fh, mapW, Math.Max(0, _originY + mapH - fy - fh), dim);
            AddFill(_originX, fy, Math.Max(0, fx - _originX), fh, dim);
            AddFill(fx + fw, fy, Math.Max(0, _originX + mapW - fx - fw), fh, dim);
        }

        double stroke = style.Thickness >= 6 ? 3 : style.Thickness >= 4 ? 2 : 1.5;
        var brush = new SolidColorBrush(color);
        var soft = new SolidColorBrush(Format.WithAlpha(argb, 110));

        if (style.ShowThirds)
        {
            for (int i = 1; i <= 2; i++)
            {
                AddFill(fx + fw * i / 3, fy, 1, fh, soft);
                AddFill(fx, fy + fh * i / 3, fw, 1, soft);
            }
        }
        if (style.ShowCenterMark)
        {
            AddFill(fx + fw / 2 - 9, fy + fh / 2, 18, 1, brush);
            AddFill(fx + fw / 2, fy + fh / 2 - 9, 1, 18, brush);
        }
        if (style.ShowSafeArea)
        {
            AddOutline(fx + fw * 0.05, fy + fh * 0.05, fw * 0.9, fh * 0.9, soft, 1, dashed: true);
        }

        if (style.Line == OutlineLine.Corners)
        {
            DrawCorners(fx, fy, fw, fh, brush, stroke);
        }
        else
        {
            AddOutline(fx, fy, fw, fh, brush, stroke, dashed: style.Line == OutlineLine.Dashed);
        }

        foreach (var nested in state.NestedFrames)
        {
            var (nx, ny, nw, nh) = Map(nested.Bounds);
            var nestedColor = Format.Opaque(OutlinePalette.Argb(nested.Color));
            var nestedBrush = new SolidColorBrush(nestedColor);
            if (style.Line == OutlineLine.Corners) DrawCorners(nx, ny, nw, nh, nestedBrush, stroke);
            else AddOutline(nx, ny, nw, nh, nestedBrush, stroke, dashed: style.Line == OutlineLine.Dashed);
            if (!nested.SafeArea.IsEmpty)
            {
                var (sx, sy, sw, sh) = Map(nested.SafeArea);
                AddOutline(sx, sy, sw, sh, nestedBrush, 1, dashed: true);
            }
            if (style.ShowSizeLabel && nw >= 68 && nh >= 26)
                AddText($"{nested.Bounds.Width} × {nested.Bounds.Height}", nx + 4, ny + 4,
                    OutlinePalette.Hex(nested.Color), 10);
        }

        if (state.ShowHandles)
        {
            foreach (var (hx, hy) in new[]
                {
                    (fx, fy), (fx + fw / 2, fy), (fx + fw, fy), (fx, fy + fh / 2),
                    (fx + fw, fy + fh / 2), (fx, fy + fh), (fx + fw / 2, fy + fh), (fx + fw, fy + fh)
                })
                AddBox(hx - 3.5, hy - 3.5, 7, 7, "#0F1E22", null, 0, 0, brush, 1.5);
        }

        if (style.ShowSizeLabel)
        {
            bool above = fy - _originY >= 20;
            AddLabel(state.SizeText, above ? fx : fx + 4, above ? fy - 19 : fy + 4, color);
        }

        if (style.ShowHud)
        {
            double hudY = fy + fh + 16 <= _originY + mapH - 16 ? fy + fh + 6 : fy + fh - 18;
            var hud = AddBox(fx + fw / 2 - 32, hudY, 64, 10, "#0B1A1E", "#24FFFFFF", 1, 4);
            _ = hud;
            AddBox(fx + fw / 2 - 26, hudY + 3.5, 20, 3, "#93AEAB", null, 0, 1.5);
            for (int i = 0; i < 3; i++) AddBox(fx + fw / 2 + i * 8, hudY + 3.5, 5, 3, "#5D7775", null, 0, 1.5);
        }
    }

    private void DrawResize(PreviewState state)
    {
        var mint = Format.BrushFromHex("#34D5B5");
        if (state.Ghost is { } ghost)
        {
            var (gx, gy, gw, gh) = Map(ghost);
            AddOutline(gx, gy, gw, gh, Format.BrushFromHex("#73B8CBC9"), 1, dashed: true);
            AddText($"Now {ghost.Width} × {ghost.Height}", gx + 6, gy + gh - 18, "#93AEAB", 10.5);
        }

        var (fx, fy, fw, fh) = Map(state.Frame);
        double titleBar = state.ShowWindowChrome ? Math.Max(4, state.TitleBarHeight * _scale) : Math.Max(4, 32 * _scale);
        double wy = state.ShowWindowChrome ? fy - titleBar : fy;
        double wh = state.ShowWindowChrome ? fh + titleBar : fh;

        AddBox(fx, wy, fw, wh, "#1C3238", "#3A565D", 1, 4);
        AddBox(fx, wy, fw, titleBar, "#27434B", null, 0, 4);
        AddBox(fx + 1, wy + titleBar, fw * 0.16, wh - titleBar - 1, "#182B30", null, 0, 0);
        double[] lines = [0.4, 0.66, 0.52, 0.74, 0.34, 0.58];
        for (int i = 0; i < lines.Length && titleBar + 14 + i * 11 < wh - 6; i++)
            AddBox(fx + fw * 0.16 + 12, wy + titleBar + 12 + i * 11, (fw * 0.84 - 24) * lines[i], 4, i % 4 == 0 ? "#3A6570" : "#2E5059", null, 0, 2);

        AddOutline(fx, fy, fw, fh, mint, 2, dashed: true);
        string label = state.CaptureLabel + " " + state.SizeText;
        AddLabel(label, fx + fw - 8 - MeasureLabel(label), fy + fh - 24, Format.ColorFromHex("#34D5B5"));
    }

    // ---- Drawing helpers --------------------------------------------------------------------

    private (double X, double Y, double W, double H) Map(DRect r) => (
        _originX + (r.X - _display.X) * _scale,
        _originY + (r.Y - _display.Y) * _scale,
        r.Width * _scale,
        r.Height * _scale);

    private Border AddBox(double x, double y, double w, double h, string fill, string? line, double thickness, double radius,
        Brush? lineBrush = null, double lineThickness = 0)
    {
        var box = new Border
        {
            Width = Math.Max(0, w),
            Height = Math.Max(0, h),
            Background = Format.BrushFromHex(fill),
            CornerRadius = new CornerRadius(radius),
            BorderBrush = lineBrush ?? (line is null ? null : Format.BrushFromHex(line)),
            BorderThickness = new Thickness(lineBrush is null ? thickness : lineThickness)
        };
        Place(box, x, y);
        return box;
    }

    private void AddFill(double x, double y, double w, double h, Brush fill)
    {
        if (w <= 0 || h <= 0) return;
        var rect = new Shapes.Rectangle { Width = w, Height = h, Fill = fill };
        Place(rect, x, y);
    }

    private void DrawCorners(double x, double y, double w, double h, Brush brush, double stroke)
    {
        double arm = Math.Clamp(Math.Min(w, h) / 5, 6, 18);
        double thickness = stroke + 0.5;
        AddFill(x, y, arm, thickness, brush); AddFill(x, y, thickness, arm, brush);
        AddFill(x + w - arm, y, arm, thickness, brush); AddFill(x + w - thickness, y, thickness, arm, brush);
        AddFill(x, y + h - thickness, arm, thickness, brush); AddFill(x, y + h - arm, thickness, arm, brush);
        AddFill(x + w - arm, y + h - thickness, arm, thickness, brush);
        AddFill(x + w - thickness, y + h - arm, thickness, arm, brush);
    }

    private void AddOutline(double x, double y, double w, double h, Brush stroke, double thickness, bool dashed)
    {
        var rect = new Shapes.Rectangle
        {
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Stroke = stroke,
            StrokeThickness = thickness
        };
        if (dashed) rect.StrokeDashArray = new DoubleCollection { 3, 2 };
        Place(rect, x, y);
    }

    private void AddLabel(string text, double x, double y, Windows.UI.Color fill)
    {
        var label = new Border
        {
            Height = 18,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(fill),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontFamily = (FontFamily)Application.Current.Resources["MonoSemiBoldFont"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Format.BrushFromHex("#06201C"),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Place(label, x, y);
    }

    private void AddText(string text, double x, double y, string color, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = Format.BrushFromHex(color) };
        Place(block, x, y);
    }

    private static double MeasureLabel(string text) => text.Length * 6.6 + 12;

    private void Place(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _canvas.Children.Add(element);
    }
}
