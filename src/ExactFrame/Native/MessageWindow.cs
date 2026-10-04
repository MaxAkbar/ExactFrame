using System.Runtime.InteropServices;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// A hidden top-level window that receives WM_HOTKEY and display-change broadcasts, and the command line of a second
/// launch (see <see cref="SingleInstance"/>). Message-only windows don't receive broadcasts, so this one is a real,
/// never-shown popup. Its title lets a second ExactFrame find it.
/// </summary>
internal sealed class MessageWindow : NativeWindow
{
    internal const string Title = "ExactFrame.MessageWindow";

    /// <summary>Marks WM_COPYDATA sent by <see cref="SingleInstance"/>, so other data is ignored.</summary>
    internal const nint LaunchDataId = 0x45464C31; // "EFL1"

    public MessageWindow() => CreateHandle(NativeMethods.WsExToolWindow, NativeMethods.WsPopup, name: Title);

    public event EventHandler<int>? HotkeyPressed;

    public event EventHandler? DisplaysChanged;

    /// <summary>Raised with the arguments of a second launch, which then exits.</summary>
    public event EventHandler<IReadOnlyList<string>>? LaunchForwarded;

    protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmHotkey:
                HotkeyPressed?.Invoke(this, (int)wParam);
                return 0;
            case NativeMethods.WmDisplayChange:
                DisplaysChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WmSettingChange when (int)wParam == NativeMethods.SpiSetWorkArea:
                // The taskbar moved or changed size, so work areas changed.
                DisplaysChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WmCopyData when lParam != 0:
                var data = Marshal.PtrToStructure<NativeMethods.CopyData>(lParam);
                if (data.Id != LaunchDataId) break;
                string text = data.Data == 0 ? string.Empty : Marshal.PtrToStringUni(data.Data, (int)(data.Size / 2)).TrimEnd('\0');
                LaunchForwarded?.Invoke(this, text.Length == 0 ? [] : text.Split('\0'));
                return 1;
        }
        return base.WndProc(hwnd, message, wParam, lParam);
    }
}
