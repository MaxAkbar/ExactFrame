using System.ComponentModel;
using ExactFrame.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ExactFrame.Views;

/// <summary>Output sliders plus exact boxes, using the main editor's range-before-value synchronization.</summary>
public sealed partial class OutputSizeEditor : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(NestedFrameItem),
        typeof(OutputSizeEditor), new PropertyMetadata(null, (d, _) => ((OutputSizeEditor)d).ChangedItem()));
    private NestedFrameItem? _attached;
    private bool _syncing;
    public OutputSizeEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach(Item);
        Unloaded += (_, _) => Attach(null);
    }
    public NestedFrameItem? Item { get => (NestedFrameItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    private void ChangedItem()
    {
        Bindings?.Update();
        if (IsLoaded) Attach(Item);
    }
    private void Attach(NestedFrameItem? item)
    {
        if (_attached is not null) _attached.PropertyChanged -= ItemChanged;
        _attached = item;
        if (item is null) return;
        item.PropertyChanged += ItemChanged;
        Sync();
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NestedFrameItem.OutputWidth) or nameof(NestedFrameItem.OutputHeight) or "" or null) Sync();
    }
    private void Sync()
    {
        if (_attached is not { HasOutput: true } item) return;
        _syncing = true;
        try
        {
            WidthSlider.Maximum = Math.Max(4096, item.OutputWidth);
            HeightSlider.Maximum = Math.Max(4096, item.OutputHeight);
            WidthSlider.Value = item.OutputWidth;
            HeightSlider.Value = item.OutputHeight;
        }
        finally { _syncing = false; }
    }
    private void OnWidthChanged(object sender, RangeBaseValueChangedEventArgs e) { if (!_syncing && _attached is { } item) item.OutputWidth = e.NewValue; }
    private void OnHeightChanged(object sender, RangeBaseValueChangedEventArgs e) { if (!_syncing && _attached is { } item) item.OutputHeight = e.NewValue; }
}
