using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ExactFrame.Controls;
using ExactFrame.Core.Models;
using ExactFrame.Core.Services;
using ExactFrame.Helpers;
using Windows.System;
using Windows.UI.Core;

namespace ExactFrame.Services;

/// <summary>ContentDialog-based prompts shown over the main window.</summary>
internal sealed class DialogService : IDialogService
{
    private Func<XamlRoot?>? _root;

    /// <summary>Connects the service to the window that hosts the dialogs.</summary>
    public void Attach(Func<XamlRoot?> root) => _root = root;

    public async Task<string?> PromptProfileNameAsync(string suggestion)
    {
        var name = new TextBox { Text = suggestion, PlaceholderText = "Profile name", MaxLength = 60 };
        name.Loaded += (_, _) =>
        {
            name.Focus(FocusState.Programmatic);
            name.SelectAll();
        };

        var dialog = Create("Save profile", "Save", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Caption("Saves the size, position, display and mode. Using an existing name updates that profile."),
                name
            }
        });
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);

        return await ShowAsync(dialog) == ContentDialogResult.Primary ? name.Text.Trim() : null;
    }

    public async Task<HotkeyGesture?> RecordHotkeyAsync(string actionName, HotkeyGesture current)
    {
        HotkeyGesture? recorded = null;
        var keys = new KeyChips { Keys = current.Keys, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = Caption("Press the new shortcut. Use Ctrl, Alt or Win with another key.");

        var dialog = Create(actionName, "Use shortcut", new StackPanel { Spacing = 14, Children = { hint, keys } });
        dialog.IsPrimaryButtonEnabled = false;
        dialog.PreviewKeyDown += (_, e) =>
        {
            int key = (int)e.Key;
            if (e.Key is VirtualKey.Escape or VirtualKey.Tab or VirtualKey.Enter || HotkeyGesture.IsModifierKey(key)) return;

            var gesture = new HotkeyGesture(CurrentModifiers(), key);
            keys.Keys = gesture.Keys;
            recorded = gesture;
            dialog.IsPrimaryButtonEnabled = gesture.IsValid;
            hint.Text = gesture.IsValid
                ? "Press another shortcut to change it, or choose Use shortcut."
                : "Add Ctrl, Alt or Win so typing never triggers it.";
            e.Handled = true;
        };

        return await ShowAsync(dialog) == ContentDialogResult.Primary ? recorded : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var dialog = Create(title, confirmLabel, Caption(message));
        dialog.DefaultButton = ContentDialogButton.Close;
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private ContentDialog Create(string title, string primary, UIElement content) => new()
    {
        Title = title,
        Content = content,
        PrimaryButtonText = primary,
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = _root?.Invoke(),
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
    };

    private static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (dialog.XamlRoot is null) return ContentDialogResult.None;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (COMException)
        {
            // Another dialog is already open.
            return ContentDialogResult.None;
        }
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Format.BrushFromHex("#5E706E")
    };

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Windows;
        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
