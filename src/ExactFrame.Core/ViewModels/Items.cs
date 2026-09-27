using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;

namespace ExactFrame.Core.ViewModels;

/// <summary>Base for anything shown as one choice in a group (chips, swatches, list rows).</summary>
public abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    protected SelectableItem(Action select) => SelectCommand = new RelayCommand(select);

    public IRelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>A labelled choice, such as a size preset or a segmented-control option.</summary>
public sealed class ChoiceItem(string key, string label, Action select, string detail = "") : SelectableItem(select)
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public string Detail { get; } = detail;
}

public sealed class AnchorItem(FrameAnchor anchor, Action select) : SelectableItem(select)
{
    public FrameAnchor Anchor { get; } = anchor;

    public string Label { get; } = anchor.DisplayName();
}

public sealed class SwatchItem(OutlineColor color, Action select) : SelectableItem(select)
{
    public OutlineColor Color { get; } = color;

    public string Name { get; } = OutlinePalette.Name(color);

    public string Hex { get; } = OutlinePalette.Hex(color);
}

public sealed class DisplayItem(DisplayInfo display, double glyphScale, Action select) : SelectableItem(select)
{
    public DisplayInfo Display { get; } = display;

    public string Title { get; } = display.Title + (display.IsPrimary ? " · Primary" : "");

    public string Resolution { get; } = $"{display.Bounds.Width} × {display.Bounds.Height} · {display.ScalePercent}%";

    public double GlyphWidth { get; } = Math.Max(8, Math.Round(display.Bounds.Width * glyphScale));

    public double GlyphHeight { get; } = Math.Max(8, Math.Round(display.Bounds.Height * glyphScale));

    public string DeviceName { get; } = display.DeviceName;
}

public sealed class WindowItem(WindowInfo window, Action select) : SelectableItem(select)
{
    private static readonly string[] Tints = ["#2F6FB0", "#6B4FB8", "#33464B", "#0E6B60", "#8A4B2A", "#8A2F5C"];

    public WindowInfo Window { get; } = window;

    public string Title { get; } = window.Title;

    public string Meta { get; } = window.IsMinimized
        ? $"{window.ProcessName}.exe · minimized · {window.Bounds.Width} × {window.Bounds.Height}"
        : $"{window.ProcessName}.exe · {window.Bounds.Width} × {window.Bounds.Height}";

    public string Initials { get; } = MakeInitials(window.ProcessName);

    public string Tint { get; } = Tints[Math.Abs(StableHash(window.ProcessName)) % Tints.Length];

    private static string MakeInitials(string processName)
    {
        var letters = processName.Where(char.IsLetterOrDigit).Take(2).ToArray();
        return letters.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(letters[0]).ToString(),
            _ => $"{char.ToUpperInvariant(letters[0])}{char.ToLowerInvariant(letters[1])}"
        };
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value.ToLowerInvariant()) hash = hash * 31 + c;
            return hash == int.MinValue ? 0 : hash;
        }
    }
}

public sealed class ProfileItem : SelectableItem
{
    public ProfileItem(FrameProfile profile, string summary, IReadOnlyList<string> keys, bool hotkeyRegistered,
        Action apply, Func<Task> delete) : base(apply)
    {
        Profile = profile;
        Summary = summary;
        Keys = keys;
        HotkeyRegistered = hotkeyRegistered;
        DeleteCommand = new AsyncRelayCommand(delete);

        double ratio = profile.Height == 0 ? 1 : (double)profile.Width / profile.Height;
        GlyphWidth = ratio >= 1 ? 22 : Math.Round(22 * ratio);
        GlyphHeight = ratio >= 1 ? Math.Round(22 / ratio) : 22;
    }

    public FrameProfile Profile { get; }

    public string Name => Profile.Name;

    public string Summary { get; }

    public IReadOnlyList<string> Keys { get; }

    public bool HasKeys => Keys.Count > 0;

    public bool HotkeyRegistered { get; }

    public double KeysOpacity => HotkeyRegistered ? 1 : 0.45;

    public string HotkeyTip => !HasKeys
        ? "Only the first nine profiles get a shortcut."
        : HotkeyRegistered ? "Applies this profile from any app." : "Windows couldn’t register this shortcut; another app may be using it.";

    public double GlyphWidth { get; }

    public double GlyphHeight { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}

public sealed class HotkeyItem(string label, HotkeyGesture gesture, bool isRegistered, Func<Task> change) : ObservableObject
{
    public string Label { get; } = label;

    public HotkeyGesture Gesture { get; } = gesture;

    public IReadOnlyList<string> Keys { get; } = gesture.Keys;

    public bool IsRegistered { get; } = isRegistered;

    public bool HasConflict => !IsRegistered;

    public IAsyncRelayCommand ChangeCommand { get; } = new AsyncRelayCommand(change);
}
