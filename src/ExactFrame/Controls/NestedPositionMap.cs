using System.ComponentModel;
using ExactFrame.Core.Models;
using ExactFrame.Core.ViewModels;
using ExactFrame.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace ExactFrame.Controls;

/// <summary>A small parent-relative editor for moving one composition crop.</summary>
public sealed class NestedPositionMap : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(object),
        typeof(NestedPositionMap), new PropertyMetadata(null, (d, e) => ((NestedPositionMap)d).SetItem(e.OldValue as NestedFrameItem)));
    private readonly Canvas _canvas = new() { Background = Format.BrushFromHex("#12262B") };
    private NestedFrame? _original;
    private NestedFrame? _expected;
    private System.Drawing.Rectangle _parent;
    private Windows.Foundation.Point _start;
    private double _x, _y, _scale, _left, _top;
    private uint _pointer;
    private bool _dragging;

    public NestedPositionMap()
    {
        Height = 130;
        Content = _canvas;
        IsTabStop = true;
        AutomationProperties.SetName(this, "Composition crop position editor");
        AutomationProperties.SetHelpText(this, "Drag the colored crop to position it freely inside the main frame. Arrow keys move one pixel; Shift moves ten. Escape cancels a drag.");
        SizeChanged += (_, _) => { Finish(true); Draw(); };
        Loaded += (_, _) => { Subscribe(); Draw(); };
        Unloaded += (_, _) => { Finish(true); Unsubscribe(Item as NestedFrameItem); };
        PointerPressed += Pressed;
        PointerMoved += Moved;
        PointerReleased += Released;
        PointerCanceled += (_, _) => Finish(true);
        PointerCaptureLost += (_, _) => Finish(true);
        LostFocus += (_, _) => Finish(true);
        KeyDown += KeyPressed;
    }

    public object? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool IsInteracting => _dragging;
    public void CancelInteraction() => Finish(true);
    private bool _subscribed;
    private void Subscribe()
    {
        if (_subscribed || Item is not NestedFrameItem item) return;
        item.PropertyChanged += Changed;
        _subscribed = true;
    }
    private void Unsubscribe(NestedFrameItem? item)
    {
        if (_subscribed && item is not null) item.PropertyChanged -= Changed;
        _subscribed = false;
    }
    private void SetItem(NestedFrameItem? old)
    {
        // A recycled row must never restore its old gesture onto a different composition.
        _dragging = false;
        ReleasePointerCaptures();
        Unsubscribe(old);
        if (IsLoaded) Subscribe();
        Draw();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_dragging && Item is NestedFrameItem item && item.ParentBounds != _parent) Finish(true);
        Draw();
    }
    private void Draw()
    {
        _canvas.Children.Clear();
        if (Item is not NestedFrameItem item || item.ParentBounds.Width <= 0 || ActualWidth <= 0) return;
        var parent = item.ParentBounds;
        _scale = Math.Min((ActualWidth - 16) / parent.Width, (Height - 16) / parent.Height);
        if (_scale <= 0) return;
        _left = (ActualWidth - parent.Width * _scale) / 2;
        _top = (Height - parent.Height * _scale) / 2;
        Box(_left, _top, parent.Width * _scale, parent.Height * _scale, "#1F3940", "#688986");
        var crop = item.Bounds;
        if (crop.IsEmpty) return;
        Box(_left + (crop.X - parent.X) * _scale, _top + (crop.Y - parent.Y) * _scale,
            crop.Width * _scale, crop.Height * _scale, "#344E50", item.ColorHex);
    }
    private void Box(double x, double y, double width, double height, string fill, string stroke)
    {
        var box = new Shapes.Rectangle { Width = width, Height = height, Fill = Format.BrushFromHex(fill), Stroke = Format.BrushFromHex(stroke), StrokeThickness = 1.5 };
        Canvas.SetLeft(box, x); Canvas.SetTop(box, y); _canvas.Children.Add(box);
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed) { Finish(true); e.Handled = true; return; }
        if (!point.Properties.IsLeftButtonPressed || Item is not NestedFrameItem item || item.Bounds.IsEmpty || _scale <= 0) return;
        double x = (point.Position.X - _left) / _scale + item.ParentBounds.X;
        double y = (point.Position.Y - _top) / _scale + item.ParentBounds.Y;
        if (x < item.Bounds.Left || x >= item.Bounds.Right || y < item.Bounds.Top || y >= item.Bounds.Bottom) return;
        Focus(FocusState.Pointer);
        if (!CapturePointer(e.Pointer)) return;
        _original = _expected = item.Frame;
        _parent = item.ParentBounds;
        _x = item.XPosition; _y = item.YPosition;
        _start = point.Position; _pointer = e.Pointer.PointerId; _dragging = true;
        e.Handled = true;
    }
    private void Move(PointerRoutedEventArgs e)
    {
        if (!_dragging || e.Pointer.PointerId != _pointer || Item is not NestedFrameItem item || _scale <= 0) return;
        // Do not overwrite a new numeric/preset edit that interrupted this drag.
        if (item.Frame != _expected || item.ParentBounds != _parent) { Finish(false); return; }
        var point = e.GetCurrentPoint(this).Position;
        item.SetPosition(_x + (point.X - _start.X) / _scale, _y + (point.Y - _start.Y) / _scale);
        _expected = item.Frame;
    }
    private void Moved(object sender, PointerRoutedEventArgs e) { Move(e); if (_dragging) e.Handled = true; }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || e.Pointer.PointerId != _pointer) return;
        Move(e); Finish(false); e.Handled = true;
    }
    private void Finish(bool cancel)
    {
        if (!_dragging) return;
        _dragging = false;
        if (cancel && Item is NestedFrameItem item && item.Frame == _expected && _original is not null && item.ParentBounds == _parent)
            item.RestoreFrame(_original);
        ReleasePointerCaptures();
    }
    private void KeyPressed(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { Finish(true); e.Handled = true; return; }
        if (_dragging || Item is not NestedFrameItem item) return;
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0 ||
            (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0) return;
        int step = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0 ? 10 : 1;
        int dx = e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0;
        int dy = e.Key == VirtualKey.Up ? -step : e.Key == VirtualKey.Down ? step : 0;
        if (dx == 0 && dy == 0) return;
        item.SetPosition(item.XPosition + dx, item.YPosition + dy); e.Handled = true;
    }
}
