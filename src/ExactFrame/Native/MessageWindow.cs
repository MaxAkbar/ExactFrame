using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// A hidden top-level window that receives WM_HOTKEY and display-change broadcasts.
/// (Message-only windows don't receive broadcasts, so this one is a real, never-shown popup.)
/// </summary>
internal sealed class MessageWindow : NativeWindow
{
    public MessageWindow() => CreateHandle(NativeMethods.WsExToolWindow, NativeMethods.WsPopup);

    public event EventHandler<int>? HotkeyPressed;

    public event EventHandler? DisplaysChanged;

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
        }
        return base.WndProc(hwnd, message, wParam, lParam);
    }
}
