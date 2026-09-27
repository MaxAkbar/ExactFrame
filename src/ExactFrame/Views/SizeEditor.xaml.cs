using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ExactFrame.Core.ViewModels;

namespace ExactFrame.Views;

/// <summary>
/// Width and height editor used by the Frame tab and Resize window mode.
/// The number boxes use x:Bind. The sliders are synced here instead, because a Slider clamps its Value to
/// its range: if a new value arrived before a new maximum, the clamped value would be written back and
/// silently change the frame. Syncing sets the maximum first and ignores the slider's own echoes.
/// </summary>
public sealed partial class SizeEditor : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MainViewModel), typeof(SizeEditor), new PropertyMetadata(null, OnViewModelChanged));

    private MainViewModel? _attached;
    private bool _syncing;

    public SizeEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach(ViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (SizeEditor)d;
        editor.Bindings?.Update();
        if (editor.IsLoaded) editor.Attach(e.NewValue as MainViewModel);
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (_attached is not null) _attached.PropertyChanged -= OnViewModelPropertyChanged;
        _attached = viewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncSliders();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.WidthValue):
            case nameof(MainViewModel.HeightValue):
            case nameof(MainViewModel.WidthSliderMaximum):
            case nameof(MainViewModel.HeightSliderMaximum):
            case "":
            case null:
                SyncSliders();
                break;
        }
    }

    private void SyncSliders()
    {
        if (_attached is not { } viewModel) return;
        _syncing = true;
        try
        {
            WidthSlider.Maximum = viewModel.WidthSliderMaximum;
            HeightSlider.Maximum = viewModel.HeightSliderMaximum;
            WidthSlider.Value = viewModel.WidthValue;
            HeightSlider.Value = viewModel.HeightValue;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnWidthSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing) _attached?.SetWidthFromSlider(e.NewValue);
    }

    private void OnHeightSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing) _attached?.SetHeightFromSlider(e.NewValue);
    }
}
